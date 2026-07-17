using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;

internal sealed record ExternalProjectOptions(
    string ProjectPath,
    string StartupProjectPath,
    string Configuration,
    string WorkingDirectory);

internal sealed record ExternalDbContextDescriptor(
    string DisplayName,
    string FullName,
    Func<DbContext> Factory);

internal sealed class ExternalProjectLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class ExternalProjectDbContextLoader
{
    private const string DesignTimeFactoryInterfaceName = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";
    private const string MetadataConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=DotErdModel;Trusted_Connection=True;TrustServerCertificate=True";

    public static IReadOnlyList<ExternalDbContextDescriptor> Discover(ExternalProjectOptions options)
    {
        var loadResult = LoadProject(options);
        var contextTypes = GetLoadableTypes(loadResult.Assemblies)
            .Where(type => !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return contextTypes
            .Select(type => CreateDescriptor(type, loadResult.Assemblies))
            .ToArray();
    }

    public static ExternalDbContextDescriptor Resolve(ExternalProjectOptions options, string? contextName)
    {
        var contexts = Discover(options);
        if (contexts.Count == 0)
        {
            throw new ExternalProjectLoadException("No EF Core DbContext types were found in the target project or startup project.");
        }

        if (string.IsNullOrWhiteSpace(contextName))
        {
            if (contexts.Count == 1)
            {
                return contexts[0];
            }

            throw new ExternalProjectLoadException("Multiple DbContext types were found. Pass --context <name>.");
        }

        var matches = contexts
            .Where(context =>
                string.Equals(context.DisplayName, contextName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.FullName, contextName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ExternalProjectLoadException("Requested DbContext was not found in the target project or startup project."),
            _ => throw new ExternalProjectLoadException("Requested DbContext name is ambiguous. Use the fully qualified context name.")
        };
    }

    private static ExternalProjectLoadResult LoadProject(ExternalProjectOptions options)
    {
        EnsureProjectFile(options.ProjectPath, "--project");
        EnsureProjectFile(options.StartupProjectPath, "--startup-project");

        BuildProject(options.ProjectPath, options);
        if (!string.Equals(options.ProjectPath, options.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            BuildProject(options.StartupProjectPath, options);
        }

        var projectAssemblyPath = GetTargetPath(options.ProjectPath, options);
        var startupAssemblyPath = GetTargetPath(options.StartupProjectPath, options);
        if (!File.Exists(projectAssemblyPath))
        {
            throw new ExternalProjectLoadException($"Built project assembly was not found: {projectAssemblyPath}");
        }

        if (!File.Exists(startupAssemblyPath))
        {
            throw new ExternalProjectLoadException($"Built startup assembly was not found: {startupAssemblyPath}");
        }

        var loadContext = new ExternalProjectAssemblyLoadContext(startupAssemblyPath);
        var assemblies = new List<Assembly>
        {
            loadContext.LoadFromAssemblyPath(startupAssemblyPath)
        };

        if (!string.Equals(projectAssemblyPath, startupAssemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            assemblies.Add(loadContext.LoadFromAssemblyPath(projectAssemblyPath));
        }

        EnsureSupportedEfCoreVersion(assemblies);

        return new ExternalProjectLoadResult(loadContext, assemblies);
    }

    private static ExternalDbContextDescriptor CreateDescriptor(Type contextType, IReadOnlyList<Assembly> assemblies)
    {
        return new ExternalDbContextDescriptor(
            contextType.Name,
            contextType.FullName ?? contextType.Name,
            () => CreateDbContext(contextType, assemblies));
    }

    private static DbContext CreateDbContext(Type contextType, IReadOnlyList<Assembly> assemblies)
    {
        try
        {
            var factory = TryCreateFromDesignTimeFactory(contextType, assemblies);
            if (factory is not null)
            {
                return factory;
            }

            var parameterlessConstructor = contextType.GetConstructor(Type.EmptyTypes);
            if (parameterlessConstructor is not null)
            {
                return (DbContext)parameterlessConstructor.Invoke(null);
            }

            var optionsConstructor = contextType.GetConstructors()
                .Select(constructor => new
                {
                    Constructor = constructor,
                    Parameters = constructor.GetParameters()
                })
                .FirstOrDefault(candidate =>
                    candidate.Parameters.Length == 1
                    && typeof(DbContextOptions).IsAssignableFrom(candidate.Parameters[0].ParameterType));

            if (optionsConstructor is null)
            {
                throw new ExternalProjectLoadException(
                    $"Unable to create DbContext '{contextType.FullName}'. Add an IDesignTimeDbContextFactory<TContext>, a parameterless constructor, or a constructor that accepts DbContextOptions<TContext>.");
            }

            return (DbContext)optionsConstructor.Constructor.Invoke([CreateSqlServerOptions(contextType)]);
        }
        catch (ExternalProjectLoadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExternalProjectLoadException($"Unable to create DbContext '{contextType.FullName}'.", exception);
        }
    }

    private static DbContext? TryCreateFromDesignTimeFactory(Type contextType, IReadOnlyList<Assembly> assemblies)
    {
        var factoryType = GetLoadableTypes(assemblies)
            .Where(type => !type.IsAbstract)
            .FirstOrDefault(type => type.GetInterfaces().Any(candidate =>
                candidate.IsGenericType
                && string.Equals(candidate.GetGenericTypeDefinition().FullName, DesignTimeFactoryInterfaceName, StringComparison.Ordinal)
                && candidate.GetGenericArguments()[0] == contextType));

        if (factoryType is null)
        {
            return null;
        }

        var factory = Activator.CreateInstance(factoryType)
            ?? throw new ExternalProjectLoadException($"Unable to create design-time factory '{factoryType.FullName}'.");
        var method = factoryType.GetMethod("CreateDbContext", [typeof(string[])])
            ?? throw new ExternalProjectLoadException($"Design-time factory '{factoryType.FullName}' does not expose CreateDbContext(string[]).");

        try
        {
            return (DbContext?)method.Invoke(factory, [Array.Empty<string>()])
                ?? throw new ExternalProjectLoadException($"Design-time factory '{factoryType.FullName}' returned null.");
        }
        catch (TargetInvocationException exception)
        {
            throw new ExternalProjectLoadException($"Design-time factory '{factoryType.FullName}' failed to create the DbContext.", exception);
        }
    }

    private static object CreateSqlServerOptions(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)(Activator.CreateInstance(builderType)
            ?? throw new ExternalProjectLoadException($"Unable to create DbContextOptionsBuilder for '{contextType.FullName}'."));

        builder.UseSqlServer(MetadataConnectionString);

        return builderType.GetProperty(nameof(DbContextOptionsBuilder.Options), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(builder)
            ?? throw new ExternalProjectLoadException($"Unable to create DbContextOptions for '{contextType.FullName}'.");
    }

    private static IEnumerable<Type> GetLoadableTypes(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                yield return type;
            }
        }
    }

    private static void EnsureSupportedEfCoreVersion(IEnumerable<Assembly> assemblies)
    {
        var efCoreReferences = assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Where(reference => string.Equals(reference.Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(reference => reference.Version?.Major)
            .Where(major => major is not null)
            .Distinct()
            .ToArray();

        foreach (var major in efCoreReferences)
        {
            if (major != DotErdVersionSupport.SupportedEfCoreMajor)
            {
                throw new ExternalProjectLoadException(
                    $"Unsupported EF Core version {major}.x in the target project. D400.DotErd.Tool 0.1.0-beta.1 supports EF Core {DotErdVersionSupport.SupportedEfCoreMajor}.x.");
            }
        }
    }

    private static void EnsureProjectFile(string path, string optionName)
    {
        if (!File.Exists(path))
        {
            throw new ExternalProjectLoadException($"{optionName} file was not found: {path}");
        }

        if (!string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExternalProjectLoadException($"{optionName} must point to a .csproj file: {path}");
        }
    }

    private static void BuildProject(string projectPath, ExternalProjectOptions options)
    {
        var result = RunDotnet(options.WorkingDirectory, "build", projectPath, "-c", options.Configuration, "--nologo");
        if (result.ExitCode != 0)
        {
            throw new ExternalProjectLoadException($"Failed to build project '{projectPath}'.{Environment.NewLine}{result.AllOutput}");
        }
    }

    private static string GetTargetPath(string projectPath, ExternalProjectOptions options)
    {
        var result = RunDotnet(
            options.WorkingDirectory,
            "msbuild",
            projectPath,
            "-getProperty:TargetPath",
            $"-p:Configuration={options.Configuration}",
            "-nologo");

        if (result.ExitCode != 0)
        {
            throw new ExternalProjectLoadException($"Unable to resolve target assembly for '{projectPath}'.{Environment.NewLine}{result.AllOutput}");
        }

        var targetPath = result.StandardOutput
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        var targetFramework = ResolveTargetFramework(projectPath);
        return Path.Combine(
            Path.GetDirectoryName(projectPath) ?? options.WorkingDirectory,
            "bin",
            options.Configuration,
            targetFramework,
            $"{Path.GetFileNameWithoutExtension(projectPath)}.dll");
    }

    private static string ResolveTargetFramework(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        var targetFramework = ReadTargetFramework(projectPath);
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            return targetFramework;
        }

        var directory = new DirectoryInfo(projectDirectory);
        while (directory is not null)
        {
            var propsPath = Path.Combine(directory.FullName, "Directory.Build.props");
            targetFramework = ReadTargetFramework(propsPath);
            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                return targetFramework;
            }

            directory = directory.Parent;
        }

        return "net10.0";
    }

    private static string? ReadTargetFramework(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(path);
            return document.Descendants("TargetFramework").FirstOrDefault()?.Value
                ?? document.Descendants("TargetFrameworks").FirstOrDefault()?.Value.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static ProcessResult RunDotnet(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new ExternalProjectLoadException("Unable to start dotnet.");
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardOutput.AppendLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardError.AppendLine(eventArgs.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            TryKill(process);
            throw new ExternalProjectLoadException($"dotnet {string.Join(' ', arguments)} timed out.", exception);
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record ExternalProjectLoadResult(AssemblyLoadContext LoadContext, IReadOnlyList<Assembly> Assemblies);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;
    }

    private sealed class ExternalProjectAssemblyLoadContext(string startupAssemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver resolver = new(startupAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (ShouldShareWithTool(assemblyName.Name))
            {
                return null;
            }

            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }

        private static bool ShouldShareWithTool(string? assemblyName)
        {
            return assemblyName is null
                || assemblyName.Equals("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Logging", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Options", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Primitives", StringComparison.Ordinal);
        }
    }
}

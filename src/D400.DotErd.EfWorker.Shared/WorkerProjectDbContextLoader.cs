using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace D400.DotErd.EfWorker.Shared;

internal sealed record WorkerProjectOptions(
    string ProjectPath,
    string StartupProjectPath,
    string Configuration);

internal sealed record WorkerDbContextDescriptor(
    string DisplayName,
    string FullName,
    Func<DbContext> Factory);

internal sealed class WorkerProjectLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class WorkerProjectDbContextLoader
{
    private const string DesignTimeFactoryInterfaceName = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    public static IReadOnlyList<WorkerDbContextDescriptor> Discover(WorkerProjectOptions options)
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

    public static WorkerDbContextDescriptor Resolve(WorkerProjectOptions options, string? contextName)
    {
        var contexts = Discover(options);
        if (contexts.Count == 0)
        {
            throw new WorkerProjectLoadException("No EF Core DbContext types were found in the target project or startup project.");
        }

        if (string.IsNullOrWhiteSpace(contextName))
        {
            if (contexts.Count == 1)
            {
                return contexts[0];
            }

            throw new WorkerProjectLoadException("Multiple DbContext types were found. Pass --context <name>.");
        }

        var matches = contexts
            .Where(context =>
                string.Equals(context.DisplayName, contextName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.FullName, contextName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new WorkerProjectLoadException("Requested DbContext was not found in the target project or startup project."),
            _ => throw new WorkerProjectLoadException("Requested DbContext name is ambiguous. Use the fully qualified context name.")
        };
    }

    private static WorkerProjectLoadResult LoadProject(WorkerProjectOptions options)
    {
        EnsureProjectFile(options.ProjectPath, "--project");
        EnsureProjectFile(options.StartupProjectPath, "--startup-project");

        var projectAssemblyPath = GetTargetPath(options.ProjectPath, options);
        var startupAssemblyPath = GetTargetPath(options.StartupProjectPath, options);
        if (!File.Exists(projectAssemblyPath))
        {
            throw new WorkerProjectLoadException($"Built project assembly was not found: {projectAssemblyPath}");
        }

        if (!File.Exists(startupAssemblyPath))
        {
            throw new WorkerProjectLoadException($"Built startup assembly was not found: {startupAssemblyPath}");
        }

        var loadContext = new WorkerProjectAssemblyLoadContext(startupAssemblyPath);
        var assemblies = new List<Assembly>
        {
            loadContext.LoadFromAssemblyPath(startupAssemblyPath)
        };

        if (!string.Equals(projectAssemblyPath, startupAssemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            assemblies.Add(loadContext.LoadFromAssemblyPath(projectAssemblyPath));
        }

        return new WorkerProjectLoadResult(loadContext, assemblies);
    }

    private static WorkerDbContextDescriptor CreateDescriptor(Type contextType, IReadOnlyList<Assembly> assemblies)
    {
        return new WorkerDbContextDescriptor(
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
                throw new WorkerProjectLoadException(
                    $"Unable to create DbContext '{contextType.FullName}'. Add an IDesignTimeDbContextFactory<TContext>, a parameterless constructor, or a constructor that accepts DbContextOptions<TContext>.");
            }

            return (DbContext)optionsConstructor.Constructor.Invoke([CreateSqlServerOptions(contextType)]);
        }
        catch (WorkerProjectLoadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WorkerProjectLoadException($"Unable to create DbContext '{contextType.FullName}'.", UnwrapInvocationException(exception));
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
            ?? throw new WorkerProjectLoadException($"Unable to create design-time factory '{factoryType.FullName}'.");
        var method = factoryType.GetMethod("CreateDbContext", [typeof(string[])])
            ?? throw new WorkerProjectLoadException($"Design-time factory '{factoryType.FullName}' does not expose CreateDbContext(string[]).");

        try
        {
            return (DbContext?)method.Invoke(factory, [Array.Empty<string>()])
                ?? throw new WorkerProjectLoadException($"Design-time factory '{factoryType.FullName}' returned null.");
        }
        catch (TargetInvocationException exception)
        {
            throw new WorkerProjectLoadException($"Design-time factory '{factoryType.FullName}' failed to create the DbContext.", UnwrapInvocationException(exception));
        }
    }

    private static Exception UnwrapInvocationException(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: { } innerException }
            ? innerException
            : exception;
    }

    private static object CreateSqlServerOptions(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)(Activator.CreateInstance(builderType)
            ?? throw new WorkerProjectLoadException($"Unable to create DbContextOptionsBuilder for '{contextType.FullName}'."));

        builder.UseSqlServer(new SqlConnection());

        return builderType.GetProperty(nameof(DbContextOptionsBuilder.Options), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(builder)
            ?? throw new WorkerProjectLoadException($"Unable to create DbContextOptions for '{contextType.FullName}'.");
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

    private static void EnsureProjectFile(string path, string optionName)
    {
        if (!File.Exists(path))
        {
            throw new WorkerProjectLoadException($"{optionName} file was not found: {path}");
        }

        if (!string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkerProjectLoadException($"{optionName} must point to a .csproj file: {path}");
        }
    }

    private static string GetTargetPath(string projectPath, WorkerProjectOptions options)
    {
        var result = RunDotnet(
            Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
            "msbuild",
            projectPath,
            "-getProperty:TargetPath",
            $"-p:Configuration={options.Configuration}",
            "-nologo");

        if (result.ExitCode != 0)
        {
            throw new WorkerProjectLoadException($"Unable to resolve target assembly for '{projectPath}'.");
        }

        var targetPath = result.StandardOutput
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        throw new WorkerProjectLoadException($"Unable to resolve target assembly for '{projectPath}'.");
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
            ?? throw new WorkerProjectLoadException("Unable to start dotnet.");
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
            throw new WorkerProjectLoadException($"dotnet {string.Join(' ', arguments)} timed out.", exception);
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

    private sealed record WorkerProjectLoadResult(AssemblyLoadContext LoadContext, IReadOnlyList<Assembly> Assemblies);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class WorkerProjectAssemblyLoadContext(string startupAssemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver resolver = new(startupAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (ShouldShareWithWorker(assemblyName.Name))
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

        private static bool ShouldShareWithWorker(string? assemblyName)
        {
            return assemblyName is null
                || assemblyName.Equals("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Logging", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Options", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Extensions.Primitives", StringComparison.Ordinal)
                || assemblyName.Equals("Microsoft.Net.Http.Headers", StringComparison.Ordinal);
        }
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using D400.DotErd.Contracts;

internal sealed record ExternalProjectOptions(
    string ProjectPath,
    string StartupProjectPath,
    string Configuration,
    string WorkingDirectory);

internal sealed record ExternalDbContextDescriptor(
    string DisplayName,
    string FullName);

internal sealed record ExternalSchemaExtractionResult(
    ExternalDbContextDescriptor Context,
    DatabaseModelDto Model);

internal sealed class ExternalProjectLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class ExternalProjectDbContextLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<ExternalDbContextDescriptor> Discover(ExternalProjectOptions options)
    {
        var major = PrepareAndDetectEfCoreMajor(options);
        return RunListContexts(major, options);
    }

    public static ExternalSchemaExtractionResult Extract(ExternalProjectOptions options, string? contextName)
    {
        var major = PrepareAndDetectEfCoreMajor(options);
        var contexts = RunListContexts(major, options);
        var context = ResolveContext(contexts, contextName);
        var outputPath = CreateTemporaryJsonPath();

        try
        {
            RunWorker(
                major,
                options,
                "--command",
                "extract",
                "--project",
                options.ProjectPath,
                "--startup-project",
                options.StartupProjectPath,
                "--configuration",
                options.Configuration,
                "--context",
                context.FullName,
                "--database-name",
                context.DisplayName.Replace("DbContext", string.Empty, StringComparison.Ordinal),
                "--output",
                outputPath);

            var model = JsonSerializer.Deserialize<DatabaseModelDto>(File.ReadAllText(outputPath), JsonOptions)
                ?? throw new ExternalProjectLoadException("The EF Core worker returned an empty schema model.");
            return new ExternalSchemaExtractionResult(context, model);
        }
        finally
        {
            DeleteTemporaryFile(outputPath);
        }
    }

    private static IReadOnlyList<ExternalDbContextDescriptor> RunListContexts(int major, ExternalProjectOptions options)
    {
        var outputPath = CreateTemporaryJsonPath();
        try
        {
            RunWorker(
                major,
                options,
                "--command",
                "list-contexts",
                "--project",
                options.ProjectPath,
                "--startup-project",
                options.StartupProjectPath,
                "--configuration",
                options.Configuration,
                "--output",
                outputPath);

            var contextList = JsonSerializer.Deserialize<WorkerContextListDto>(File.ReadAllText(outputPath), JsonOptions)
                ?? throw new ExternalProjectLoadException("The EF Core worker returned an empty context list.");
            return contextList.Contexts
                .Select(context => new ExternalDbContextDescriptor(context.DisplayName, context.FullName))
                .ToArray();
        }
        finally
        {
            DeleteTemporaryFile(outputPath);
        }
    }

    private static ExternalDbContextDescriptor ResolveContext(IReadOnlyList<ExternalDbContextDescriptor> contexts, string? contextName)
    {
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

    private static int PrepareAndDetectEfCoreMajor(ExternalProjectOptions options)
    {
        EnsureProjectFile(options.ProjectPath, "--project");
        EnsureProjectFile(options.StartupProjectPath, "--startup-project");

        RestoreProject(options.ProjectPath, options);
        if (!string.Equals(options.ProjectPath, options.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            RestoreProject(options.StartupProjectPath, options);
        }

        BuildProject(options.ProjectPath, options);
        if (!string.Equals(options.ProjectPath, options.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            BuildProject(options.StartupProjectPath, options);
        }

        var versions = new[]
            {
                ResolveProjectAssetsFile(options.ProjectPath, options),
                ResolveProjectAssetsFile(options.StartupProjectPath, options)
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(ReadEfCorePackageVersions)
            .ToArray();

        if (versions.Length == 0)
        {
            throw new ExternalProjectLoadException("Unable to detect EF Core version from project.assets.json. Ensure the target project restores Microsoft.EntityFrameworkCore or Microsoft.EntityFrameworkCore.Relational.");
        }

        var majors = versions.Select(version => version.Major).Distinct().OrderBy(major => major).ToArray();
        if (majors.Length > 1)
        {
            throw new ExternalProjectLoadException($"Multiple EF Core major versions were detected ({string.Join(", ", majors.Select(major => $"{major}.x"))}). Use matching EF Core versions across the target and startup projects.");
        }

        var major = majors[0];
        if (major is not 9 and not 10)
        {
            throw new ExternalProjectLoadException($"Unsupported EF Core version {major}.x. D400.DotErd.Tool 0.1.0-beta.2 supports EF Core 9.x and 10.x.");
        }

        return major;
    }

    private static IEnumerable<Version> ReadEfCorePackageVersions(string assetsFile)
    {
        if (!File.Exists(assetsFile))
        {
            throw new ExternalProjectLoadException($"NuGet assets file was not found: {assetsFile}");
        }

        using var stream = File.OpenRead(assetsFile);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
        {
            yield break;
        }

        foreach (var library in libraries.EnumerateObject())
        {
            var slashIndex = library.Name.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex <= 0 || slashIndex == library.Name.Length - 1)
            {
                continue;
            }

            var packageName = library.Name[..slashIndex];
            if (!string.Equals(packageName, "Microsoft.EntityFrameworkCore.Relational", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(packageName, "Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Version.TryParse(library.Name[(slashIndex + 1)..], out var version))
            {
                yield return version;
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

    private static void RestoreProject(string projectPath, ExternalProjectOptions options)
    {
        var result = RunDotnet(options.WorkingDirectory, "restore", projectPath, "--nologo");
        if (result.ExitCode != 0)
        {
            throw new ExternalProjectLoadException($"Failed to restore project '{projectPath}'.");
        }
    }

    private static void BuildProject(string projectPath, ExternalProjectOptions options)
    {
        var result = RunDotnet(options.WorkingDirectory, "build", projectPath, "-c", options.Configuration, "--no-restore", "--nologo");
        if (result.ExitCode != 0)
        {
            throw new ExternalProjectLoadException($"Failed to build project '{projectPath}'.");
        }
    }

    private static string ResolveProjectAssetsFile(string projectPath, ExternalProjectOptions options)
    {
        var result = RunDotnet(
            options.WorkingDirectory,
            "msbuild",
            projectPath,
            "-getProperty:ProjectAssetsFile",
            $"-p:Configuration={options.Configuration}",
            "-nologo");

        if (result.ExitCode == 0)
        {
            var assetsFile = result.StandardOutput
                .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(line => line.EndsWith("project.assets.json", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(assetsFile))
            {
                return Path.GetFullPath(assetsFile);
            }
        }

        return Path.Combine(Path.GetDirectoryName(projectPath) ?? options.WorkingDirectory, "obj", "project.assets.json");
    }

    private static void RunWorker(int efCoreMajor, ExternalProjectOptions options, params string[] arguments)
    {
        var workerPath = ResolveWorkerPath(efCoreMajor);
        var result = RunDotnet(options.WorkingDirectory, [workerPath, .. arguments]);
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"EF Core {efCoreMajor} worker failed."
                : result.StandardError.Trim();
            throw new ExternalProjectLoadException(message);
        }
    }

    private static string ResolveWorkerPath(int efCoreMajor)
    {
        var workerFileName = $"D400.DotErd.Ef{efCoreMajor}.Worker.dll";
        var packagedPath = Path.Combine(AppContext.BaseDirectory, "workers", $"ef{efCoreMajor}", workerFileName);
        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is not null)
        {
            var targetFramework = efCoreMajor == 9 ? "net8.0" : "net10.0";
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var developmentPath = Path.Combine(
                    repositoryRoot,
                    "src",
                    $"D400.DotErd.Ef{efCoreMajor}.Worker",
                    "bin",
                    configuration,
                    targetFramework,
                    workerFileName);
                if (File.Exists(developmentPath))
                {
                    return developmentPath;
                }
            }
        }

        throw new ExternalProjectLoadException($"EF Core {efCoreMajor} worker was not found. Build the solution before running doterd from source.");
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "D400.DotErd.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string CreateTemporaryJsonPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "doterd");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.json");
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

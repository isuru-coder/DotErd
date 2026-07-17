using System.Text.Json;
using D400.DotErd.Application;
using D400.DotErd.Core;
using D400.DotErd.Diff;
using D400.DotErd.Drawio;
using D400.DotErd.EfCore;
using Microsoft.EntityFrameworkCore;

return DotErdCli.Run(args, Console.Out, Console.Error);

public static class DotErdCli
{
    private const string ConfigFileName = ".doterd.json";
    private const string DefaultOutputDirectory = "docs/erd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error, string? workingDirectory = null, DotErdVersionSupport? versionSupport = null)
    {
        workingDirectory ??= Directory.GetCurrentDirectory();
        versionSupport ??= DotErdVersionSupport.Current();

        if (!versionSupport.IsSupported)
        {
            error.WriteLine(versionSupport.UnsupportedMessage);
            return (int)DotErdExitCode.Error;
        }

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return (int)DotErdExitCode.Success;
        }

        var command = args[0].ToLowerInvariant();
        if (!TryParseOptions(args.Skip(1).ToArray(), out var options, out var parseError))
        {
            error.WriteLine(parseError);
            return (int)DotErdExitCode.InvalidArguments;
        }

        if (options.Help)
        {
            WriteCommandHelp(command, output);
            return (int)DotErdExitCode.Success;
        }

        try
        {
            return command switch
            {
                DotErdCommands.Init => Init(options, output, workingDirectory),
                DotErdCommands.ListContexts => ListContexts(options, output, error, workingDirectory),
                DotErdCommands.Inspect => Inspect(options, output, error, workingDirectory),
                DotErdCommands.Generate => Generate(options, output, error, workingDirectory),
                DotErdCommands.Diff => Diff(options, output, error, workingDirectory),
                DotErdCommands.Verify => Verify(options, output, error, workingDirectory),
                _ => InvalidCommand(command, error)
            };
        }
        catch (IOException exception)
        {
            error.WriteLine($"File error: {exception.Message}");
            return (int)DotErdExitCode.Error;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"File access error: {exception.Message}");
            return (int)DotErdExitCode.Error;
        }
        catch (DrawioXmlUpdateException exception)
        {
            error.WriteLine(exception.Message);
            return (int)DotErdExitCode.Error;
        }
        catch (JsonException exception)
        {
            error.WriteLine($"Snapshot JSON error: {exception.Message}");
            return (int)DotErdExitCode.Error;
        }
    }

    private static int Init(CliOptions options, TextWriter output, string workingDirectory)
    {
        var root = ResolvePath(workingDirectory, options.Output ?? ".");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "erd"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "schema"));

        var configPath = Path.Combine(root, ConfigFileName);
        var config = new DotErdConfig(
            Version: 1,
            DefaultContext: options.Context ?? string.Empty,
            Configuration: options.Configuration ?? "Debug",
            OutputDirectory: DefaultOutputDirectory);

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
        output.WriteLine($"Created {configPath}");
        output.WriteLine("Created docs/erd and docs/schema");
        return (int)DotErdExitCode.Success;
    }

    private static int ListContexts(CliOptions options, TextWriter output, TextWriter error, string workingDirectory)
    {
        var projectOptions = ResolveProjectOptions(options, workingDirectory);
        if (projectOptions is null)
        {
            error.WriteLine("list-contexts requires --project <path>.");
            return (int)DotErdExitCode.InvalidArguments;
        }

        IReadOnlyList<ExternalDbContextDescriptor> contexts;
        try
        {
            contexts = ExternalProjectDbContextLoader.Discover(projectOptions);
        }
        catch (ExternalProjectLoadException exception)
        {
            error.WriteLine(exception.Message);
            return (int)DotErdExitCode.Error;
        }

        foreach (var context in contexts.OrderBy(context => context.FullName, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine(context.FullName);
        }

        return (int)DotErdExitCode.Success;
    }

    private static int Inspect(CliOptions options, TextWriter output, TextWriter error, string workingDirectory)
    {
        var result = Extract(options, error, workingDirectory);
        if (result is null)
        {
            return (int)DotErdExitCode.InvalidArguments;
        }

        WriteSchemaSummary(result.Model, output);

        if (!string.IsNullOrWhiteSpace(options.Output))
        {
            var jsonPath = ResolvePath(workingDirectory, options.Output);
            EnsureParentDirectory(jsonPath);
            File.WriteAllText(jsonPath, SerializeSchema(result.Model));
            output.WriteLine($"Wrote schema JSON: {jsonPath}");
        }

        return (int)DotErdExitCode.Success;
    }

    private static int Generate(CliOptions options, TextWriter output, TextWriter error, string workingDirectory)
    {
        var result = Extract(options, error, workingDirectory);
        if (result is null)
        {
            return (int)DotErdExitCode.InvalidArguments;
        }

        var paths = ResolveGeneratePaths(workingDirectory, options.Output, result.Context.DisplayName);
        EnsureParentDirectory(paths.DrawioPath);
        EnsureParentDirectory(paths.SchemaPath);

        var drawioOptions = new DrawioGenerationOptions(result.Context.DisplayName.Replace("DbContext", string.Empty, StringComparison.Ordinal));
        var drawioXml = File.Exists(paths.DrawioPath)
            ? DrawioXmlGenerator.Update(result.Model, File.ReadAllText(paths.DrawioPath), drawioOptions)
            : DrawioXmlGenerator.Generate(result.Model, drawioOptions);

        WriteTextAtomically(paths.DrawioPath, drawioXml);
        WriteTextAtomically(paths.SchemaPath, SerializeSchema(result.Model));

        output.WriteLine($"Wrote draw.io diagram: {paths.DrawioPath}");
        output.WriteLine($"Wrote schema JSON: {paths.SchemaPath}");
        return (int)DotErdExitCode.Success;
    }

    private static int Diff(CliOptions options, TextWriter output, TextWriter error, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.OldSnapshot) || string.IsNullOrWhiteSpace(options.NewSnapshot))
        {
            error.WriteLine("diff requires --old <schema.json> and --new <schema.json>.");
            return (int)DotErdExitCode.InvalidArguments;
        }

        var oldPath = ResolvePath(workingDirectory, options.OldSnapshot);
        var newPath = ResolvePath(workingDirectory, options.NewSnapshot);
        if (!File.Exists(oldPath))
        {
            error.WriteLine($"Baseline snapshot not found: {oldPath}");
            return (int)DotErdExitCode.Error;
        }

        if (!File.Exists(newPath))
        {
            error.WriteLine($"Current snapshot not found: {newPath}");
            return (int)DotErdExitCode.Error;
        }

        var result = SchemaDiff.CompareJson(File.ReadAllText(oldPath), File.ReadAllText(newPath));
        output.WriteLine(result.ToConsoleSummary());

        if (!string.IsNullOrWhiteSpace(options.Output))
        {
            var reportPath = ResolvePath(workingDirectory, options.Output);
            WriteTextAtomically(reportPath, result.ToMarkdown());
            output.WriteLine($"Wrote Markdown report: {reportPath}");
        }

        return (int)DotErdExitCode.Success;
    }

    private static int Verify(CliOptions options, TextWriter output, TextWriter error, string workingDirectory)
    {
        var result = Extract(options, error, workingDirectory);
        if (result is null)
        {
            return (int)DotErdExitCode.InvalidArguments;
        }

        var snapshotPath = ResolveVerifySnapshotPath(workingDirectory, options.Output, result.Context.DisplayName);
        if (!File.Exists(snapshotPath))
        {
            error.WriteLine($"Schema snapshot not found: {snapshotPath}");
            return (int)DotErdExitCode.Error;
        }

        var diff = SchemaDiff.CompareJson(File.ReadAllText(snapshotPath), SerializeSchema(result.Model));
        output.WriteLine(diff.ToConsoleSummary());

        if (!diff.HasChanges)
        {
            return (int)DotErdExitCode.Success;
        }

        output.WriteLine("Run generate to update the committed schema snapshot.");
        return (int)DotErdExitCode.VerificationFailed;
    }

    private static ExtractionResult? Extract(CliOptions options, TextWriter error, string workingDirectory)
    {
        var config = ReadConfig(workingDirectory);
        var contextName = options.Context ?? (string.IsNullOrWhiteSpace(config?.DefaultContext) ? null : config.DefaultContext);
        var projectOptions = ResolveProjectOptions(options, workingDirectory);

        if (projectOptions is null)
        {
            error.WriteLine("A target EF Core project is required. Pass --project <path> and optionally --startup-project <path>.");
            return null;
        }

        ExternalDbContextDescriptor context;
        try
        {
            context = ExternalProjectDbContextLoader.Resolve(projectOptions, contextName);
            using var dbContext = context.Factory();
            var model = EfCoreRelationalModelExtractor.Extract(
                dbContext,
                new EfCoreExtractionOptions(context.DisplayName.Replace("DbContext", string.Empty, StringComparison.Ordinal)));

            if (options.Verbose)
            {
                error.WriteLine($"Extracted context: {context.FullName}");
            }

            return new ExtractionResult(context, model);
        }
        catch (EfCoreRelationalModelExtractionException exception)
        {
            error.WriteLine(exception.Message);
            return null;
        }
        catch (ExternalProjectLoadException exception)
        {
            error.WriteLine(exception.Message);
            return null;
        }
    }

    private static DotErdConfig? ReadConfig(string workingDirectory)
    {
        var configPath = Path.Combine(workingDirectory, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DotErdConfig>(File.ReadAllText(configPath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteSchemaSummary(DatabaseModel model, TextWriter output)
    {
        var tables = model.Schemas.SelectMany(schema => schema.Tables).ToArray();
        var columns = tables.Sum(table => table.Columns.Count);
        var foreignKeys = tables.Sum(table => table.ForeignKeys.Count);
        var indexes = tables.Sum(table => table.Indexes.Count);

        output.WriteLine($"Database: {model.Name}");
        output.WriteLine($"Schemas: {model.Schemas.Count}");
        output.WriteLine($"Tables: {tables.Length}");
        output.WriteLine($"Columns: {columns}");
        output.WriteLine($"Foreign keys: {foreignKeys}");
        output.WriteLine($"Indexes: {indexes}");
        output.WriteLine("Tables:");

        foreach (var table in tables.OrderBy(table => table.SchemaName, StringComparer.OrdinalIgnoreCase).ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase))
        {
            var primaryKey = table.PrimaryKey is null
                ? "no PK"
                : table.PrimaryKey.IsComposite ? "composite PK" : "PK";
            output.WriteLine($"  {table.SchemaName}.{table.Name} ({table.Columns.Count} columns, {primaryKey})");
        }
    }

    private static string SerializeSchema(DatabaseModel model)
    {
        return JsonSerializer.Serialize(model, JsonOptions);
    }

    private static GeneratePaths ResolveGeneratePaths(string workingDirectory, string? outputOption, string contextName)
    {
        var defaultBaseName = contextName.Replace("DbContext", string.Empty, StringComparison.Ordinal);
        var output = string.IsNullOrWhiteSpace(outputOption)
            ? Path.Combine(DefaultOutputDirectory)
            : outputOption;
        var resolved = ResolvePath(workingDirectory, output);
        var extension = Path.GetExtension(resolved);

        if (string.Equals(extension, ".drawio", StringComparison.OrdinalIgnoreCase))
        {
            return new GeneratePaths(
                resolved,
                Path.Combine(Path.GetDirectoryName(resolved) ?? workingDirectory, $"{Path.GetFileNameWithoutExtension(resolved)}.schema.json"));
        }

        return new GeneratePaths(
            Path.Combine(resolved, $"{defaultBaseName}.drawio"),
            Path.Combine(resolved, $"{defaultBaseName}.schema.json"));
    }

    private static string ResolveVerifySnapshotPath(string workingDirectory, string? outputOption, string contextName)
    {
        var defaultBaseName = contextName.Replace("DbContext", string.Empty, StringComparison.Ordinal);
        var output = string.IsNullOrWhiteSpace(outputOption)
            ? DefaultOutputDirectory
            : outputOption;
        var resolved = ResolvePath(workingDirectory, output);

        return string.Equals(Path.GetExtension(resolved), ".json", StringComparison.OrdinalIgnoreCase)
            ? resolved
            : Path.Combine(resolved, $"{defaultBaseName}.schema.json");
    }

    private static string ResolvePath(string workingDirectory, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path));
    }

    private static ExternalProjectOptions? ResolveProjectOptions(CliOptions options, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.Project))
        {
            return null;
        }

        var projectPath = ResolvePath(workingDirectory, options.Project);
        var startupProjectPath = string.IsNullOrWhiteSpace(options.StartupProject)
            ? projectPath
            : ResolvePath(workingDirectory, options.StartupProject);

        return new ExternalProjectOptions(
            projectPath,
            startupProjectPath,
            options.Configuration ?? "Debug",
            workingDirectory);
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void WriteTextAtomically(string path, string content)
    {
        EnsureParentDirectory(path);

        var directory = Path.GetDirectoryName(path);
        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool TryParseOptions(string[] args, out CliOptions options, out string error)
    {
        var parsed = new CliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (IsHelp(option))
            {
                options = parsed with { Help = true };
                error = string.Empty;
                return true;
            }

            if (string.Equals(option, "--verbose", StringComparison.OrdinalIgnoreCase))
            {
                parsed = parsed with { Verbose = true };
                continue;
            }

            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                options = parsed;
                error = $"Unexpected argument '{option}'.";
                return false;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options = parsed;
                error = $"Option '{option}' requires a value.";
                return false;
            }

            var value = args[++index];
            parsed = option.ToLowerInvariant() switch
            {
                "--configuration" => parsed with { Configuration = value },
                "--context" => parsed with { Context = value },
                "--old" => parsed with { OldSnapshot = value },
                "--new" => parsed with { NewSnapshot = value },
                "--output" => parsed with { Output = value },
                "--project" => parsed with { Project = value },
                "--startup-project" => parsed with { StartupProject = value },
                _ => parsed with { UnknownOption = option }
            };

            if (parsed.UnknownOption is not null)
            {
                options = parsed;
                error = $"Unknown option '{parsed.UnknownOption}'.";
                return false;
            }
        }

        options = parsed;
        error = string.Empty;
        return true;
    }

    private static int InvalidCommand(string command, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'. Expected init, list-contexts, inspect, generate, diff, or verify.");
        return (int)DotErdExitCode.InvalidArguments;
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("D400.DotErd");
        output.WriteLine("Usage: doterd <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  init           Create .doterd.json and documentation directories.");
        output.WriteLine("  list-contexts  Print DbContext names from an EF Core project.");
        output.WriteLine("  inspect        Print a schema summary and optionally write JSON.");
        output.WriteLine("  generate       Write .drawio and .schema.json artifacts.");
        output.WriteLine("  diff           Compare two schema snapshots.");
        output.WriteLine("  verify         Check that a schema snapshot is current.");
    }

    private static void WriteCommandHelp(string command, TextWriter output)
    {
        output.WriteLine(command switch
        {
            DotErdCommands.Diff => "Usage: doterd diff --old <schema.json> --new <schema.json> [--output <report.md>] [--verbose]",
            DotErdCommands.ListContexts => "Usage: doterd list-contexts --project <project.csproj> [--startup-project <project.csproj>] [--configuration <name>] [--verbose]",
            DotErdCommands.Verify => "Usage: doterd verify --project <project.csproj> [--startup-project <project.csproj>] --context <name> [--output <schema.json|directory>] [--configuration <name>] [--verbose]",
            _ => $"Usage: doterd {command} --project <project.csproj> [--startup-project <project.csproj>] --context <name> [--output <path>] [--configuration <name>] [--verbose]"
        });
    }

    private sealed record ExtractionResult(ExternalDbContextDescriptor Context, DatabaseModel Model);

    private sealed record GeneratePaths(string DrawioPath, string SchemaPath);

    private sealed record CliOptions(
        string? Configuration = null,
        string? Context = null,
        string? OldSnapshot = null,
        string? NewSnapshot = null,
        string? Output = null,
        string? Project = null,
        string? StartupProject = null,
        bool Verbose = false,
        bool Help = false,
        string? UnknownOption = null);

    private sealed record DotErdConfig(int Version, string DefaultContext, string Configuration, string OutputDirectory);
}

public sealed record DotErdVersionSupport(int DotNetMajor, int EfCoreMajor)
{
    public const int SupportedDotNetMajor = 10;

    public const int SupportedEfCoreMajor = 10;

    public bool IsSupported => DotNetMajor == SupportedDotNetMajor && EfCoreMajor == SupportedEfCoreMajor;

    public string UnsupportedMessage
    {
        get
        {
            if (DotNetMajor != SupportedDotNetMajor)
            {
                return $"Unsupported .NET runtime version {DotNetMajor}.x. D400.DotErd.Tool 0.1.0 is tested only on .NET {SupportedDotNetMajor}.x.";
            }

            return $"Unsupported EF Core version {EfCoreMajor}.x. D400.DotErd.Tool 0.1.0 is tested only with EF Core {SupportedEfCoreMajor}.x SQL Server metadata.";
        }
    }

    public static DotErdVersionSupport Current()
    {
        var dotNetMajor = Environment.Version.Major;
        var efCoreMajor = typeof(DbContext).Assembly.GetName().Version?.Major ?? 0;
        return new DotErdVersionSupport(dotNetMajor, efCoreMajor);
    }
}

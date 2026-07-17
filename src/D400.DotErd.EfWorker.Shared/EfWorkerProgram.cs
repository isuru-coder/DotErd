using System.Text.Json;
using D400.DotErd.Contracts;

namespace D400.DotErd.EfWorker.Shared;

internal static class EfWorkerProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out var options, out var parseError))
        {
            error.WriteLine(parseError);
            return 2;
        }

        try
        {
            var projectOptions = new WorkerProjectOptions(
                options.ProjectPath!,
                options.StartupProjectPath ?? options.ProjectPath!,
                options.Configuration ?? "Debug");

            return options.Command switch
            {
                WorkerCommand.ListContexts => ListContexts(projectOptions, options.OutputPath!, output),
                WorkerCommand.Extract => Extract(projectOptions, options.ContextName, options.DatabaseName, options.OutputPath!, output),
                _ => 2
            };
        }
        catch (WorkerProjectLoadException exception)
        {
            error.WriteLine(exception.Message);
            return 3;
        }
        catch (EfWorkerExtractionException exception)
        {
            error.WriteLine(exception.Message);
            return 4;
        }
        catch (IOException exception)
        {
            error.WriteLine($"File error: {exception.Message}");
            return 5;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"File access error: {exception.Message}");
            return 5;
        }
    }

    private static int ListContexts(WorkerProjectOptions projectOptions, string outputPath, TextWriter output)
    {
        var contexts = WorkerProjectDbContextLoader.Discover(projectOptions)
            .Select(context => new WorkerContextDto(context.DisplayName, context.FullName))
            .OrderBy(context => context.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        EnsureParentDirectory(outputPath);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new WorkerContextListDto(contexts), JsonOptions));
        output.WriteLine($"Wrote contexts: {outputPath}");
        return 0;
    }

    private static int Extract(
        WorkerProjectOptions projectOptions,
        string? contextName,
        string? databaseName,
        string outputPath,
        TextWriter output)
    {
        var context = WorkerProjectDbContextLoader.Resolve(projectOptions, contextName);
        using var dbContext = context.Factory();
        var model = EfWorkerRelationalModelExtractor.Extract(
            dbContext,
            new EfWorkerExtractionOptions(string.IsNullOrWhiteSpace(databaseName) ? context.DisplayName.Replace("DbContext", string.Empty, StringComparison.Ordinal) : databaseName));

        EnsureParentDirectory(outputPath);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(model, JsonOptions));
        output.WriteLine($"Wrote schema JSON: {outputPath}");
        return 0;
    }

    private static bool TryParse(string[] args, out WorkerOptions options, out string error)
    {
        var parsed = new WorkerOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
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
                "--command" => parsed with { Command = ParseCommand(value) },
                "--project" => parsed with { ProjectPath = value },
                "--startup-project" => parsed with { StartupProjectPath = value },
                "--configuration" => parsed with { Configuration = value },
                "--context" => parsed with { ContextName = value },
                "--database-name" => parsed with { DatabaseName = value },
                "--output" => parsed with { OutputPath = value },
                _ => parsed with { UnknownOption = option }
            };

            if (parsed.UnknownOption is not null)
            {
                options = parsed;
                error = $"Unknown option '{parsed.UnknownOption}'.";
                return false;
            }
        }

        if (parsed.Command is WorkerCommand.None)
        {
            options = parsed;
            error = "--command is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.ProjectPath))
        {
            options = parsed;
            error = "--project is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.OutputPath))
        {
            options = parsed;
            error = "--output is required.";
            return false;
        }

        options = parsed;
        error = string.Empty;
        return true;
    }

    private static WorkerCommand ParseCommand(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "list-contexts" => WorkerCommand.ListContexts,
            "extract" => WorkerCommand.Extract,
            _ => WorkerCommand.None
        };
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private sealed record WorkerOptions(
        WorkerCommand Command = WorkerCommand.None,
        string? ProjectPath = null,
        string? StartupProjectPath = null,
        string? Configuration = null,
        string? ContextName = null,
        string? DatabaseName = null,
        string? OutputPath = null,
        string? UnknownOption = null);

    private enum WorkerCommand
    {
        None,
        ListContexts,
        Extract
    }
}

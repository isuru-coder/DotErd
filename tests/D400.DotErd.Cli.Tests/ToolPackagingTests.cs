using System.Diagnostics;
using System.Text;

namespace D400.DotErd.Cli.Tests;

public sealed class ToolPackagingTests
{
    [Fact]
    public void LocalToolPackage_InstallsRunsHelpAndGeneratesSimpleShopArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var workspace = TemporaryWorkspace.Create();
        var packageDirectory = Path.Combine(workspace.Path, "packages");
        Directory.CreateDirectory(packageDirectory);

        var pack = RunDotnet(
            repositoryRoot,
            "pack",
            Path.Combine(repositoryRoot, "src", "D400.DotErd.Cli", "D400.DotErd.Cli.csproj"),
            "--no-build",
            "--no-restore",
            "--disable-build-servers",
            "-c",
            "Debug",
            "-o",
            packageDirectory);
        Assert.Equal(0, pack.ExitCode);
        Assert.True(File.Exists(Path.Combine(packageDirectory, "D400.DotErd.Tool.0.1.0.nupkg")), pack.AllOutput);

        var nugetConfig = Path.Combine(workspace.Path, "NuGet.config");
        File.WriteAllText(nugetConfig, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local-doterd" value="{packageDirectory}" />
              </packageSources>
            </configuration>
            """);

        Assert.Equal(0, RunDotnet(workspace.Path, "new", "tool-manifest").ExitCode);

        var install = RunDotnet(workspace.Path, "tool", "install", "D400.DotErd.Tool", "--configfile", nugetConfig);
        Assert.Equal(0, install.ExitCode);
        Assert.Contains("successfully installed", install.AllOutput, StringComparison.OrdinalIgnoreCase);

        var help = RunDotnet(workspace.Path, "doterd", "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: doterd <command> [options]", help.StandardOutput);

        var outputDirectory = Path.Combine(workspace.Path, "generated");
        var generate = RunDotnet(workspace.Path, "doterd", "generate", "--context", "SimpleShopDbContext", "--output", outputDirectory);
        Assert.Equal(0, generate.ExitCode);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "SimpleShop.drawio")), generate.AllOutput);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "SimpleShop.schema.json")), generate.AllOutput);
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start dotnet.");
        var standardOutputBuilder = new StringBuilder();
        var standardErrorBuilder = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardOutputBuilder.AppendLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardErrorBuilder.AppendLine(eventArgs.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.");
        }
        process.WaitForExit();

        return new ProcessResult(
            process.ExitCode,
            standardOutputBuilder.ToString(),
            standardErrorBuilder.ToString());
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

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Unable to find repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "doterd-tool-packaging-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

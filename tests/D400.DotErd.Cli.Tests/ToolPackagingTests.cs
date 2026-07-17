using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace D400.DotErd.Cli.Tests;

public sealed class ToolPackagingTests
{
    private const string PackageId = "D400.DotErd.Tool";
    private const string PackageVersion = "0.1.0-beta.1";

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
            "Release",
            "-o",
            packageDirectory);
        Assert.Equal(0, pack.ExitCode);
        var packagePath = Path.Combine(packageDirectory, $"{PackageId}.{PackageVersion}.nupkg");
        Assert.True(File.Exists(packagePath), pack.AllOutput);
        AssertPackageContents(packagePath);
        AssertCliProjectDoesNotReferenceSample(repositoryRoot);

        var toolDirectory = Path.Combine(workspace.Path, "tool");
        var install = RunDotnet(workspace.Path, "tool", "install", PackageId, "--version", PackageVersion, "--tool-path", toolDirectory, "--add-source", packageDirectory, "--ignore-failed-sources");
        Assert.Equal(0, install.ExitCode);
        Assert.Contains("successfully installed", install.AllOutput, StringComparison.OrdinalIgnoreCase);

        var doterd = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "doterd.exe" : "doterd");
        var help = Run(doterd, workspace.Path, "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: doterd <command> [options]", help.StandardOutput);

        var sampleProject = Path.Combine(repositoryRoot, "samples", "D400.DotErd.Samples.SimpleShop", "D400.DotErd.Samples.SimpleShop.csproj");
        var sampleStartupProject = Path.Combine(repositoryRoot, "samples", "D400.DotErd.Samples.SimpleShop.Api", "D400.DotErd.Samples.SimpleShop.Api.csproj");
        var drawioPath = Path.Combine(workspace.Path, "generated", "SimpleShop.drawio");
        var generate = Run(
            doterd,
            workspace.Path,
            "generate",
            "--project",
            sampleProject,
            "--startup-project",
            sampleStartupProject,
            "--context",
            "D400.DotErd.Samples.SimpleShop.SimpleShopDbContext",
            "--output",
            drawioPath);
        Assert.Equal(0, generate.ExitCode);
        Assert.True(File.Exists(drawioPath), generate.AllOutput);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(drawioPath)!, "SimpleShop.schema.json")), generate.AllOutput);
    }

    private static void AssertPackageContents(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(entry => entry.FullName).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
        var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var nuspecReader = new StreamReader(nuspecEntry.Open());
        var nuspec = XDocument.Parse(nuspecReader.ReadToEnd());
        XNamespace ns = nuspec.Root?.Name.Namespace ?? XNamespace.None;
        var dependencies = nuspec.Descendants(ns + "dependency").Select(element => element.Attribute("id")?.Value ?? string.Empty).ToArray();

        Assert.Contains(entries, entry => entry.EndsWith("tools/net10.0/any/D400.DotErd.Cli.dll", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("tools/net10.0/any/D400.DotErd.Core.dll", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("tools/net10.0/any/D400.DotErd.EfCore.dll", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("tools/net10.0/any/D400.DotErd.Drawio.dll", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.EndsWith("tools/net10.0/any/D400.DotErd.Diff.dll", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Equals("README.md", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Contains("D400.DotErd.Samples", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Contains(".Tests", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Contains("NuGet.local.config", StringComparison.Ordinal));
        Assert.DoesNotContain(dependencies, dependency => dependency.StartsWith("D400.DotErd.", StringComparison.Ordinal));
        Assert.Equal(PackageVersion, nuspec.Descendants(ns + "version").Single().Value);
        Assert.Equal("D400.DotErd.Tool", nuspec.Descendants(ns + "id").Single().Value);
    }

    private static void AssertCliProjectDoesNotReferenceSample(string repositoryRoot)
    {
        var projectXml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "D400.DotErd.Cli", "D400.DotErd.Cli.csproj"));
        Assert.DoesNotContain("D400.DotErd.Samples", projectXml, StringComparison.Ordinal);
    }

    private static ProcessResult RunDotnet(string workingDirectory, params string[] arguments)
    {
        return Run("dotnet", workingDirectory, arguments);
    }

    private static ProcessResult Run(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
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

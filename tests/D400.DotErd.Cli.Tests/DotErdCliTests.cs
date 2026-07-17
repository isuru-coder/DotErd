using System.Xml.Linq;
using D400.DotErd.Application;

namespace D400.DotErd.Cli.Tests;

public sealed class DotErdCliTests
{
    [Fact]
    public void Init_CreatesConfigAndDocumentationDirectories()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run(["init"], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, ".doterd.json")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(workspace.Path, "docs", "erd")));
        Assert.True(Directory.Exists(System.IO.Path.Combine(workspace.Path, "docs", "schema")));
    }

    [Fact]
    public void ListContexts_PrintsSupportedDbContextNames()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run(["list-contexts", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath()], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.Contains("D400.DotErd.Samples.SimpleShop.SimpleShopDbContext", result.Output);
    }

    [Fact]
    public void Inspect_PrintsSummaryAndCanWriteJson()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputPath = System.IO.Path.Combine(workspace.Path, "schema.json");

        var result = Run(["inspect", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputPath], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.Contains("Database: SimpleShop", result.Output);
        Assert.Contains("shop.Customers", result.Output);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("\"Name\": \"SimpleShop\"", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Generate_WritesDrawioAndSchemaJson()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputDirectory = System.IO.Path.Combine(workspace.Path, "artifacts");

        var result = Run(["generate", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputDirectory], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        var drawioPath = System.IO.Path.Combine(outputDirectory, "SimpleShop.drawio");
        var schemaPath = System.IO.Path.Combine(outputDirectory, "SimpleShop.schema.json");

        Assert.True(File.Exists(drawioPath));
        Assert.True(File.Exists(schemaPath));
        Assert.Equal("mxfile", XDocument.Load(drawioPath).Root?.Name.LocalName);
        Assert.Contains("\"Name\": \"SimpleShop\"", File.ReadAllText(schemaPath));
    }

    [Fact]
    public void Diff_PrintsSummaryAndWritesMarkdownReport()
    {
        using var workspace = TemporaryWorkspace.Create();
        var oldSnapshot = System.IO.Path.Combine(workspace.Path, "old.schema.json");
        var newSnapshot = System.IO.Path.Combine(workspace.Path, "new.schema.json");
        var report = System.IO.Path.Combine(workspace.Path, "diff.md");
        Run(["inspect", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", oldSnapshot], workspace.Path);
        File.WriteAllText(newSnapshot, File.ReadAllText(oldSnapshot).Replace("\"StoreType\": \"nvarchar(200)\"", "\"StoreType\": \"nvarchar(201)\"", StringComparison.Ordinal));

        var result = Run(["diff", "--old", oldSnapshot, "--new", newSnapshot, "--output", report], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.Contains("Schema differences detected", result.Output);
        Assert.True(File.Exists(report));
        Assert.Contains("# Schema Difference Report", File.ReadAllText(report));
    }

    [Fact]
    public void Verify_ReturnsSuccessWhenSnapshotIsCurrent()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputDirectory = System.IO.Path.Combine(workspace.Path, "artifacts");
        Run(["generate", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputDirectory], workspace.Path);

        var result = Run(["verify", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputDirectory], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.Contains("Schema is current", result.Output);
    }

    [Fact]
    public void Verify_ReturnsVerificationFailedWhenSnapshotIsOutdatedAndDoesNotOverwrite()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputDirectory = System.IO.Path.Combine(workspace.Path, "artifacts");
        Run(["generate", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputDirectory], workspace.Path);
        var snapshotPath = System.IO.Path.Combine(outputDirectory, "SimpleShop.schema.json");
        var outdated = File.ReadAllText(snapshotPath).Replace("\"StoreType\": \"nvarchar(200)\"", "\"StoreType\": \"nvarchar(199)\"", StringComparison.Ordinal);
        File.WriteAllText(snapshotPath, outdated);

        var result = Run(["verify", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", outputDirectory], workspace.Path);

        Assert.Equal((int)DotErdExitCode.VerificationFailed, result.ExitCode);
        Assert.Contains("Schema differences detected", result.Output);
        Assert.Equal(outdated, File.ReadAllText(snapshotPath));
    }

    [Fact]
    public void Verify_UsesErrorExitCodeForMissingSnapshot()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run(["verify", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", SampleContextName, "--output", System.IO.Path.Combine(workspace.Path, "missing")], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Error, result.ExitCode);
        Assert.Contains("Schema snapshot not found", result.Error);
    }

    [Fact]
    public void UnknownContext_ReturnsInvalidArgumentsWithoutSecrets()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run(["inspect", "--project", SampleProjectPath(), "--startup-project", SampleStartupProjectPath(), "--context", "Server=secret;Password=very-secret"], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("was not found", result.Error);
        Assert.DoesNotContain("very-secret", result.Output);
        Assert.DoesNotContain("very-secret", result.Error);
    }

    [Fact]
    public void MissingProject_ReturnsInvalidArguments()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run(["generate", "--context", "SimpleShopDbContext"], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("Pass --project", result.Error);
    }

    [Fact]
    public void Generate_CanUseSeparateStartupProjectOption()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputDirectory = System.IO.Path.Combine(workspace.Path, "artifacts");

        var result = Run([
            "generate",
            "--project",
            SampleProjectPath(),
            "--startup-project",
            SampleStartupProjectPath(),
            "--context",
            SampleContextName,
            "--output",
            outputDirectory
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(outputDirectory, "SimpleShop.drawio")));
        Assert.True(File.Exists(System.IO.Path.Combine(outputDirectory, "SimpleShop.schema.json")));
    }

    [Fact]
    public void Generate_CanUseEfCore9Project()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outputDirectory = System.IO.Path.Combine(workspace.Path, "artifacts");

        var result = Run([
            "generate",
            "--project",
            SampleEf9ProjectPath(),
            "--startup-project",
            SampleEf9StartupProjectPath(),
            "--context",
            SampleContextName,
            "--output",
            outputDirectory
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.Success, result.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(outputDirectory, "SimpleShop.drawio")), result.Error);
        Assert.True(File.Exists(System.IO.Path.Combine(outputDirectory, "SimpleShop.schema.json")), result.Error);
    }

    [Fact]
    public void Generate_ReturnsAmbiguousContextErrorForDuplicateShortName()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run([
            "generate",
            "--project",
            SampleProjectPath(),
            "--startup-project",
            SampleStartupProjectPath(),
            "--context",
            "SimpleShopDbContext",
            "--output",
            System.IO.Path.Combine(workspace.Path, "artifacts")
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingProjectFile_ReturnsUsefulError()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run([
            "generate",
            "--project",
            "missing.csproj",
            "--startup-project",
            SampleStartupProjectPath(),
            "--context",
            SampleContextName
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("--project file was not found", result.Error);
    }

    [Fact]
    public void BuildFailure_ReturnsUsefulError()
    {
        using var workspace = TemporaryWorkspace.Create();
        var projectPath = System.IO.Path.Combine(workspace.Path, "BrokenProject.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "Broken.cs"), "public class Broken {");

        var result = Run([
            "generate",
            "--project",
            projectPath,
            "--startup-project",
            projectPath,
            "--context",
            "MissingContext"
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("Failed to build project", result.Error);
    }

    [Fact]
    public void DesignTimeFactoryFailure_ReturnsUsefulErrorWithoutSecrets()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = Run([
            "generate",
            "--project",
            SampleStartupProjectPath(),
            "--startup-project",
            SampleStartupProjectPath(),
            "--context",
            "D400.DotErd.Samples.SimpleShop.Api.BrokenFactoryDbContext",
            "--output",
            System.IO.Path.Combine(workspace.Path, "broken.drawio")
        ], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("Design-time factory", result.Error);
        Assert.DoesNotContain("Server=", result.Error);
        Assert.DoesNotContain("Password=", result.Error);
    }

    [Fact]
    public void UnsupportedEfCoreVersion_ReturnsClearError()
    {
        using var workspace = TemporaryWorkspace.Create();
        var projectPath = System.IO.Path.Combine(workspace.Path, "Ef8Project.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.11" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "Marker.cs"), "public sealed class Marker { }");

        var result = Run(["generate", "--project", projectPath, "--startup-project", projectPath, "--context", "MissingContext"], workspace.Path);

        Assert.Equal((int)DotErdExitCode.InvalidArguments, result.ExitCode);
        Assert.Contains("Unsupported EF Core version 8.x", result.Error);
    }

    private static CliResult Run(string[] args, string workingDirectory)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = DotErdCli.Run(args, output, error, workingDirectory);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private const string SampleContextName = "D400.DotErd.Samples.SimpleShop.SimpleShopDbContext";

    private static string SampleProjectPath()
    {
        return System.IO.Path.Combine(FindRepositoryRoot(), "samples", "D400.DotErd.Samples.SimpleShop", "D400.DotErd.Samples.SimpleShop.csproj");
    }

    private static string SampleStartupProjectPath()
    {
        return System.IO.Path.Combine(FindRepositoryRoot(), "samples", "D400.DotErd.Samples.SimpleShop.Api", "D400.DotErd.Samples.SimpleShop.Api.csproj");
    }

    private static string SampleEf9ProjectPath()
    {
        return System.IO.Path.Combine(FindRepositoryRoot(), "samples", "D400.DotErd.Samples.SimpleShop.Ef9", "D400.DotErd.Samples.SimpleShop.Ef9.csproj");
    }

    private static string SampleEf9StartupProjectPath()
    {
        return System.IO.Path.Combine(FindRepositoryRoot(), "samples", "D400.DotErd.Samples.SimpleShop.Ef9.Api", "D400.DotErd.Samples.SimpleShop.Ef9.Api.csproj");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "D400.DotErd.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root.");
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "doterd-cli-tests", Guid.NewGuid().ToString("N"));
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

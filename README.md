# D400.DotErd

D400.DotErd is a developer-focused .NET tool for generating and updating native editable draw.io ERDs from Entity Framework Core SQL Server relational models.

## Supported Versions

D400.DotErd.Tool `0.1.0-beta.2` is tested with:

- .NET SDK/runtime `10.x`
- EF Core `9.x` and `10.x`
- `Microsoft.EntityFrameworkCore.SqlServer` `9.x` and `10.x`

Other .NET, EF Core, and database provider versions are not claimed as compatible in this beta. EF Core 9 extraction is handled by a bundled `net8.0` worker process, so machines running EF Core 9 extraction also need the .NET 8 runtime available.

## Local Package Workflow

```powershell
dotnet restore D400.DotErd.sln
dotnet build D400.DotErd.sln -c Release
dotnet test D400.DotErd.sln -c Release
dotnet pack src/D400.DotErd.Cli/D400.DotErd.Cli.csproj -c Release
```

The package is written to `artifacts/packages`.

Install the locally built tool:

```powershell
dotnet tool install D400.DotErd.Tool --version 0.1.0-beta.2 --tool-path .tmp/doterd-tool --add-source artifacts/packages
.tmp/doterd-tool/doterd --help
```

## Command Examples

List DbContext types in an EF Core project:

```powershell
doterd list-contexts --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --startup-project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj
```

Inspect a schema and write a JSON snapshot:

```powershell
doterd inspect --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --startup-project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext --output docs/erd/SimpleShop.schema.json
```

Generate editable draw.io and schema JSON artifacts:

```powershell
doterd generate --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --startup-project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext --output docs/erd
```

Compare two schema snapshots:

```powershell
doterd diff --old docs/erd/SimpleShop.schema.json --new artifacts/current-erd/SimpleShop.schema.json --output artifacts/schema-diff.md
```

Verify that a committed schema snapshot is current:

```powershell
doterd verify --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --startup-project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext --output docs/erd
```

## Configuration Example

```json
{
  "Version": 1,
  "DefaultContext": "D400.DotErd.Samples.SimpleShop.SimpleShopDbContext",
  "Configuration": "Release",
  "OutputDirectory": "docs/erd"
}
```

## Security Note

D400.DotErd builds and loads the target project to create the EF Core model. Run it only against projects you trust. Metadata extraction should not require opening a database connection, but project startup code or design-time factories may read configuration, environment variables, or secrets depending on how the target project is written.

## Project Layout

- `src/D400.DotErd.Core`: provider-neutral core types.
- `src/D400.DotErd.Application`: application contracts and command-level abstractions.
- `src/D400.DotErd.Contracts`: version-neutral schema DTOs shared between the CLI and workers.
- `src/D400.DotErd.Ef9.Worker`: EF Core 9 extraction worker.
- `src/D400.DotErd.Ef10.Worker`: EF Core 10 extraction worker.
- `src/D400.DotErd.EfCore`: legacy EF Core 10 extraction boundary covered by unit tests.
- `src/D400.DotErd.Drawio`: draw.io generation boundary.
- `src/D400.DotErd.Diff`: schema snapshot diff boundary.
- `src/D400.DotErd.Cli`: .NET tool entry point.
- `samples/D400.DotErd.Samples.SimpleShop`: EF Core SQL Server sample model.
- `tests`: unit and integration tests.

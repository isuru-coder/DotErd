# D400.DotErd

D400.DotErd is a .NET tool that generates and updates editable draw.io ERDs from Entity Framework Core SQL Server relational models.

The NuGet tool package is `D400.DotErd.Tool`, and the installed command is `doterd`.

## Supported Versions

D400.DotErd.Tool `0.1.0-beta.4` is tested with:

- .NET SDK/runtime `10.x` for the CLI tool.
- .NET runtime `8.x` when extracting EF Core 9 projects.
- EF Core `9.x` and `10.x`.
- `Microsoft.EntityFrameworkCore.SqlServer` `9.x` and `10.x`.

Other EF Core major versions are rejected with an explicit unsupported-version error. Other database providers are not claimed as compatible in this beta.

## How EF Core Loading Works

The CLI does not load EF Core directly. For `inspect`, `generate`, `verify`, and `list-contexts`, it:

1. Resolves `--project` and `--startup-project` relative to the current working directory.
2. Restores and builds the target projects when needed.
3. Reads `obj/project.assets.json` to detect the EF Core major version.
4. Runs a bundled worker process for that EF Core version.
5. Receives normalized schema JSON from the worker.
6. Generates or updates the draw.io and schema artifacts.

Bundled workers:

- `workers/ef9`: `net8.0` worker using EF Core 9 packages.
- `workers/ef10`: `net10.0` worker using EF Core 10 packages.

The tool prefers `IDesignTimeDbContextFactory<TContext>` when available. It reads the EF Core relational model metadata and does not run migrations.

## Installation

Install from NuGet.org after the package is published:

```powershell
dotnet tool install --global D400.DotErd.Tool --version 0.1.0-beta.4
doterd --help
```

Install from a local package build:

```powershell
dotnet tool install D400.DotErd.Tool --version 0.1.0-beta.4 --tool-path .tmp/doterd-tool --add-source artifacts/packages
.tmp/doterd-tool/doterd --help
```

## Commands

Create a starter configuration and output folders:

```powershell
doterd init --output .
```

List DbContext types in an external EF Core project:

```powershell
doterd list-contexts `
  --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj `
  --startup-project samples/D400.DotErd.Samples.SimpleShop.Api/D400.DotErd.Samples.SimpleShop.Api.csproj `
  --configuration Release
```

Inspect a schema and write a JSON snapshot:

```powershell
doterd inspect `
  --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj `
  --startup-project samples/D400.DotErd.Samples.SimpleShop.Api/D400.DotErd.Samples.SimpleShop.Api.csproj `
  --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext `
  --output docs/erd/SimpleShop.schema.json `
  --configuration Release
```

Generate editable draw.io and schema JSON artifacts:

```powershell
doterd generate `
  --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj `
  --startup-project samples/D400.DotErd.Samples.SimpleShop.Api/D400.DotErd.Samples.SimpleShop.Api.csproj `
  --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext `
  --output docs/erd `
  --configuration Release
```

Generate from an EF Core 9 project:

```powershell
doterd generate `
  --project samples/D400.DotErd.Samples.SimpleShop.Ef9/D400.DotErd.Samples.SimpleShop.Ef9.csproj `
  --startup-project samples/D400.DotErd.Samples.SimpleShop.Ef9.Api/D400.DotErd.Samples.SimpleShop.Ef9.Api.csproj `
  --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext `
  --output docs/erd `
  --configuration Release
```

Compare two schema snapshots:

```powershell
doterd diff --old docs/erd/SimpleShop.schema.json --new artifacts/current-erd/SimpleShop.schema.json --output artifacts/schema-diff.md
```

Verify that a committed schema snapshot is current:

```powershell
doterd verify `
  --project samples/D400.DotErd.Samples.SimpleShop/D400.DotErd.Samples.SimpleShop.csproj `
  --startup-project samples/D400.DotErd.Samples.SimpleShop.Api/D400.DotErd.Samples.SimpleShop.Api.csproj `
  --context D400.DotErd.Samples.SimpleShop.SimpleShopDbContext `
  --output docs/erd `
  --configuration Release
```

## Configuration Example

`doterd` reads `.doterd.json` from the current working directory when present.

```json
{
  "Version": 1,
  "DefaultContext": "D400.DotErd.Samples.SimpleShop.SimpleShopDbContext",
  "Configuration": "Release",
  "OutputDirectory": "docs/erd"
}
```

Command-line options override configuration values where supported.

## Security Limitations

D400.DotErd builds and loads the target project to create the EF Core model. Run it only against projects you trust.

The tool is designed to read model metadata only. It must not call `Database.Migrate`, `EnsureCreated`, `EnsureDeleted`, or `database update`. However, your target project's design-time factory, startup code, constructors, or configuration providers may still execute while the DbContext is being created.

D400.DotErd does not intentionally print or serialize connection strings. Avoid writing design-time code that logs secrets during DbContext creation.

## Local Build and Package Validation

```powershell
dotnet restore D400.DotErd.sln
dotnet build D400.DotErd.sln -c Release
dotnet test D400.DotErd.sln -c Release
dotnet pack src/D400.DotErd.Cli/D400.DotErd.Cli.csproj -c Release
```

The package is written to `artifacts/packages`.

## Project Layout

- `src/D400.DotErd.Core`: provider-neutral model types.
- `src/D400.DotErd.Application`: application contracts and command-level abstractions.
- `src/D400.DotErd.Contracts`: EF-version-neutral schema DTOs shared by the CLI and workers.
- `src/D400.DotErd.EfWorker.Shared`: shared worker implementation.
- `src/D400.DotErd.Ef9.Worker`: EF Core 9 extraction worker.
- `src/D400.DotErd.Ef10.Worker`: EF Core 10 extraction worker.
- `src/D400.DotErd.EfCore`: legacy EF Core 10 extraction boundary covered by unit tests.
- `src/D400.DotErd.Drawio`: draw.io generation.
- `src/D400.DotErd.Diff`: schema snapshot diffing.
- `src/D400.DotErd.Cli`: .NET tool entry point.
- `samples/D400.DotErd.Samples.SimpleShop`: EF Core 10 SQL Server sample model.
- `samples/D400.DotErd.Samples.SimpleShop.Api`: EF Core 10 startup sample.
- `samples/D400.DotErd.Samples.SimpleShop.Ef9`: EF Core 9 SQL Server sample model.
- `samples/D400.DotErd.Samples.SimpleShop.Ef9.Api`: EF Core 9 startup sample.
- `tests`: unit and integration tests.

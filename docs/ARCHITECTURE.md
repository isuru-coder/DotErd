# D400.DotErd Architecture

## Architecture Goals

- Extract relational schema from EF Core metadata without connecting to a production database.
- Generate deterministic outputs suitable for source control.
- Keep draw.io files native and editable.
- Preserve existing diagram positions across updates.
- Separate EF Core loading, schema normalization, diffing, layout, and draw.io serialization.

## Component Overview

```text
CLI
  -> Project/Context Resolver
  -> EF Core Model Loader
  -> Relational Model Extractor
  -> Schema Snapshot Service
  -> Diff Engine
  -> Diagram Model Builder
  -> Layout Engine
  -> draw.io Reader/Writer
```

## Component Responsibilities

### CLI

Responsible for parsing commands, validating user input, resolving paths, invoking application services, writing files, and returning automation-friendly exit codes.

It should not contain EF Core extraction, diffing, layout, or XML generation logic.

### Project and Context Resolver

Responsible for finding and selecting `DbContext` classes from a project or compiled assembly.

Responsibilities:

- Resolve project path, startup project path, target framework, configuration, and optional assembly path.
- Support one or multiple context names.
- Report ambiguous or missing contexts clearly.
- Keep context selection deterministic.

### EF Core Model Loader

Responsible for constructing the selected `DbContext` instances and accessing their EF Core metadata.

Responsibilities:

- Use EF Core design-time creation patterns where possible.
- Avoid production database connections.
- Fail fast when a context cannot be created without unsafe runtime dependencies.
- Expose provider information so Phase 1 can enforce SQL Server-only support.

### Relational Model Extractor

Responsible for translating EF Core metadata into a provider-aware internal relational schema model.

Responsibilities:

- Extract schemas and tables.
- Extract columns, store names, SQL data types, nullability, and key participation.
- Extract primary keys and composite key order.
- Extract foreign keys and relationship cardinality where available.
- Represent many-to-many join tables as relational tables.
- Extract basic indexes, including column order and uniqueness.
- Normalize names and ordering for deterministic output.

### Schema Snapshot Service

Responsible for reading and writing JSON snapshots of the internal relational schema model.

Responsibilities:

- Define a versioned JSON shape.
- Preserve deterministic property and collection ordering.
- Validate snapshot compatibility.
- Provide clear errors for unsupported snapshot versions.

### Diff Engine

Responsible for comparing two schema snapshots or extracted schema models.

Responsibilities:

- Report added, removed, and changed tables.
- Report column, key, foreign key, and index changes.
- Produce deterministic ordering.
- Support human-readable reports for review.
- Support machine-readable output for automation if needed.

### Diagram Model Builder

Responsible for converting the relational schema model into an intermediate diagram model before layout and XML serialization.

Responsibilities:

- Convert tables to diagram nodes.
- Convert relationships to diagram edges.
- Mark primary key, foreign key, required, nullable, and indexed column details.
- Assign deterministic logical identifiers independent of layout.

### Layout Engine

Responsible for assigning positions to diagram nodes that do not already have preserved coordinates.

Responsibilities:

- Preserve existing table positions by deterministic table ID.
- Provide a basic deterministic layout for new diagrams.
- Place newly added tables predictably near related tables when possible.
- Avoid unstable layout churn across runs.

### draw.io Reader/Writer

Responsible for reading existing draw.io XML and writing native draw.io XML.

Responsibilities:

- Parse generated diagram elements.
- Preserve table positions.
- Generate editable table shapes and relationship edges.
- Avoid opaque images or non-editable rendered content.
- Use deterministic element IDs.
- Preserve enough metadata to identify D400.DotErd-owned elements on future updates.

### Deterministic ID Service

Responsible for generating stable IDs for schema and diagram elements.

Candidate ID inputs:

- Context identifier.
- Schema name.
- Table name.
- Column name.
- Key or index name.
- Relationship endpoint names and constrained columns.

IDs should be stable across machines and runs, and should not depend on discovery order.

## Public Interfaces

### CLI Commands

The planned public command groups are:

```text
d400 doterd generate
d400 doterd diff
d400 doterd verify
```

### Application Services

The implementation can expose internal service boundaries similar to:

```text
IContextResolver
IModelLoader
IRelationalModelExtractor
ISnapshotReader
ISnapshotWriter
ISchemaDiffer
IDiagramReader
IDiagramWriter
ILayoutEngine
IIdGenerator
```

These names are planning placeholders, not committed implementation names.

### Artifact Formats

Public artifacts:

- `.drawio` native XML diagram.
- `.json` schema snapshot.
- Text or JSON schema difference report.

## Data Flow

### Generate

```text
User input
  -> resolve contexts
  -> load EF Core model
  -> extract relational schema
  -> write snapshot
  -> read existing diagram when present
  -> build diagram model
  -> apply preserved positions and layout
  -> write draw.io XML
```

### Diff

```text
Old snapshot + new snapshot
  -> validate versions
  -> compare schema models
  -> write deterministic report
```

### Verify

```text
User input
  -> regenerate expected artifacts
  -> compare with committed outputs
  -> return success or failure exit code
```

## Key Risks

- EF Core design-time context loading is the highest integration risk.
- SQL Server type extraction must match actual store mappings, not just CLR types.
- Existing draw.io files may be manually edited in ways that make preservation ambiguous.
- Relationship routing and layout may be acceptable technically but visually noisy.
- Composite keys and many-to-many join tables need careful deterministic naming.

## Unresolved Architecture Decisions

- Whether Phase 1 ships as a .NET global tool, local tool, library plus CLI, or all of these.
- Whether context loading uses `Microsoft.EntityFrameworkCore.Design` directly or shells out to compiled design-time infrastructure.
- Whether draw.io metadata is stored in `mxCell` IDs, custom properties, labels, or a combination.
- Whether diagram updates delete missing tables automatically or require an explicit pruning option.
- Whether snapshot JSON is hand-authored with DTOs or generated from a schema contract.


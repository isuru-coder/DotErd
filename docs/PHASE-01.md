# Phase 1 Plan

## Objective

Read the relational model from one or more Entity Framework Core `DbContext` classes and generate or update a native editable draw.io ERD.

## In Scope

- One or multiple `DbContext` classes.
- EF Core code-first projects.
- SQL Server relational mappings.
- Schemas and tables.
- Columns and SQL data types.
- Required and nullable columns.
- Primary keys and composite keys.
- Foreign keys.
- One-to-one and one-to-many relationships.
- Many-to-many join tables.
- Basic indexes.
- Native draw.io XML.
- Deterministic diagram element IDs.
- Basic layout.
- Existing table position preservation.
- JSON schema snapshots.
- Schema difference reports.
- Generate and verify CLI commands.

## Out of Scope

- UML diagrams.
- Other database providers.
- Production database connections.
- Web UI.
- AI relationship inference.
- Stored procedure diagrams.

## Milestone Order

### Milestone 1: Repository and Tool Skeleton

Acceptance criteria:

- Solution structure exists for CLI, core application logic, EF Core integration, draw.io generation, and tests.
- CLI can parse planned commands and options.
- Commands return stable success and failure exit codes.
- No production database connection behavior is introduced.

### Milestone 2: EF Core Context Discovery and Loading

Acceptance criteria:

- Tool can load a sample SQL Server EF Core code-first project.
- Tool can select one explicit `DbContext`.
- Tool can select multiple explicit `DbContext` classes.
- Ambiguous, missing, unsupported, or non-SQL Server contexts produce actionable errors.

### Milestone 3: Relational Model Extraction

Acceptance criteria:

- Extracted model includes schemas, tables, columns, SQL data types, nullability, primary keys, foreign keys, relationship cardinality, many-to-many join tables, and basic indexes.
- Composite keys preserve column order.
- Output ordering is deterministic.
- Extraction works without connecting to a production database.

### Milestone 4: Snapshot JSON

Acceptance criteria:

- Tool writes a versioned JSON snapshot.
- Re-running against the same model produces stable JSON.
- Snapshot can be read back and validated.
- Unsupported snapshot versions produce clear errors.

### Milestone 5: Initial draw.io Generation

Acceptance criteria:

- Tool writes native editable draw.io XML.
- Tables are represented as editable diagram elements.
- Columns show key, nullability, and SQL type details.
- Relationships are represented as editable connectors.
- Element IDs are deterministic.
- Basic layout is deterministic.

### Milestone 6: Existing Diagram Update and Position Preservation

Acceptance criteria:

- Tool reads an existing generated draw.io file.
- Existing table positions are preserved by stable table identity.
- New tables receive deterministic positions.
- Updated columns and relationships are reflected in the diagram.
- Re-running generation without model changes avoids layout churn.

### Milestone 7: Schema Diff Reports

Acceptance criteria:

- Tool compares two snapshots.
- Report includes added, removed, and changed tables.
- Report includes column, key, foreign key, and index changes.
- Report ordering is deterministic.
- Report can be written to a file or standard output.

### Milestone 8: Verify Command

Acceptance criteria:

- Verify command detects stale draw.io and snapshot artifacts.
- Verify command returns a non-zero exit code when artifacts are stale.
- Verify command is suitable for CI.
- Verification output identifies which artifact changed.

### Milestone 9: Documentation and Samples

Acceptance criteria:

- Documentation covers installation assumptions, command usage, supported scope, and limitations.
- Sample EF Core SQL Server project covers single context, multiple contexts, composite keys, one-to-one, one-to-many, many-to-many, and indexes.
- Generated sample artifacts are deterministic.

## Phase 1 Acceptance Criteria

- A developer can generate a draw.io ERD from a SQL Server EF Core code-first project.
- A developer can update an existing generated diagram while preserving table positions.
- A developer can generate and compare JSON schema snapshots.
- CI can run verification and fail on stale artifacts.
- The tool clearly rejects unsupported providers and out-of-scope inputs.
- Outputs are deterministic enough for source control review.

## Key Risks

- Design-time `DbContext` loading may require more configuration than users expect.
- Multi-context diagrams may need namespacing rules to avoid collisions between tables with the same schema and name.
- draw.io XML can become difficult to maintain if generation is too close to raw XML.
- Basic layout may be insufficient for large schemas.
- Snapshot schema changes may become breaking if not versioned from the start.

## Unresolved Decisions

- Final CLI executable and command naming.
- Minimum supported .NET and EF Core versions.
- Whether one output diagram can include multiple contexts by default or whether each context defaults to a separate page.
- Whether removed tables are deleted, hidden, or marked as stale during update.
- Exact visual notation for primary keys, foreign keys, required columns, nullable columns, and indexes.
- Whether verify compares byte-for-byte files or normalized artifact models.


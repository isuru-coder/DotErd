# Testing Strategy

## Testing Goals

- Prove EF Core relational metadata is extracted accurately for SQL Server code-first models.
- Prove generated artifacts are deterministic.
- Prove draw.io diagrams are native, editable, and updateable.
- Prove existing table positions are preserved.
- Prove snapshot diffs and verify command behavior are reliable enough for CI.

## Test Layers

### Unit Tests

Unit tests should cover pure logic with minimal external dependencies.

Targets:

- Deterministic ID generation.
- Relational schema normalization and ordering.
- Snapshot serialization ordering.
- Diff classification and ordering.
- Layout placement rules.
- CLI option validation where parsing can be isolated.

### Integration Tests

Integration tests should use sample EF Core projects and exercise the real extraction path.

Targets:

- Single `DbContext` project.
- Multiple `DbContext` project.
- SQL Server table and schema mappings.
- Column SQL types and nullability.
- Primary keys and composite keys.
- Foreign keys.
- One-to-one relationships.
- One-to-many relationships.
- Many-to-many join tables.
- Basic indexes.

### Golden Artifact Tests

Golden tests should compare generated snapshots, diff reports, and draw.io XML against approved outputs.

Targets:

- Stable JSON snapshots.
- Stable draw.io XML for representative models.
- Stable diff reports for known schema changes.
- No output churn when generation is repeated.

### Update Workflow Tests

Update tests should start from an existing generated draw.io file with known positions.

Targets:

- Existing table positions are preserved.
- New tables are placed deterministically.
- Removed or renamed tables follow the selected Phase 1 behavior.
- Changed columns update without moving the table.
- Relationship updates do not rewrite unrelated diagram elements unnecessarily.

### CLI Tests

CLI tests should exercise command behavior from the user's perspective.

Targets:

- Generate command writes expected diagram and snapshot artifacts.
- Diff command returns expected report and exit code.
- Verify command succeeds when artifacts are current.
- Verify command fails when artifacts are stale.
- Invalid context, unsupported provider, and bad paths produce clear messages.

## Sample Models Required

Phase 1 should include sample models covering:

- Default schema and explicit schema.
- Simple table with scalar columns.
- Required and nullable columns.
- Explicit SQL Server column types.
- Single-column primary key.
- Composite primary key.
- One-to-one relationship.
- One-to-many relationship.
- Many-to-many relationship with join table.
- Foreign key column with custom name.
- Unique and non-unique indexes.
- Multiple `DbContext` classes in one project.

## Determinism Checks

The test suite should verify:

- Repeated generation produces identical snapshots.
- Repeated generation produces semantically identical draw.io output.
- Element IDs are stable regardless of reflection discovery order.
- Diff report ordering does not depend on dictionary or metadata enumeration order.
- Basic layout does not move existing tables.

## Acceptance Test Scenarios

### Generate from a single context

Given a SQL Server EF Core code-first project with one context, when the developer runs generate, then a draw.io diagram and snapshot are created with all supported tables, columns, keys, relationships, and indexes.

### Generate from multiple contexts

Given a project with multiple contexts, when the developer selects two contexts, then the generated artifacts include both context models without ID collisions.

### Preserve positions

Given an existing generated draw.io file where table positions were manually adjusted, when generate runs again, then those table coordinates remain unchanged.

### Report schema changes

Given two snapshots, when diff runs, then added, removed, and changed schema objects are reported deterministically.

### Verify committed artifacts

Given committed generated artifacts, when verify runs after a model change without regenerated outputs, then verify fails and reports the stale artifacts.

## Test Data Boundaries

Tests should not depend on:

- Production database connections.
- External SQL Server instances.
- User secrets.
- Network access.
- Machine-specific absolute paths.

## Key Testing Risks

- EF Core design-time loading can make integration tests brittle across SDK versions.
- Golden draw.io XML tests may be noisy if XML serialization is not normalized.
- Layout tests can over-constrain implementation details.
- Multi-targeting sample projects may increase test runtime.

## Unresolved Testing Decisions

- Whether integration tests compile sample projects at test time or use prebuilt fixtures.
- Whether draw.io assertions compare raw XML, normalized XML, or parsed diagram models.
- Whether CLI tests run the packaged tool or invoke the project directly.
- How many EF Core versions Phase 1 must test.
- Whether verify should support an update mode for approved golden artifacts.


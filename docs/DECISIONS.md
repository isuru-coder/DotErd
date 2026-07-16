# Decisions Log

This document records product and architecture decisions for D400.DotErd. Entries marked `Proposed` should be confirmed before implementation.

## Accepted Decisions

### Phase 1 is EF Core SQL Server code-first only

Status: Accepted

Phase 1 supports EF Core code-first projects using SQL Server relational mappings. Other providers are explicitly out of scope.

Reasoning:

- SQL Server metadata is enough surface area for Phase 1.
- Provider-specific behavior affects data types, schemas, keys, indexes, and relationship mapping.
- Narrow support improves determinism and testability.

### Phase 1 does not connect to production databases

Status: Accepted

The tool reads EF Core metadata and must not require or initiate production database connections.

Reasoning:

- The source of truth is the code-first model.
- Avoiding production connections keeps the tool safe for local and CI use.
- Database reverse engineering is outside Phase 1.

### draw.io output must remain native and editable

Status: Accepted

The ERD must be written as native draw.io XML rather than an embedded image or opaque rendering.

Reasoning:

- Users need to adjust diagrams manually.
- Position preservation requires editable diagram elements.
- Source-controlled XML should reflect meaningful model changes.

### Outputs must be deterministic

Status: Accepted

Diagram IDs, snapshot ordering, diff ordering, and basic layout must be deterministic.

Reasoning:

- The generated artifacts are intended for source control.
- Verification depends on stable regeneration.
- Determinism keeps pull requests reviewable.

## Proposed Decisions

### Ship as a .NET CLI tool

Status: Proposed

Phase 1 should ship primarily as a .NET CLI tool, likely installable as a local or global .NET tool.

Open questions:

- Should the package expose a reusable library API in Phase 1?
- Should command names be under `d400 doterd` or a standalone `doterd` executable?

### Use explicit context selection for multi-context projects

Status: Proposed

When a project contains multiple `DbContext` classes, the user should explicitly list contexts unless a config file selects defaults.

Open questions:

- Should "all contexts" be an explicit option?
- Should one context per diagram page be the default for multi-context generation?

### Store D400.DotErd metadata in draw.io elements

Status: Proposed

Generated draw.io elements should include stable metadata that identifies the schema object they represent.

Open questions:

- Should metadata live in custom XML attributes, draw.io object properties, or encoded labels?
- How should manually duplicated or renamed elements be handled?

### Preserve positions only for recognized generated table elements

Status: Proposed

The update workflow should preserve positions for generated table elements with stable D400.DotErd metadata.

Open questions:

- Should the tool attempt best-effort matching for diagrams that lost metadata?
- Should manual-only shapes be preserved untouched?

### Verify should compare normalized artifacts

Status: Proposed

Verification should prefer normalized artifact comparison over raw byte comparison where possible.

Open questions:

- Is normalized XML comparison practical for draw.io in Phase 1?
- Should a strict byte-for-byte mode also be available?

## Unresolved Decisions

- Minimum supported .NET SDK version.
- Minimum and maximum supported EF Core versions.
- Final command names and option names.
- Snapshot JSON schema shape and versioning policy.
- Diagram page strategy for multiple contexts.
- Layout algorithm for new tables.
- Default behavior for removed tables during diagram update.
- Visual representation of indexes.
- Diff output formats required in Phase 1.
- Whether configuration lives only in CLI options or also in a project config file.

## Decision Review Triggers

Revisit decisions when:

- EF Core model loading cannot support common project shapes.
- draw.io metadata strategy prevents clean manual editing.
- Snapshot versioning blocks backward compatibility.
- Verification produces noisy failures in real CI usage.
- Basic layout is too weak for representative schemas.


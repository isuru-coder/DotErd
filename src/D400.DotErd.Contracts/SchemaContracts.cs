namespace D400.DotErd.Contracts;

public enum SchemaObjectKindDto
{
    Database,
    Schema,
    Table,
    Column,
    PrimaryKey,
    ForeignKey,
    Index,
    Relationship
}

public enum ColumnNullabilityDto
{
    Required,
    Nullable
}

public enum RelationshipCardinalityDto
{
    OneToOne,
    OneToMany,
    ManyToMany
}

public enum RelationshipKindDto
{
    Physical,
    Logical
}

public enum PhysicalRelationshipTypeDto
{
    ForeignKey,
    JoinTable
}

public enum LogicalRelationshipTypeDto
{
    CrossServiceReference,
    ExternalContract
}

public enum IndexSortDirectionDto
{
    Ascending,
    Descending
}

public sealed record LogicalIdentityDto(string Value)
{
    public static LogicalIdentityDto Create(SchemaObjectKindDto kind, params string[] parts)
    {
        if (parts.Length == 0)
        {
            throw new ArgumentException("At least one identity part is required.", nameof(parts));
        }

        var normalizedParts = parts.Select(NormalizePart);
        return new LogicalIdentityDto($"{kind.ToString().ToLowerInvariant()}:{string.Join("/", normalizedParts)}");
    }

    private static string NormalizePart(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            throw new ArgumentException("Identity parts cannot be empty.", nameof(part));
        }

        return part.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("/", "\\/", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}

public sealed record DatabaseModelDto(
    LogicalIdentityDto Identity,
    string Name,
    IReadOnlyList<DatabaseSchemaDto> Schemas,
    IReadOnlyList<RelationshipDefinitionDto> Relationships);

public sealed record DatabaseSchemaDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string Name,
    IReadOnlyList<DatabaseTableDto> Tables);

public sealed record DatabaseTableDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string SchemaName,
    string Name,
    IReadOnlyList<DatabaseColumnDto> Columns,
    PrimaryKeyDefinitionDto? PrimaryKey,
    IReadOnlyList<ForeignKeyDefinitionDto> ForeignKeys,
    IReadOnlyList<IndexDefinitionDto> Indexes);

public sealed record DatabaseColumnDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string SchemaName,
    string TableName,
    string Name,
    SqlDataTypeDto DataType,
    ColumnNullabilityDto Nullability,
    int Ordinal);

public sealed record SqlDataTypeDto(
    string StoreType,
    string? ProviderName = null,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    bool? IsUnicode = null);

public sealed record PrimaryKeyDefinitionDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string SchemaName,
    string TableName,
    string Name,
    IReadOnlyList<LogicalIdentityDto> ColumnIdentities);

public sealed record ForeignKeyColumnMappingDto(
    LogicalIdentityDto DependentColumnIdentity,
    LogicalIdentityDto PrincipalColumnIdentity);

public sealed record ForeignKeyDefinitionDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string SchemaName,
    string TableName,
    string Name,
    LogicalIdentityDto DependentTableIdentity,
    LogicalIdentityDto PrincipalTableIdentity,
    IReadOnlyList<ForeignKeyColumnMappingDto> ColumnMappings,
    RelationshipCardinalityDto Cardinality);

public sealed record IndexColumnDto(
    LogicalIdentityDto ColumnIdentity,
    IndexSortDirectionDto SortDirection,
    int Ordinal);

public sealed record IndexDefinitionDto(
    LogicalIdentityDto Identity,
    string DatabaseName,
    string SchemaName,
    string TableName,
    string Name,
    IReadOnlyList<IndexColumnDto> Columns,
    bool IsUnique);

public sealed record RelationshipDefinitionDto(
    LogicalIdentityDto Identity,
    string Name,
    RelationshipKindDto Kind,
    RelationshipCardinalityDto Cardinality,
    LogicalIdentityDto SourceIdentity,
    LogicalIdentityDto TargetIdentity,
    PhysicalRelationshipTypeDto? PhysicalType = null,
    LogicalRelationshipTypeDto? LogicalType = null);

public sealed record WorkerContextDto(string DisplayName, string FullName);

public sealed record WorkerContextListDto(IReadOnlyList<WorkerContextDto> Contexts);

using D400.DotErd.Contracts;
using D400.DotErd.Core;

internal static class SchemaContractMapper
{
    public static DatabaseModel ToCore(DatabaseModelDto dto)
    {
        return new DatabaseModel(
            dto.Name,
            dto.Schemas.Select(ToCore),
            dto.Relationships.Select(ToCore));
    }

    private static DatabaseSchema ToCore(DatabaseSchemaDto dto)
    {
        return new DatabaseSchema(
            dto.DatabaseName,
            dto.Name,
            dto.Tables.Select(ToCore));
    }

    private static DatabaseTable ToCore(DatabaseTableDto dto)
    {
        return new DatabaseTable(
            dto.DatabaseName,
            dto.SchemaName,
            dto.Name,
            dto.Columns.Select(ToCore),
            dto.PrimaryKey is null ? null : ToCore(dto.PrimaryKey),
            dto.ForeignKeys.Select(ToCore),
            dto.Indexes.Select(ToCore));
    }

    private static DatabaseColumn ToCore(DatabaseColumnDto dto)
    {
        return new DatabaseColumn(
            dto.DatabaseName,
            dto.SchemaName,
            dto.TableName,
            dto.Name,
            ToCore(dto.DataType),
            (ColumnNullability)dto.Nullability,
            dto.Ordinal);
    }

    private static SqlDataType ToCore(SqlDataTypeDto dto)
    {
        return new SqlDataType(
            dto.StoreType,
            dto.ProviderName,
            dto.MaxLength,
            dto.Precision,
            dto.Scale,
            dto.IsUnicode);
    }

    private static PrimaryKeyDefinition ToCore(PrimaryKeyDefinitionDto dto)
    {
        return new PrimaryKeyDefinition(
            dto.DatabaseName,
            dto.SchemaName,
            dto.TableName,
            dto.Name,
            dto.ColumnIdentities.Select(ToCore));
    }

    private static ForeignKeyDefinition ToCore(ForeignKeyDefinitionDto dto)
    {
        return new ForeignKeyDefinition(
            dto.DatabaseName,
            dto.SchemaName,
            dto.TableName,
            dto.Name,
            ToCore(dto.DependentTableIdentity),
            ToCore(dto.PrincipalTableIdentity),
            dto.ColumnMappings.Select(ToCore),
            (RelationshipCardinality)dto.Cardinality);
    }

    private static ForeignKeyColumnMapping ToCore(ForeignKeyColumnMappingDto dto)
    {
        return new ForeignKeyColumnMapping(
            ToCore(dto.DependentColumnIdentity),
            ToCore(dto.PrincipalColumnIdentity));
    }

    private static IndexDefinition ToCore(IndexDefinitionDto dto)
    {
        return new IndexDefinition(
            dto.DatabaseName,
            dto.SchemaName,
            dto.TableName,
            dto.Name,
            dto.Columns.Select(ToCore),
            dto.IsUnique);
    }

    private static IndexColumn ToCore(IndexColumnDto dto)
    {
        return new IndexColumn(
            ToCore(dto.ColumnIdentity),
            (IndexSortDirection)dto.SortDirection,
            dto.Ordinal);
    }

    private static RelationshipDefinition ToCore(RelationshipDefinitionDto dto)
    {
        return new RelationshipDefinition(
            dto.Name,
            (RelationshipKind)dto.Kind,
            (RelationshipCardinality)dto.Cardinality,
            ToCore(dto.SourceIdentity),
            ToCore(dto.TargetIdentity),
            dto.PhysicalType is null ? null : (PhysicalRelationshipType)dto.PhysicalType,
            dto.LogicalType is null ? null : (LogicalRelationshipType)dto.LogicalType);
    }

    private static LogicalIdentity ToCore(LogicalIdentityDto dto)
    {
        return new LogicalIdentity(dto.Value);
    }
}

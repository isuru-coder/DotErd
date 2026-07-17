using D400.DotErd.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace D400.DotErd.EfWorker.Shared;

internal sealed record EfWorkerExtractionOptions(string? DatabaseName = null, bool ExcludeMigrationsHistory = true);

internal sealed class EfWorkerExtractionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class EfWorkerRelationalModelExtractor
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public static DatabaseModelDto Extract(DbContext context, EfWorkerExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= new EfWorkerExtractionOptions();

        try
        {
            EnsureSqlServerProvider(context);

            var databaseName = ResolveDatabaseName(context, options);
            var relationalModel = context.Model.GetRelationalModel();
            var tables = relationalModel.Tables
                .Where(table => ShouldIncludeTable(table, options))
                .OrderBy(table => table.Schema ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var extractedTables = tables.ToDictionary(
                table => TableIdentity(databaseName, table),
                table => ExtractTable(databaseName, table));

            var schemas = extractedTables.Values
                .GroupBy(table => table.SchemaName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DatabaseSchemaDto(
                    LogicalIdentityDto.Create(SchemaObjectKindDto.Schema, databaseName, group.Key),
                    databaseName,
                    group.Key,
                    group.OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase).ToArray()))
                .ToArray();

            var relationships = ExtractRelationships(databaseName, tables)
                .OrderBy(relationship => relationship.Identity.Value, StringComparer.Ordinal)
                .ToArray();

            return new DatabaseModelDto(
                LogicalIdentityDto.Create(SchemaObjectKindDto.Database, databaseName),
                databaseName,
                schemas,
                relationships);
        }
        catch (EfWorkerExtractionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EfWorkerExtractionException(
                $"Unable to extract the EF Core relational model from DbContext '{context.GetType().FullName}'. Ensure the model can be finalized without a database connection and that SQL Server relational mappings are configured.",
                exception);
        }
    }

    private static void EnsureSqlServerProvider(DbContext context)
    {
        if (!string.Equals(context.Database.ProviderName, SqlServerProviderName, StringComparison.Ordinal))
        {
            throw new EfWorkerExtractionException(
                $"Unsupported EF Core provider '{context.Database.ProviderName ?? "<unknown>"}'. This release supports only SQL Server relational mappings.");
        }
    }

    private static string ResolveDatabaseName(DbContext context, EfWorkerExtractionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            return options.DatabaseName;
        }

        var connectionDatabaseName = context.Database.GetDbConnection().Database;
        return string.IsNullOrWhiteSpace(connectionDatabaseName)
            ? context.GetType().Name
            : connectionDatabaseName;
    }

    private static bool ShouldIncludeTable(ITable table, EfWorkerExtractionOptions options)
    {
        return !options.ExcludeMigrationsHistory
            || !string.Equals(table.Name, MigrationsHistoryTableName, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseTableDto ExtractTable(string databaseName, ITable table)
    {
        var schemaName = SchemaName(table);
        var columns = table.Columns
            .Select((column, ordinal) => ExtractColumn(databaseName, table, column, ordinal))
            .OrderBy(column => column.Ordinal)
            .ToArray();

        var primaryKey = table.PrimaryKey is null
            ? null
            : ExtractPrimaryKey(databaseName, table, table.PrimaryKey);

        var foreignKeys = table.ForeignKeyConstraints
            .OrderBy(foreignKey => foreignKey.Name, StringComparer.OrdinalIgnoreCase)
            .Select(foreignKey => ExtractForeignKey(databaseName, foreignKey))
            .ToArray();

        var indexes = table.Indexes
            .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
            .Select(index => ExtractIndex(databaseName, index))
            .ToArray();

        return new DatabaseTableDto(
            TableIdentity(databaseName, table),
            databaseName,
            schemaName,
            table.Name,
            columns,
            primaryKey,
            foreignKeys,
            indexes);
    }

    private static DatabaseColumnDto ExtractColumn(string databaseName, ITable table, IColumn column, int ordinal)
    {
        var schemaName = SchemaName(table);
        return new DatabaseColumnDto(
            LogicalIdentityDto.Create(SchemaObjectKindDto.Column, databaseName, schemaName, table.Name, column.Name),
            databaseName,
            schemaName,
            table.Name,
            column.Name,
            ExtractDataType(column),
            column.IsNullable ? ColumnNullabilityDto.Nullable : ColumnNullabilityDto.Required,
            ordinal);
    }

    private static SqlDataTypeDto ExtractDataType(IColumn column)
    {
        var mapping = column.StoreTypeMapping;
        return new SqlDataTypeDto(
            column.StoreType,
            ProviderName: SqlServerProviderName,
            MaxLength: mapping.Size,
            Precision: mapping.Precision,
            Scale: mapping.Scale,
            IsUnicode: mapping.IsUnicode);
    }

    private static PrimaryKeyDefinitionDto ExtractPrimaryKey(string databaseName, ITable table, IUniqueConstraint primaryKey)
    {
        var schemaName = SchemaName(table);
        return new PrimaryKeyDefinitionDto(
            LogicalIdentityDto.Create(SchemaObjectKindDto.PrimaryKey, databaseName, schemaName, table.Name, primaryKey.Name),
            databaseName,
            schemaName,
            table.Name,
            primaryKey.Name,
            primaryKey.Columns.Select(column => ColumnIdentity(databaseName, table, column)).ToArray());
    }

    private static ForeignKeyDefinitionDto ExtractForeignKey(string databaseName, IForeignKeyConstraint foreignKey)
    {
        var schemaName = SchemaName(foreignKey.Table);
        return new ForeignKeyDefinitionDto(
            LogicalIdentityDto.Create(SchemaObjectKindDto.ForeignKey, databaseName, schemaName, foreignKey.Table.Name, foreignKey.Name),
            databaseName,
            schemaName,
            foreignKey.Table.Name,
            foreignKey.Name,
            TableIdentity(databaseName, foreignKey.Table),
            TableIdentity(databaseName, foreignKey.PrincipalTable),
            foreignKey.Columns.Zip(foreignKey.PrincipalColumns)
                .Select(mapping => new ForeignKeyColumnMappingDto(
                    ColumnIdentity(databaseName, foreignKey.Table, mapping.First),
                    ColumnIdentity(databaseName, foreignKey.PrincipalTable, mapping.Second)))
                .ToArray(),
            GetForeignKeyCardinality(foreignKey));
    }

    private static IndexDefinitionDto ExtractIndex(string databaseName, ITableIndex index)
    {
        var schemaName = SchemaName(index.Table);
        return new IndexDefinitionDto(
            LogicalIdentityDto.Create(SchemaObjectKindDto.Index, databaseName, schemaName, index.Table.Name, index.Name),
            databaseName,
            schemaName,
            index.Table.Name,
            index.Name,
            index.Columns.Select((column, ordinal) => new IndexColumnDto(
                ColumnIdentity(databaseName, index.Table, column),
                IndexSortDirectionDto.Ascending,
                ordinal)).ToArray(),
            index.IsUnique);
    }

    private static RelationshipCardinalityDto GetForeignKeyCardinality(IForeignKeyConstraint foreignKey)
    {
        return foreignKey.MappedForeignKeys.Any(mappedForeignKey => mappedForeignKey.IsUnique)
            ? RelationshipCardinalityDto.OneToOne
            : RelationshipCardinalityDto.OneToMany;
    }

    private static IEnumerable<RelationshipDefinitionDto> ExtractRelationships(string databaseName, IReadOnlyCollection<ITable> tables)
    {
        foreach (var table in tables)
        {
            foreach (var foreignKey in table.ForeignKeyConstraints.OrderBy(foreignKey => foreignKey.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return new RelationshipDefinitionDto(
                    LogicalIdentityDto.Create(SchemaObjectKindDto.Relationship, databaseName, foreignKey.Name),
                    foreignKey.Name,
                    RelationshipKindDto.Physical,
                    GetForeignKeyCardinality(foreignKey),
                    TableIdentity(databaseName, foreignKey.PrincipalTable),
                    TableIdentity(databaseName, foreignKey.Table),
                    PhysicalType: PhysicalRelationshipTypeDto.ForeignKey);
            }

            var manyToManyRelationship = TryCreateManyToManyRelationship(databaseName, table);
            if (manyToManyRelationship is not null)
            {
                yield return manyToManyRelationship;
            }
        }
    }

    private static RelationshipDefinitionDto? TryCreateManyToManyRelationship(string databaseName, ITable table)
    {
        var primaryKey = table.PrimaryKey;
        if (table.ForeignKeyConstraints.Count() != 2 || primaryKey is null)
        {
            return null;
        }

        var foreignKeyColumns = table.ForeignKeyConstraints
            .SelectMany(foreignKey => foreignKey.Columns)
            .Select(column => column.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primaryKeyColumns = primaryKey.Columns
            .Select(column => column.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (table.Columns.Count() != primaryKeyColumns.Length
            || !foreignKeyColumns.SequenceEqual(primaryKeyColumns, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var foreignKeys = table.ForeignKeyConstraints
            .OrderBy(foreignKey => SchemaName(foreignKey.PrincipalTable), StringComparer.OrdinalIgnoreCase)
            .ThenBy(foreignKey => foreignKey.PrincipalTable.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RelationshipDefinitionDto(
            LogicalIdentityDto.Create(SchemaObjectKindDto.Relationship, databaseName, table.Name),
            table.Name,
            RelationshipKindDto.Physical,
            RelationshipCardinalityDto.ManyToMany,
            TableIdentity(databaseName, foreignKeys[0].PrincipalTable),
            TableIdentity(databaseName, foreignKeys[1].PrincipalTable),
            PhysicalType: PhysicalRelationshipTypeDto.JoinTable);
    }

    private static LogicalIdentityDto TableIdentity(string databaseName, ITable table)
    {
        return LogicalIdentityDto.Create(SchemaObjectKindDto.Table, databaseName, SchemaName(table), table.Name);
    }

    private static LogicalIdentityDto ColumnIdentity(string databaseName, ITable table, IColumn column)
    {
        return LogicalIdentityDto.Create(SchemaObjectKindDto.Column, databaseName, SchemaName(table), table.Name, column.Name);
    }

    private static string SchemaName(ITableBase table)
    {
        return string.IsNullOrWhiteSpace(table.Schema) ? "dbo" : table.Schema;
    }
}

using D400.DotErd.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace D400.DotErd.EfCore;

/// <summary>
/// Extracts the finalized SQL Server EF Core relational model into the provider-neutral Core schema model.
/// </summary>
public static class EfCoreRelationalModelExtractor
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    /// <summary>
    /// Creates a context with the supplied factory and extracts its finalized relational model.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="options">The extraction options.</param>
    /// <returns>The extracted normalized database model.</returns>
    /// <exception cref="EfCoreRelationalModelExtractionException">Thrown when the context cannot be created or extracted.</exception>
    public static DatabaseModel Extract(Func<DbContext> contextFactory, EfCoreExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        try
        {
            using var context = contextFactory();
            return Extract(context, options);
        }
        catch (EfCoreRelationalModelExtractionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EfCoreRelationalModelExtractionException(
                "Unable to create the DbContext for schema extraction. Ensure the context can be constructed at design time without opening a database connection, and pass explicit options or a design-time factory when required.",
                exception);
        }
    }

    /// <summary>
    /// Extracts the finalized relational model from an existing context instance.
    /// </summary>
    /// <param name="context">The context instance.</param>
    /// <param name="options">The extraction options.</param>
    /// <returns>The extracted normalized database model.</returns>
    /// <exception cref="EfCoreRelationalModelExtractionException">Thrown when the provider is unsupported or metadata cannot be extracted.</exception>
    public static DatabaseModel Extract(DbContext context, EfCoreExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= new EfCoreExtractionOptions();

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
                .Select(group => new DatabaseSchema(databaseName, group.Key, group.OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            var relationships = ExtractRelationships(databaseName, tables)
                .OrderBy(relationship => relationship.Identity.Value, StringComparer.Ordinal)
                .ToArray();

            return new DatabaseModel(databaseName, schemas, relationships);
        }
        catch (EfCoreRelationalModelExtractionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EfCoreRelationalModelExtractionException(
                $"Unable to extract the EF Core relational model from DbContext '{context.GetType().FullName}'. Ensure the model can be finalized without a database connection and that SQL Server relational mappings are configured.",
                exception);
        }
    }

    private static void EnsureSqlServerProvider(DbContext context)
    {
        if (!string.Equals(context.Database.ProviderName, SqlServerProviderName, StringComparison.Ordinal))
        {
            throw new EfCoreRelationalModelExtractionException(
                $"Unsupported EF Core provider '{context.Database.ProviderName ?? "<unknown>"}'. Phase 1 supports only SQL Server relational mappings.");
        }
    }

    private static string ResolveDatabaseName(DbContext context, EfCoreExtractionOptions options)
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

    private static bool ShouldIncludeTable(ITable table, EfCoreExtractionOptions options)
    {
        return !options.ExcludeMigrationsHistory
            || !string.Equals(table.Name, MigrationsHistoryTableName, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseTable ExtractTable(string databaseName, ITable table)
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

        return new DatabaseTable(databaseName, schemaName, table.Name, columns, primaryKey, foreignKeys, indexes);
    }

    private static DatabaseColumn ExtractColumn(string databaseName, ITable table, IColumn column, int ordinal)
    {
        return new DatabaseColumn(
            databaseName,
            SchemaName(table),
            table.Name,
            column.Name,
            ExtractDataType(column),
            column.IsNullable ? ColumnNullability.Nullable : ColumnNullability.Required,
            ordinal);
    }

    private static SqlDataType ExtractDataType(IColumn column)
    {
        var mapping = column.StoreTypeMapping;
        return new SqlDataType(
            column.StoreType,
            ProviderName: SqlServerProviderName,
            MaxLength: mapping.Size,
            Precision: mapping.Precision,
            Scale: mapping.Scale,
            IsUnicode: mapping.IsUnicode);
    }

    private static PrimaryKeyDefinition ExtractPrimaryKey(string databaseName, ITable table, IUniqueConstraint primaryKey)
    {
        return new PrimaryKeyDefinition(
            databaseName,
            SchemaName(table),
            table.Name,
            primaryKey.Name,
            primaryKey.Columns.Select(column => ColumnIdentity(databaseName, table, column)));
    }

    private static ForeignKeyDefinition ExtractForeignKey(string databaseName, IForeignKeyConstraint foreignKey)
    {
        return new ForeignKeyDefinition(
            databaseName,
            SchemaName(foreignKey.Table),
            foreignKey.Table.Name,
            foreignKey.Name,
            TableIdentity(databaseName, foreignKey.Table),
            TableIdentity(databaseName, foreignKey.PrincipalTable),
            foreignKey.Columns.Zip(foreignKey.PrincipalColumns)
                .Select(mapping => new ForeignKeyColumnMapping(
                    ColumnIdentity(databaseName, foreignKey.Table, mapping.First),
                    ColumnIdentity(databaseName, foreignKey.PrincipalTable, mapping.Second))),
            GetForeignKeyCardinality(foreignKey));
    }

    private static IndexDefinition ExtractIndex(string databaseName, ITableIndex index)
    {
        return new IndexDefinition(
            databaseName,
            SchemaName(index.Table),
            index.Table.Name,
            index.Name,
            index.Columns.Select((column, ordinal) => new IndexColumn(
                ColumnIdentity(databaseName, index.Table, column),
                IndexSortDirection.Ascending,
                ordinal)),
            index.IsUnique);
    }

    private static RelationshipCardinality GetForeignKeyCardinality(IForeignKeyConstraint foreignKey)
    {
        return foreignKey.MappedForeignKeys.Any(mappedForeignKey => mappedForeignKey.IsUnique)
            ? RelationshipCardinality.OneToOne
            : RelationshipCardinality.OneToMany;
    }

    private static IEnumerable<RelationshipDefinition> ExtractRelationships(string databaseName, IReadOnlyCollection<ITable> tables)
    {
        foreach (var table in tables)
        {
            foreach (var foreignKey in table.ForeignKeyConstraints.OrderBy(foreignKey => foreignKey.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return new RelationshipDefinition(
                    foreignKey.Name,
                    RelationshipKind.Physical,
                    GetForeignKeyCardinality(foreignKey),
                    TableIdentity(databaseName, foreignKey.PrincipalTable),
                    TableIdentity(databaseName, foreignKey.Table),
                    physicalType: PhysicalRelationshipType.ForeignKey);
            }

            var manyToManyRelationship = TryCreateManyToManyRelationship(databaseName, table);
            if (manyToManyRelationship is not null)
            {
                yield return manyToManyRelationship;
            }
        }
    }

    private static RelationshipDefinition? TryCreateManyToManyRelationship(string databaseName, ITable table)
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

        return new RelationshipDefinition(
            table.Name,
            RelationshipKind.Physical,
            RelationshipCardinality.ManyToMany,
            TableIdentity(databaseName, foreignKeys[0].PrincipalTable),
            TableIdentity(databaseName, foreignKeys[1].PrincipalTable),
            physicalType: PhysicalRelationshipType.JoinTable);
    }

    private static LogicalIdentity TableIdentity(string databaseName, ITable table)
    {
        return LogicalIdentity.Create(SchemaObjectKind.Table, databaseName, SchemaName(table), table.Name);
    }

    private static LogicalIdentity ColumnIdentity(string databaseName, ITable table, IColumn column)
    {
        return LogicalIdentity.Create(SchemaObjectKind.Column, databaseName, SchemaName(table), table.Name, column.Name);
    }

    private static string SchemaName(ITableBase table)
    {
        return string.IsNullOrWhiteSpace(table.Schema) ? "dbo" : table.Schema;
    }
}

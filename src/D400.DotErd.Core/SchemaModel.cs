using System.Collections;

namespace D400.DotErd.Core;

/// <summary>
/// Identifies the kind of schema object represented by a logical identity.
/// </summary>
public enum SchemaObjectKind
{
    /// <summary>
    /// A database model.
    /// </summary>
    Database,

    /// <summary>
    /// A database schema.
    /// </summary>
    Schema,

    /// <summary>
    /// A database table.
    /// </summary>
    Table,

    /// <summary>
    /// A database column.
    /// </summary>
    Column,

    /// <summary>
    /// A primary key constraint.
    /// </summary>
    PrimaryKey,

    /// <summary>
    /// A foreign key constraint.
    /// </summary>
    ForeignKey,

    /// <summary>
    /// A database index.
    /// </summary>
    Index,

    /// <summary>
    /// A relationship between schema objects.
    /// </summary>
    Relationship
}

/// <summary>
/// Describes whether a column accepts database null values.
/// </summary>
public enum ColumnNullability
{
    /// <summary>
    /// The column is required and does not accept null values.
    /// </summary>
    Required,

    /// <summary>
    /// The column is optional and accepts null values.
    /// </summary>
    Nullable
}

/// <summary>
/// Describes the cardinality of a relationship between tables or logical resources.
/// </summary>
public enum RelationshipCardinality
{
    /// <summary>
    /// One source row or resource relates to at most one target row or resource.
    /// </summary>
    OneToOne,

    /// <summary>
    /// One source row or resource relates to many target rows or resources.
    /// </summary>
    OneToMany,

    /// <summary>
    /// Many source rows or resources relate to many target rows or resources.
    /// </summary>
    ManyToMany
}

/// <summary>
/// Describes whether a relationship is backed by a physical database construct or a logical external link.
/// </summary>
public enum RelationshipKind
{
    /// <summary>
    /// The relationship is represented by physical database metadata such as a foreign key or join table.
    /// </summary>
    Physical,

    /// <summary>
    /// The relationship represents a logical link that may cross service or database boundaries.
    /// </summary>
    Logical
}

/// <summary>
/// Describes the physical database construct used by a relationship.
/// </summary>
public enum PhysicalRelationshipType
{
    /// <summary>
    /// The relationship is represented by a foreign key.
    /// </summary>
    ForeignKey,

    /// <summary>
    /// The relationship is represented by a join table.
    /// </summary>
    JoinTable
}

/// <summary>
/// Describes a logical relationship type reserved for future cross-service links.
/// </summary>
public enum LogicalRelationshipType
{
    /// <summary>
    /// The relationship points at an object owned by another service or bounded context.
    /// </summary>
    CrossServiceReference,

    /// <summary>
    /// The relationship is described by an external contract rather than a database constraint.
    /// </summary>
    ExternalContract
}

/// <summary>
/// Describes the sort direction of an indexed column.
/// </summary>
public enum IndexSortDirection
{
    /// <summary>
    /// Values are sorted in ascending order.
    /// </summary>
    Ascending,

    /// <summary>
    /// Values are sorted in descending order.
    /// </summary>
    Descending
}

/// <summary>
/// Represents a deterministic identity for a normalized schema object.
/// </summary>
public readonly record struct LogicalIdentity
{
    /// <summary>
    /// Initializes a new logical identity.
    /// </summary>
    /// <param name="value">The canonical identity value.</param>
    /// <exception cref="ArgumentException">Thrown when the identity value is empty.</exception>
    public LogicalIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Logical identity values cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the canonical identity value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a deterministic logical identity from a schema object kind and canonical name parts.
    /// </summary>
    /// <param name="kind">The schema object kind.</param>
    /// <param name="parts">The stable name parts that identify the object.</param>
    /// <returns>A deterministic logical identity.</returns>
    public static LogicalIdentity Create(SchemaObjectKind kind, params string[] parts)
    {
        if (parts.Length == 0)
        {
            throw new ArgumentException("At least one identity part is required.", nameof(parts));
        }

        var normalizedParts = parts.Select(NormalizePart);
        return new LogicalIdentity($"{kind.ToString().ToLowerInvariant()}:{string.Join("/", normalizedParts)}");
    }

    /// <summary>
    /// Returns the canonical identity value.
    /// </summary>
    /// <returns>The canonical identity value.</returns>
    public override string ToString()
    {
        return Value;
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

/// <summary>
/// Represents an immutable read-only list with structural equality.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    /// <summary>
    /// Initializes a new immutable value list.
    /// </summary>
    /// <param name="items">The items to copy into the list.</param>
    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    /// <summary>
    /// Gets an empty immutable value list.
    /// </summary>
    public static ValueList<T> Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)_items).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Determines whether this list has the same values in the same order as another list.
    /// </summary>
    /// <param name="other">The other list.</param>
    /// <returns><see langword="true" /> when the lists are equal; otherwise, <see langword="false" />.</returns>
    public bool Equals(ValueList<T>? other)
    {
        return other is not null && _items.SequenceEqual(other._items);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ValueList<T> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        foreach (var item in _items)
        {
            hashCode.Add(item);
        }

        return hashCode.ToHashCode();
    }
}

/// <summary>
/// Defines the common identity surface for normalized schema objects.
/// </summary>
public interface ISchemaObject
{
    /// <summary>
    /// Gets the deterministic logical identity of the schema object.
    /// </summary>
    LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the object name as it appears in the normalized model.
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Represents a normalized database model.
/// </summary>
public sealed record DatabaseModel : ISchemaObject
{
    /// <summary>
    /// Initializes a new database model.
    /// </summary>
    /// <param name="name">The database name.</param>
    /// <param name="schemas">The database schemas.</param>
    /// <param name="relationships">The normalized relationships.</param>
    public DatabaseModel(string name, IEnumerable<DatabaseSchema> schemas, IEnumerable<RelationshipDefinition>? relationships = null)
    {
        Name = Guard.Required(name);
        Identity = LogicalIdentity.Create(SchemaObjectKind.Database, Name);
        Schemas = new ValueList<DatabaseSchema>(schemas);
        Relationships = relationships is null ? ValueList<RelationshipDefinition>.Empty : new ValueList<RelationshipDefinition>(relationships);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the schemas in the database.
    /// </summary>
    public ValueList<DatabaseSchema> Schemas { get; }

    /// <summary>
    /// Gets the relationships in the database model.
    /// </summary>
    public ValueList<RelationshipDefinition> Relationships { get; }
}

/// <summary>
/// Represents a normalized database schema.
/// </summary>
public sealed record DatabaseSchema : ISchemaObject
{
    /// <summary>
    /// Initializes a new database schema.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="name">The schema name.</param>
    /// <param name="tables">The tables contained by the schema.</param>
    public DatabaseSchema(string databaseName, string name, IEnumerable<DatabaseTable> tables)
    {
        DatabaseName = Guard.Required(databaseName);
        Name = Guard.Required(name);
        Identity = LogicalIdentity.Create(SchemaObjectKind.Schema, DatabaseName, Name);
        Tables = new ValueList<DatabaseTable>(tables);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the tables contained by the schema.
    /// </summary>
    public ValueList<DatabaseTable> Tables { get; }
}

/// <summary>
/// Represents a normalized database table.
/// </summary>
public sealed record DatabaseTable : ISchemaObject
{
    /// <summary>
    /// Initializes a new database table.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="schemaName">The owning schema name.</param>
    /// <param name="name">The table name.</param>
    /// <param name="columns">The table columns.</param>
    /// <param name="primaryKey">The table primary key, when present.</param>
    /// <param name="foreignKeys">The table foreign keys.</param>
    /// <param name="indexes">The table indexes.</param>
    public DatabaseTable(
        string databaseName,
        string schemaName,
        string name,
        IEnumerable<DatabaseColumn> columns,
        PrimaryKeyDefinition? primaryKey = null,
        IEnumerable<ForeignKeyDefinition>? foreignKeys = null,
        IEnumerable<IndexDefinition>? indexes = null)
    {
        DatabaseName = Guard.Required(databaseName);
        SchemaName = Guard.Required(schemaName);
        Name = Guard.Required(name);
        Identity = LogicalIdentity.Create(SchemaObjectKind.Table, DatabaseName, SchemaName, Name);
        Columns = new ValueList<DatabaseColumn>(columns);
        PrimaryKey = primaryKey;
        ForeignKeys = foreignKeys is null ? ValueList<ForeignKeyDefinition>.Empty : new ValueList<ForeignKeyDefinition>(foreignKeys);
        Indexes = indexes is null ? ValueList<IndexDefinition>.Empty : new ValueList<IndexDefinition>(indexes);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the table columns.
    /// </summary>
    public ValueList<DatabaseColumn> Columns { get; }

    /// <summary>
    /// Gets the primary key definition, when one exists.
    /// </summary>
    public PrimaryKeyDefinition? PrimaryKey { get; }

    /// <summary>
    /// Gets the foreign key definitions.
    /// </summary>
    public ValueList<ForeignKeyDefinition> ForeignKeys { get; }

    /// <summary>
    /// Gets the index definitions.
    /// </summary>
    public ValueList<IndexDefinition> Indexes { get; }
}

/// <summary>
/// Represents a normalized database column.
/// </summary>
public sealed record DatabaseColumn : ISchemaObject
{
    /// <summary>
    /// Initializes a new database column.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="schemaName">The owning schema name.</param>
    /// <param name="tableName">The owning table name.</param>
    /// <param name="name">The column name.</param>
    /// <param name="dataType">The SQL data type.</param>
    /// <param name="nullability">The column nullability.</param>
    /// <param name="ordinal">The zero-based physical column order.</param>
    public DatabaseColumn(
        string databaseName,
        string schemaName,
        string tableName,
        string name,
        SqlDataType dataType,
        ColumnNullability nullability,
        int ordinal)
    {
        DatabaseName = Guard.Required(databaseName);
        SchemaName = Guard.Required(schemaName);
        TableName = Guard.Required(tableName);
        Name = Guard.Required(name);
        DataType = dataType;
        Nullability = nullability;
        Ordinal = ordinal;
        Identity = LogicalIdentity.Create(SchemaObjectKind.Column, DatabaseName, SchemaName, TableName, Name);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the owning table name.
    /// </summary>
    public string TableName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the SQL data type.
    /// </summary>
    public SqlDataType DataType { get; }

    /// <summary>
    /// Gets the column nullability.
    /// </summary>
    public ColumnNullability Nullability { get; }

    /// <summary>
    /// Gets the zero-based physical column order.
    /// </summary>
    public int Ordinal { get; }
}

/// <summary>
/// Represents a SQL data type in provider store-type form.
/// </summary>
/// <param name="StoreType">The provider store type, such as <c>nvarchar(200)</c> or <c>decimal(18,2)</c>.</param>
/// <param name="ProviderName">The provider name, when known.</param>
/// <param name="MaxLength">The maximum length, when known.</param>
/// <param name="Precision">The numeric precision, when known.</param>
/// <param name="Scale">The numeric scale, when known.</param>
/// <param name="IsUnicode">A value indicating whether the type stores Unicode text.</param>
public sealed record SqlDataType(
    string StoreType,
    string? ProviderName = null,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    bool? IsUnicode = null);

/// <summary>
/// Represents a normalized primary key constraint.
/// </summary>
public sealed record PrimaryKeyDefinition : ISchemaObject
{
    /// <summary>
    /// Initializes a new primary key definition.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="schemaName">The owning schema name.</param>
    /// <param name="tableName">The owning table name.</param>
    /// <param name="name">The primary key name.</param>
    /// <param name="columnIdentities">The key column identities in key order.</param>
    public PrimaryKeyDefinition(string databaseName, string schemaName, string tableName, string name, IEnumerable<LogicalIdentity> columnIdentities)
    {
        DatabaseName = Guard.Required(databaseName);
        SchemaName = Guard.Required(schemaName);
        TableName = Guard.Required(tableName);
        Name = Guard.Required(name);
        Identity = LogicalIdentity.Create(SchemaObjectKind.PrimaryKey, DatabaseName, SchemaName, TableName, Name);
        ColumnIdentities = new ValueList<LogicalIdentity>(columnIdentities);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the owning table name.
    /// </summary>
    public string TableName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the key column identities in key order.
    /// </summary>
    public ValueList<LogicalIdentity> ColumnIdentities { get; }

    /// <summary>
    /// Gets a value indicating whether the primary key spans more than one column.
    /// </summary>
    public bool IsComposite => ColumnIdentities.Count > 1;
}

/// <summary>
/// Represents a column pair in a foreign key relationship.
/// </summary>
/// <param name="DependentColumnIdentity">The dependent table column identity.</param>
/// <param name="PrincipalColumnIdentity">The principal table column identity.</param>
public sealed record ForeignKeyColumnMapping(LogicalIdentity DependentColumnIdentity, LogicalIdentity PrincipalColumnIdentity);

/// <summary>
/// Represents a normalized foreign key constraint.
/// </summary>
public sealed record ForeignKeyDefinition : ISchemaObject
{
    /// <summary>
    /// Initializes a new foreign key definition.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="schemaName">The owning schema name.</param>
    /// <param name="tableName">The dependent table name.</param>
    /// <param name="name">The foreign key name.</param>
    /// <param name="dependentTableIdentity">The dependent table identity.</param>
    /// <param name="principalTableIdentity">The principal table identity.</param>
    /// <param name="columnMappings">The dependent-to-principal column mappings.</param>
    /// <param name="cardinality">The relationship cardinality represented by the foreign key.</param>
    public ForeignKeyDefinition(
        string databaseName,
        string schemaName,
        string tableName,
        string name,
        LogicalIdentity dependentTableIdentity,
        LogicalIdentity principalTableIdentity,
        IEnumerable<ForeignKeyColumnMapping> columnMappings,
        RelationshipCardinality cardinality = RelationshipCardinality.OneToMany)
    {
        DatabaseName = Guard.Required(databaseName);
        SchemaName = Guard.Required(schemaName);
        TableName = Guard.Required(tableName);
        Name = Guard.Required(name);
        DependentTableIdentity = dependentTableIdentity;
        PrincipalTableIdentity = principalTableIdentity;
        ColumnMappings = new ValueList<ForeignKeyColumnMapping>(columnMappings);
        Cardinality = cardinality;
        Identity = LogicalIdentity.Create(SchemaObjectKind.ForeignKey, DatabaseName, SchemaName, TableName, Name);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the dependent table name.
    /// </summary>
    public string TableName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the dependent table identity.
    /// </summary>
    public LogicalIdentity DependentTableIdentity { get; }

    /// <summary>
    /// Gets the principal table identity.
    /// </summary>
    public LogicalIdentity PrincipalTableIdentity { get; }

    /// <summary>
    /// Gets the dependent-to-principal column mappings.
    /// </summary>
    public ValueList<ForeignKeyColumnMapping> ColumnMappings { get; }

    /// <summary>
    /// Gets the relationship cardinality represented by the foreign key.
    /// </summary>
    public RelationshipCardinality Cardinality { get; }
}

/// <summary>
/// Represents a column in an index definition.
/// </summary>
/// <param name="ColumnIdentity">The indexed column identity.</param>
/// <param name="SortDirection">The indexed column sort direction.</param>
/// <param name="Ordinal">The zero-based index column order.</param>
public sealed record IndexColumn(LogicalIdentity ColumnIdentity, IndexSortDirection SortDirection, int Ordinal);

/// <summary>
/// Represents a normalized database index.
/// </summary>
public sealed record IndexDefinition : ISchemaObject
{
    /// <summary>
    /// Initializes a new index definition.
    /// </summary>
    /// <param name="databaseName">The owning database name.</param>
    /// <param name="schemaName">The owning schema name.</param>
    /// <param name="tableName">The owning table name.</param>
    /// <param name="name">The index name.</param>
    /// <param name="columns">The indexed columns.</param>
    /// <param name="isUnique">A value indicating whether the index is unique.</param>
    public IndexDefinition(string databaseName, string schemaName, string tableName, string name, IEnumerable<IndexColumn> columns, bool isUnique)
    {
        DatabaseName = Guard.Required(databaseName);
        SchemaName = Guard.Required(schemaName);
        TableName = Guard.Required(tableName);
        Name = Guard.Required(name);
        Identity = LogicalIdentity.Create(SchemaObjectKind.Index, DatabaseName, SchemaName, TableName, Name);
        Columns = new ValueList<IndexColumn>(columns);
        IsUnique = isUnique;
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <summary>
    /// Gets the owning database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the owning table name.
    /// </summary>
    public string TableName { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the indexed columns.
    /// </summary>
    public ValueList<IndexColumn> Columns { get; }

    /// <summary>
    /// Gets a value indicating whether the index is unique.
    /// </summary>
    public bool IsUnique { get; }
}

/// <summary>
/// Represents a normalized relationship between physical tables or logical resources.
/// </summary>
public sealed record RelationshipDefinition : ISchemaObject
{
    /// <summary>
    /// Initializes a new relationship definition.
    /// </summary>
    /// <param name="name">The relationship name.</param>
    /// <param name="kind">The relationship kind.</param>
    /// <param name="cardinality">The relationship cardinality.</param>
    /// <param name="sourceIdentity">The source object identity.</param>
    /// <param name="targetIdentity">The target object identity.</param>
    /// <param name="physicalType">The physical relationship type, when the relationship is physical.</param>
    /// <param name="logicalType">The logical relationship type, when the relationship is logical.</param>
    public RelationshipDefinition(
        string name,
        RelationshipKind kind,
        RelationshipCardinality cardinality,
        LogicalIdentity sourceIdentity,
        LogicalIdentity targetIdentity,
        PhysicalRelationshipType? physicalType = null,
        LogicalRelationshipType? logicalType = null)
    {
        Name = Guard.Required(name);
        Kind = kind;
        Cardinality = cardinality;
        SourceIdentity = sourceIdentity;
        TargetIdentity = targetIdentity;
        PhysicalType = physicalType;
        LogicalType = logicalType;
        Identity = LogicalIdentity.Create(SchemaObjectKind.Relationship, Name, SourceIdentity.Value, TargetIdentity.Value);
    }

    /// <inheritdoc />
    public LogicalIdentity Identity { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets whether the relationship is physical or logical.
    /// </summary>
    public RelationshipKind Kind { get; }

    /// <summary>
    /// Gets the relationship cardinality.
    /// </summary>
    public RelationshipCardinality Cardinality { get; }

    /// <summary>
    /// Gets the source object identity.
    /// </summary>
    public LogicalIdentity SourceIdentity { get; }

    /// <summary>
    /// Gets the target object identity.
    /// </summary>
    public LogicalIdentity TargetIdentity { get; }

    /// <summary>
    /// Gets the physical relationship type, when available.
    /// </summary>
    public PhysicalRelationshipType? PhysicalType { get; }

    /// <summary>
    /// Gets the logical relationship type, when available.
    /// </summary>
    public LogicalRelationshipType? LogicalType { get; }
}

internal static class Guard
{
    public static string Required(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        return value.Trim();
    }
}

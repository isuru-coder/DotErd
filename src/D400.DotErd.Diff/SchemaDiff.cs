using System.Text;
using System.Text.Json;
using D400.DotErd.Core;

namespace D400.DotErd.Diff;

/// <summary>
/// Compares normalized database schema snapshots and formats schema difference reports.
/// </summary>
public static class SchemaDiff
{
    /// <summary>
    /// Compares two normalized schema models while ignoring non-semantic ordering differences.
    /// </summary>
    /// <param name="oldModel">The baseline schema model.</param>
    /// <param name="newModel">The current schema model.</param>
    /// <returns>The schema difference result.</returns>
    public static SchemaDiffResult Compare(DatabaseModel oldModel, DatabaseModel newModel)
    {
        ArgumentNullException.ThrowIfNull(oldModel);
        ArgumentNullException.ThrowIfNull(newModel);

        return Compare(SchemaSnapshot.FromModel(oldModel), SchemaSnapshot.FromModel(newModel));
    }

    /// <summary>
    /// Compares two serialized schema snapshots while ignoring non-semantic ordering differences.
    /// </summary>
    /// <param name="oldJson">The baseline schema JSON.</param>
    /// <param name="newJson">The current schema JSON.</param>
    /// <returns>The schema difference result.</returns>
    public static SchemaDiffResult CompareJson(string oldJson, string newJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(newJson);

        return Compare(SchemaSnapshot.FromJson(oldJson), SchemaSnapshot.FromJson(newJson));
    }

    private static SchemaDiffResult Compare(SchemaSnapshot oldSnapshot, SchemaSnapshot newSnapshot)
    {
        var changes = new List<SchemaChange>();

        CompareDictionary(oldSnapshot.Schemas, newSnapshot.Schemas, "schema", changes);
        CompareDictionary(oldSnapshot.Tables, newSnapshot.Tables, "table", changes);
        CompareDictionary(oldSnapshot.Columns, newSnapshot.Columns, "column", changes, CompareColumns);
        CompareDictionary(oldSnapshot.PrimaryKeys, newSnapshot.PrimaryKeys, "primary key", changes, ComparePrimaryKeys);
        CompareDictionary(oldSnapshot.ForeignKeys, newSnapshot.ForeignKeys, "foreign key", changes, CompareForeignKeys);
        CompareDictionary(oldSnapshot.Indexes, newSnapshot.Indexes, "index", changes, CompareIndexes);

        return new SchemaDiffResult(changes.OrderBy(change => change.SortKey, StringComparer.Ordinal).ToArray());
    }

    private static void CompareDictionary<T>(
        IReadOnlyDictionary<string, T> oldItems,
        IReadOnlyDictionary<string, T> newItems,
        string objectType,
        ICollection<SchemaChange> changes,
        Func<T, T, string?>? changeDetail = null)
        where T : ISnapshotItem
    {
        foreach (var removed in oldItems.Keys.Except(newItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(SchemaChange.Removed(objectType, removed, oldItems[removed].DisplayName));
        }

        foreach (var added in newItems.Keys.Except(oldItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(SchemaChange.Added(objectType, added, newItems[added].DisplayName));
        }

        if (changeDetail is null)
        {
            return;
        }

        foreach (var key in oldItems.Keys.Intersect(newItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var detail = changeDetail(oldItems[key], newItems[key]);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                changes.Add(SchemaChange.Changed(objectType, key, newItems[key].DisplayName, detail));
            }
        }
    }

    private static string? CompareColumns(ColumnSnapshot oldColumn, ColumnSnapshot newColumn)
    {
        var details = new List<string>();
        if (!string.Equals(oldColumn.StoreType, newColumn.StoreType, StringComparison.OrdinalIgnoreCase))
        {
            details.Add($"SQL type `{oldColumn.StoreType}` -> `{newColumn.StoreType}`");
        }

        if (!string.Equals(oldColumn.Nullability, newColumn.Nullability, StringComparison.OrdinalIgnoreCase))
        {
            details.Add($"nullability `{oldColumn.Nullability}` -> `{newColumn.Nullability}`");
        }

        return details.Count == 0 ? null : string.Join("; ", details);
    }

    private static string? ComparePrimaryKeys(KeySnapshot oldKey, KeySnapshot newKey)
    {
        return oldKey.Signature == newKey.Signature ? null : $"columns `{oldKey.Signature}` -> `{newKey.Signature}`";
    }

    private static string? CompareForeignKeys(ForeignKeySnapshot oldKey, ForeignKeySnapshot newKey)
    {
        return oldKey.Signature == newKey.Signature ? null : $"definition `{oldKey.Signature}` -> `{newKey.Signature}`";
    }

    private static string? CompareIndexes(IndexSnapshot oldIndex, IndexSnapshot newIndex)
    {
        return oldIndex.Signature == newIndex.Signature ? null : $"definition `{oldIndex.Signature}` -> `{newIndex.Signature}`";
    }

    private interface ISnapshotItem
    {
        string DisplayName { get; }
    }

    private sealed record NamedSnapshot(string DisplayName) : ISnapshotItem;

    private sealed record ColumnSnapshot(string DisplayName, string StoreType, string Nullability) : ISnapshotItem;

    private sealed record KeySnapshot(string DisplayName, string Signature) : ISnapshotItem;

    private sealed record ForeignKeySnapshot(string DisplayName, string Signature) : ISnapshotItem;

    private sealed record IndexSnapshot(string DisplayName, string Signature) : ISnapshotItem;

    private sealed record SchemaSnapshot(
        IReadOnlyDictionary<string, NamedSnapshot> Schemas,
        IReadOnlyDictionary<string, NamedSnapshot> Tables,
        IReadOnlyDictionary<string, ColumnSnapshot> Columns,
        IReadOnlyDictionary<string, KeySnapshot> PrimaryKeys,
        IReadOnlyDictionary<string, ForeignKeySnapshot> ForeignKeys,
        IReadOnlyDictionary<string, IndexSnapshot> Indexes)
    {
        public static SchemaSnapshot FromModel(DatabaseModel model)
        {
            var schemas = new Dictionary<string, NamedSnapshot>(StringComparer.Ordinal);
            var tables = new Dictionary<string, NamedSnapshot>(StringComparer.Ordinal);
            var columns = new Dictionary<string, ColumnSnapshot>(StringComparer.Ordinal);
            var primaryKeys = new Dictionary<string, KeySnapshot>(StringComparer.Ordinal);
            var foreignKeys = new Dictionary<string, ForeignKeySnapshot>(StringComparer.Ordinal);
            var indexes = new Dictionary<string, IndexSnapshot>(StringComparer.Ordinal);

            foreach (var schema in model.Schemas)
            {
                schemas[schema.Identity.Value] = new NamedSnapshot(schema.Name);

                foreach (var table in schema.Tables)
                {
                    tables[table.Identity.Value] = new NamedSnapshot($"{table.SchemaName}.{table.Name}");

                    foreach (var column in table.Columns)
                    {
                        columns[column.Identity.Value] = new ColumnSnapshot(
                            $"{table.SchemaName}.{table.Name}.{column.Name}",
                            column.DataType.StoreType,
                            column.Nullability.ToString());
                    }

                    if (table.PrimaryKey is not null)
                    {
                        primaryKeys[table.Identity.Value] = new KeySnapshot(
                            $"{table.SchemaName}.{table.Name} primary key",
                            JoinIdentities(table.PrimaryKey.ColumnIdentities));
                    }

                    foreach (var foreignKey in table.ForeignKeys)
                    {
                        foreignKeys[foreignKey.Identity.Value] = new ForeignKeySnapshot(
                            $"{table.SchemaName}.{table.Name}.{foreignKey.Name}",
                            $"{foreignKey.DependentTableIdentity.Value}->{foreignKey.PrincipalTableIdentity.Value}|{foreignKey.Cardinality}|{JoinMappings(foreignKey.ColumnMappings)}");
                    }

                    foreach (var index in table.Indexes)
                    {
                        indexes[index.Identity.Value] = new IndexSnapshot(
                            $"{table.SchemaName}.{table.Name}.{index.Name}",
                            $"{index.IsUnique}|{JoinIndexColumns(index.Columns)}");
                    }
                }
            }

            return new SchemaSnapshot(schemas, tables, columns, primaryKeys, foreignKeys, indexes);
        }

        public static SchemaSnapshot FromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var schemas = new Dictionary<string, NamedSnapshot>(StringComparer.Ordinal);
            var tables = new Dictionary<string, NamedSnapshot>(StringComparer.Ordinal);
            var columns = new Dictionary<string, ColumnSnapshot>(StringComparer.Ordinal);
            var primaryKeys = new Dictionary<string, KeySnapshot>(StringComparer.Ordinal);
            var foreignKeys = new Dictionary<string, ForeignKeySnapshot>(StringComparer.Ordinal);
            var indexes = new Dictionary<string, IndexSnapshot>(StringComparer.Ordinal);

            foreach (var schema in EnumerateArray(root, "Schemas"))
            {
                var schemaName = GetString(schema, "Name");
                schemas[GetIdentity(schema)] = new NamedSnapshot(schemaName);

                foreach (var table in EnumerateArray(schema, "Tables"))
                {
                    var tableIdentity = GetIdentity(table);
                    var schemaNameForTable = GetString(table, "SchemaName");
                    var tableName = GetString(table, "Name");
                    tables[tableIdentity] = new NamedSnapshot($"{schemaNameForTable}.{tableName}");

                    foreach (var column in EnumerateArray(table, "Columns"))
                    {
                        var columnName = GetString(column, "Name");
                        columns[GetIdentity(column)] = new ColumnSnapshot(
                            $"{schemaNameForTable}.{tableName}.{columnName}",
                            GetProperty(column, "DataType").ValueKind == JsonValueKind.Object ? GetString(GetProperty(column, "DataType"), "StoreType") : string.Empty,
                            GetEnumName<ColumnNullability>(column, "Nullability"));
                    }

                    if (TryGetProperty(table, "PrimaryKey", out var primaryKey) && primaryKey.ValueKind == JsonValueKind.Object)
                    {
                        primaryKeys[tableIdentity] = new KeySnapshot(
                            $"{schemaNameForTable}.{tableName} primary key",
                            JoinJsonIdentities(primaryKey, "ColumnIdentities"));
                    }

                    foreach (var foreignKey in EnumerateArray(table, "ForeignKeys"))
                    {
                        foreignKeys[GetIdentity(foreignKey)] = new ForeignKeySnapshot(
                            $"{schemaNameForTable}.{tableName}.{GetString(foreignKey, "Name")}",
                            $"{GetIdentityValue(foreignKey, "DependentTableIdentity")}->{GetIdentityValue(foreignKey, "PrincipalTableIdentity")}|{GetEnumName<RelationshipCardinality>(foreignKey, "Cardinality")}|{JoinJsonMappings(foreignKey)}");
                    }

                    foreach (var index in EnumerateArray(table, "Indexes"))
                    {
                        indexes[GetIdentity(index)] = new IndexSnapshot(
                            $"{schemaNameForTable}.{tableName}.{GetString(index, "Name")}",
                            $"{GetBoolean(index, "IsUnique")}|{JoinJsonIndexColumns(index)}");
                    }
                }
            }

            return new SchemaSnapshot(schemas, tables, columns, primaryKeys, foreignKeys, indexes);
        }

        private static string JoinIdentities(IEnumerable<LogicalIdentity> identities)
        {
            return string.Join(",", identities.Select(identity => identity.Value).Order(StringComparer.Ordinal));
        }

        private static string JoinMappings(IEnumerable<ForeignKeyColumnMapping> mappings)
        {
            return string.Join(",", mappings
                .Select(mapping => $"{mapping.DependentColumnIdentity.Value}->{mapping.PrincipalColumnIdentity.Value}")
                .Order(StringComparer.Ordinal));
        }

        private static string JoinIndexColumns(IEnumerable<IndexColumn> columns)
        {
            return string.Join(",", columns
                .OrderBy(column => column.Ordinal)
                .Select(column => $"{column.ColumnIdentity.Value}:{column.SortDirection}"));
        }

        private static string JoinJsonIdentities(JsonElement element, string propertyName)
        {
            return string.Join(",", EnumerateArray(element, propertyName)
                .Select(GetIdentityValue)
                .Order(StringComparer.Ordinal));
        }

        private static string JoinJsonMappings(JsonElement foreignKey)
        {
            return string.Join(",", EnumerateArray(foreignKey, "ColumnMappings")
                .Select(mapping => $"{GetIdentityValue(mapping, "DependentColumnIdentity")}->{GetIdentityValue(mapping, "PrincipalColumnIdentity")}")
                .Order(StringComparer.Ordinal));
        }

        private static string JoinJsonIndexColumns(JsonElement index)
        {
            return string.Join(",", EnumerateArray(index, "Columns")
                .OrderBy(column => GetInt32(column, "Ordinal"))
                .Select(column => $"{GetIdentityValue(column, "ColumnIdentity")}:{GetEnumName<IndexSortDirection>(column, "SortDirection")}"));
        }

        private static IEnumerable<JsonElement> EnumerateArray(JsonElement element, string propertyName)
        {
            return TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.Array
                ? property.EnumerateArray()
                : [];
        }

        private static JsonElement GetProperty(JsonElement element, string propertyName)
        {
            return TryGetProperty(element, propertyName, out var property)
                ? property
                : default;
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
            {
                return true;
            }

            property = default;
            return false;
        }

        private static string GetIdentity(JsonElement element)
        {
            return GetIdentityValue(GetProperty(element, "Identity"));
        }

        private static string GetIdentityValue(JsonElement element, string propertyName)
        {
            return GetIdentityValue(GetProperty(element, propertyName));
        }

        private static string GetIdentityValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("Value", out var value))
            {
                return value.GetString() ?? string.Empty;
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool GetBoolean(JsonElement element, string propertyName)
        {
            return TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.True;
        }

        private static int GetInt32(JsonElement element, string propertyName)
        {
            return TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : 0;
        }

        private static string GetEnumName<TEnum>(JsonElement element, string propertyName)
            where TEnum : struct, Enum
        {
            if (!TryGetProperty(element, propertyName, out var property))
            {
                return string.Empty;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            if (property.ValueKind == JsonValueKind.Number && Enum.IsDefined(typeof(TEnum), property.GetInt32()))
            {
                return ((TEnum)Enum.ToObject(typeof(TEnum), property.GetInt32())).ToString();
            }

            return string.Empty;
        }
    }
}

/// <summary>
/// Represents one semantic schema change.
/// </summary>
/// <param name="ChangeType">The change type.</param>
/// <param name="ObjectType">The schema object type.</param>
/// <param name="Identity">The deterministic object identity.</param>
/// <param name="DisplayName">The human-readable object name.</param>
/// <param name="Detail">The change detail, when available.</param>
public sealed record SchemaChange(string ChangeType, string ObjectType, string Identity, string DisplayName, string? Detail = null)
{
    internal string SortKey => $"{ObjectType}|{Identity}|{ChangeType}";

    public static SchemaChange Added(string objectType, string identity, string displayName)
    {
        return new SchemaChange("Added", objectType, identity, displayName);
    }

    public static SchemaChange Removed(string objectType, string identity, string displayName)
    {
        return new SchemaChange("Removed", objectType, identity, displayName);
    }

    public static SchemaChange Changed(string objectType, string identity, string displayName, string detail)
    {
        return new SchemaChange("Changed", objectType, identity, displayName, detail);
    }
}

/// <summary>
/// Represents the result of a semantic schema comparison.
/// </summary>
public sealed record SchemaDiffResult
{
    /// <summary>
    /// Initializes a schema difference result.
    /// </summary>
    /// <param name="changes">The detected semantic changes.</param>
    public SchemaDiffResult(IReadOnlyList<SchemaChange> changes)
    {
        Changes = changes;
    }

    /// <summary>
    /// Gets the detected semantic changes.
    /// </summary>
    public IReadOnlyList<SchemaChange> Changes { get; }

    /// <summary>
    /// Gets a value indicating whether any semantic differences were detected.
    /// </summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>
    /// Creates a concise console summary.
    /// </summary>
    /// <returns>The console summary.</returns>
    public string ToConsoleSummary()
    {
        if (!HasChanges)
        {
            return "Schema is current. No semantic differences detected.";
        }

        var groups = Changes
            .GroupBy(change => $"{change.ChangeType} {change.ObjectType}", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {group.Count()}");

        return $"Schema differences detected: {string.Join(", ", groups)}.";
    }

    /// <summary>
    /// Creates a readable Markdown report.
    /// </summary>
    /// <returns>The Markdown report.</returns>
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Schema Difference Report");
        builder.AppendLine();

        if (!HasChanges)
        {
            builder.AppendLine("No semantic differences detected.");
            return builder.ToString();
        }

        foreach (var group in Changes.GroupBy(change => change.ObjectType).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"## {ToTitle(group.Key)}");
            builder.AppendLine();

            foreach (var change in group.OrderBy(change => change.ChangeType, StringComparer.Ordinal).ThenBy(change => change.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var detail = string.IsNullOrWhiteSpace(change.Detail) ? string.Empty : $" - {change.Detail}";
                builder.AppendLine($"- **{change.ChangeType}** `{change.DisplayName}`{detail}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ToTitle(string value)
    {
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

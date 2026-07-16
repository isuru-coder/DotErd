using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using D400.DotErd.Core;

namespace D400.DotErd.Drawio;

/// <summary>
/// Generates native uncompressed draw.io XML for normalized database schema models.
/// </summary>
public static class DrawioXmlGenerator
{
    private const string RootCellId = "0";
    private const string DocumentCellId = "1";
    private const string GeneratedLayerId = "d400-layer-generated-erd";
    private const string ManualLayerId = "d400-layer-manual-notes";
    private const string TableIdPrefix = "d400-table-";
    private const string ColumnIdPrefix = "d400-column-";
    private const string RelationshipIdPrefix = "d400-relationship-";

    /// <summary>
    /// Generates an uncompressed draw.io document.
    /// </summary>
    /// <param name="model">The normalized database model.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>A byte-stable draw.io XML string where practical.</returns>
    public static string Generate(DatabaseModel model, DrawioGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new DrawioGenerationOptions();

        return ToStableString(CreateDocument(model, options));
    }

    /// <summary>
    /// Updates an existing uncompressed draw.io document while preserving generated element layout where possible.
    /// </summary>
    /// <param name="model">The normalized database model.</param>
    /// <param name="existingDrawioXml">The existing draw.io XML.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The updated draw.io XML.</returns>
    /// <exception cref="DrawioXmlUpdateException">Thrown when the existing draw.io XML cannot be parsed or updated safely.</exception>
    public static string Update(DatabaseModel model, string existingDrawioXml, DrawioGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingDrawioXml);
        options ??= new DrawioGenerationOptions();

        XDocument existingDocument;
        try
        {
            existingDocument = XDocument.Parse(existingDrawioXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw new DrawioXmlUpdateException("The existing draw.io file is not valid XML. No changes were written.", exception);
        }

        try
        {
            var existingRoot = GetDrawioRoot(existingDocument);
            var layout = CaptureLayout(existingRoot);
            var updatedDocument = new XDocument(existingDocument);
            var updatedRoot = GetDrawioRoot(updatedDocument);

            EnsureBaseCells(updatedRoot);
            RemoveGeneratedCells(updatedRoot);
            AddGeneratedCells(updatedRoot, model, options, layout);

            return ToStableString(updatedDocument);
        }
        catch (DrawioXmlUpdateException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DrawioXmlUpdateException("The existing draw.io file could not be updated safely. No changes were written.", exception);
        }
    }

    private static XDocument CreateDocument(DatabaseModel model, DrawioGenerationOptions options)
    {
        var root = CreateBaseRoot();
        AddGeneratedCells(root, model, options, ExistingLayout.Empty);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("mxfile",
                new XAttribute("host", "D400.DotErd"),
                new XAttribute("type", "device"),
                new XAttribute("compressed", "false"),
                new XAttribute("version", "24.7.17"),
                new XElement("diagram",
                    new XAttribute("id", ToMxCellId("diagram", model.Identity)),
                    new XAttribute("name", options.PageName),
                    new XElement("mxGraphModel",
                        new XAttribute("dx", "1200"),
                        new XAttribute("dy", "800"),
                        new XAttribute("grid", "1"),
                        new XAttribute("gridSize", "10"),
                        new XAttribute("guides", "1"),
                        new XAttribute("tooltips", "1"),
                        new XAttribute("connect", "1"),
                        new XAttribute("arrows", "1"),
                        new XAttribute("fold", "1"),
                        new XAttribute("page", "1"),
                        new XAttribute("pageScale", "1"),
                        new XAttribute("pageWidth", "1600"),
                        new XAttribute("pageHeight", "1200"),
                        new XAttribute("math", "0"),
                        new XAttribute("shadow", "0"),
                        root))));
    }

    private static XElement CreateBaseRoot()
    {
        return new XElement("root",
            new XElement("mxCell", new XAttribute("id", RootCellId)),
            new XElement("mxCell", new XAttribute("id", DocumentCellId), new XAttribute("parent", RootCellId)),
            new XElement("mxCell", new XAttribute("id", GeneratedLayerId), new XAttribute("value", "Generated ERD"), new XAttribute("parent", RootCellId)),
            new XElement("mxCell", new XAttribute("id", ManualLayerId), new XAttribute("value", "Manual Notes"), new XAttribute("parent", RootCellId)));
    }

    private static void AddGeneratedCells(XElement root, DatabaseModel model, DrawioGenerationOptions options, ExistingLayout layout)
    {
        var tables = GetTables(model).ToArray();
        var tableIds = tables.ToDictionary(table => table.Identity, table => ToMxCellId("table", table.Identity));
        var occupiedTableGeometries = new List<TableGeometry>();

        for (var index = 0; index < tables.Length; index++)
        {
            var table = tables[index];
            var tableId = tableIds[table.Identity];
            var geometry = layout.TableGeometries.TryGetValue(tableId, out var existingGeometry)
                ? existingGeometry
                : FindNearestAvailableGridGeometry(index, GetTableHeight(table, options), options, occupiedTableGeometries);

            occupiedTableGeometries.Add(geometry.ToTableGeometry());
            root.Add(CreateTableCells(table, tableId, options, geometry));
        }

        foreach (var relationship in GetRelationships(model))
        {
            if (!tableIds.TryGetValue(relationship.SourceIdentity, out var sourceId)
                || !tableIds.TryGetValue(relationship.TargetIdentity, out var targetId))
            {
                continue;
            }

            var relationshipId = ToMxCellId("relationship", relationship.Identity);
            var relationshipGeometry = layout.RelationshipGeometries.GetValueOrDefault(relationshipId);
            root.Add(CreateRelationshipCell(relationship, relationshipId, sourceId, targetId, relationshipGeometry));
        }
    }

    private static XElement GetDrawioRoot(XDocument document)
    {
        return document.Descendants("mxGraphModel").FirstOrDefault()?.Element("root")
            ?? throw new DrawioXmlUpdateException("The existing draw.io file does not contain an mxGraphModel root. No changes were written.");
    }

    private static void EnsureBaseCells(XElement root)
    {
        EnsureCell(root, RootCellId, null, null);
        EnsureCell(root, DocumentCellId, RootCellId, null);
        EnsureCell(root, GeneratedLayerId, RootCellId, "Generated ERD");
        EnsureCell(root, ManualLayerId, RootCellId, "Manual Notes");
    }

    private static void EnsureCell(XElement root, string id, string? parent, string? value)
    {
        var cell = root.Elements("mxCell").FirstOrDefault(element => element.Attribute("id")?.Value == id);
        if (cell is null)
        {
            cell = new XElement("mxCell", new XAttribute("id", id));
            root.AddFirst(cell);
        }

        if (parent is not null && cell.Attribute("parent") is null)
        {
            cell.Add(new XAttribute("parent", parent));
        }

        if (value is not null && cell.Attribute("value") is null)
        {
            cell.Add(new XAttribute("value", value));
        }
    }

    private static ExistingLayout CaptureLayout(XElement root)
    {
        var tableGeometries = new Dictionary<string, CellGeometry>(StringComparer.Ordinal);
        var relationshipGeometries = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var cell in root.Elements("mxCell"))
        {
            var id = cell.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var geometry = cell.Element("mxGeometry");
            if (id.StartsWith(TableIdPrefix, StringComparison.Ordinal) && geometry is not null)
            {
                tableGeometries[id] = CellGeometry.From(geometry);
            }
            else if (id.StartsWith(RelationshipIdPrefix, StringComparison.Ordinal) && geometry is not null)
            {
                relationshipGeometries[id] = new XElement(geometry);
            }
        }

        return new ExistingLayout(tableGeometries, relationshipGeometries);
    }

    private static void RemoveGeneratedCells(XElement root)
    {
        root.Elements("mxCell")
            .Where(cell =>
            {
                var id = cell.Attribute("id")?.Value ?? string.Empty;
                var parent = cell.Attribute("parent")?.Value;
                return !string.Equals(parent, ManualLayerId, StringComparison.Ordinal)
                    && (id.StartsWith(TableIdPrefix, StringComparison.Ordinal)
                        || id.StartsWith(ColumnIdPrefix, StringComparison.Ordinal)
                        || id.StartsWith(RelationshipIdPrefix, StringComparison.Ordinal)
                        || parent?.StartsWith(TableIdPrefix, StringComparison.Ordinal) == true
                        || (string.Equals(parent, GeneratedLayerId, StringComparison.Ordinal)
                            && !string.Equals(id, GeneratedLayerId, StringComparison.Ordinal)));
            })
            .Remove();
    }

    /// <summary>
    /// Converts a Core logical identity into a deterministic draw.io cell identifier.
    /// </summary>
    /// <param name="prefix">The identifier prefix.</param>
    /// <param name="identity">The Core logical identity.</param>
    /// <returns>A deterministic draw.io cell identifier.</returns>
    public static string ToMxCellId(string prefix, LogicalIdentity identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity.Value));
        return string.Create(CultureInfo.InvariantCulture, $"d400-{prefix}-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}");
    }

    private static IEnumerable<XElement> CreateTableCells(DatabaseTable table, string cellId, DrawioGenerationOptions options, CellGeometry geometry)
    {
        yield return new XElement("mxCell",
            new XAttribute("id", cellId),
            new XAttribute("value", $"{table.SchemaName}.{table.Name}"),
            new XAttribute("style", "shape=table;startSize=34;container=1;collapsible=0;childLayout=tableLayout;fixedRows=1;rowLines=1;columnLines=0;resizeLast=1;html=1;whiteSpace=wrap;align=left;verticalAlign=middle;spacingLeft=8;strokeColor=#2f3a45;strokeWidth=1.5;fillColor=#ffffff;swimlaneFillColor=#e9eef3;fontColor=#000000;fontFamily=Consolas;fontSize=12;fontStyle=1;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", GeneratedLayerId),
            new XElement("mxGeometry",
                new XAttribute("x", geometry.X),
                new XAttribute("y", geometry.Y),
                new XAttribute("width", geometry.Width),
                new XAttribute("height", geometry.Height),
                new XAttribute("as", "geometry")));

        foreach (var column in table.Columns.OrderBy(column => column.Ordinal).ThenBy(column => column.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new XElement("mxCell",
                new XAttribute("id", ToMxCellId("column", column.Identity)),
                new XAttribute("value", CreateColumnLabel(table, column)),
                new XAttribute("style", CreateColumnValueStyle(table, column)),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", cellId),
                new XElement("mxGeometry",
                    new XAttribute("x", "0"),
                    new XAttribute("y", (options.HeaderHeight + column.Ordinal * options.ColumnHeight).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("width", geometry.Width),
                    new XAttribute("height", options.ColumnHeight.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("as", "geometry")));
        }
    }

    private static XElement CreateRelationshipCell(RelationshipDefinition relationship, string relationshipId, string sourceId, string targetId, XElement? preservedGeometry)
    {
        var geometry = preservedGeometry is null
            ? new XElement("mxGeometry",
                new XAttribute("relative", "1"),
                new XAttribute("as", "geometry"))
            : new XElement(preservedGeometry);

        return new XElement("mxCell",
            new XAttribute("id", relationshipId),
            new XAttribute("value", string.Empty),
            new XAttribute("style", CreateRelationshipStyle(relationship)),
            new XAttribute("edge", "1"),
            new XAttribute("parent", GeneratedLayerId),
            new XAttribute("source", sourceId),
            new XAttribute("target", targetId),
            geometry);
    }

    private static int GetTableHeight(DatabaseTable table, DrawioGenerationOptions options)
    {
        return options.HeaderHeight + Math.Max(1, table.Columns.Count) * options.ColumnHeight;
    }

    private static CellGeometry FindNearestAvailableGridGeometry(int preferredIndex, int tableHeight, DrawioGenerationOptions options, IReadOnlyCollection<TableGeometry> occupied)
    {
        var preferred = CreateGridGeometry(preferredIndex, tableHeight, options);

        for (var slotCount = Math.Max(16, occupied.Count + 16); slotCount < 4096; slotCount *= 2)
        {
            foreach (var candidate in Enumerable.Range(0, slotCount)
                .Select(index => CreateGridGeometry(index, tableHeight, options))
                .OrderBy(candidate => Math.Abs(candidate.NumericX - preferred.NumericX) + Math.Abs(candidate.NumericY - preferred.NumericY))
                .ThenBy(candidate => candidate.NumericY)
                .ThenBy(candidate => candidate.NumericX))
            {
                if (occupied.All(existing => !candidate.ToTableGeometry().Intersects(existing)))
                {
                    return candidate;
                }
            }
        }

        return CreateGridGeometry(4096 + occupied.Count, tableHeight, options);
    }

    private static CellGeometry CreateGridGeometry(int index, int tableHeight, DrawioGenerationOptions options)
    {
        var gridColumns = Math.Max(1, options.GridColumns);
        var x = 40 + index % gridColumns * options.HorizontalSpacing;
        var y = 40 + index / gridColumns * options.VerticalSpacing;

        return CellGeometry.Create(x, y, options.TableWidth, tableHeight);
    }

    private static string CreateRelationshipStyle(RelationshipDefinition relationship)
    {
        var (startArrow, endArrow) = relationship.Cardinality switch
        {
            RelationshipCardinality.OneToOne => ("ERone", "ERone"),
            RelationshipCardinality.ManyToMany => ("ERmany", "ERmany"),
            _ => ("ERone", "ERmany")
        };

        return $"edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;startArrow={startArrow};startFill=0;startSize=12;endArrow={endArrow};endFill=0;endSize=12;strokeWidth=3;strokeColor=#333333;fontSize=11;";
    }

    private static string CreateColumnLabel(DatabaseTable table, DatabaseColumn column)
    {
        return $"{GetColumnIndicators(table, column)} {column.Name} : {column.DataType.StoreType}";
    }

    private static string CreateColumnValueStyle(DatabaseTable table, DatabaseColumn column)
    {
        var fontStyle = table.PrimaryKey?.ColumnIdentities.Contains(column.Identity) == true ? "5" : "0";
        return $"shape=partialRectangle;top=0;left=0;right=0;bottom=1;fillColor=#ffffff;strokeColor=#9aa4af;strokeWidth=1.25;html=1;whiteSpace=wrap;overflow=hidden;connectable=0;align=left;verticalAlign=middle;spacingLeft=8;fontColor=#000000;fontFamily=Consolas;fontSize=12;fontStyle={fontStyle};";
    }

    private static string GetColumnIndicators(DatabaseTable table, DatabaseColumn column)
    {
        var indicators = GetKeyIndicators(table, column);
        indicators.Add(column.Nullability == ColumnNullability.Nullable ? "NULL" : "REQ");
        return string.Join(" ", indicators);
    }

    private static List<string> GetKeyIndicators(DatabaseTable table, DatabaseColumn column)
    {
        var indicators = new List<string>();
        if (table.PrimaryKey?.ColumnIdentities.Contains(column.Identity) == true)
        {
            indicators.Add("PK");
        }

        if (table.ForeignKeys.Any(foreignKey => foreignKey.ColumnMappings.Any(mapping => mapping.DependentColumnIdentity == column.Identity)))
        {
            indicators.Add("FK");
        }

        return indicators;
    }

    private static IEnumerable<DatabaseTable> GetTables(DatabaseModel model)
    {
        return model.Schemas
            .OrderBy(schema => schema.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(schema => schema.Tables.OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<RelationshipDefinition> GetRelationships(DatabaseModel model)
    {
        return model.Relationships
            .Where(relationship => relationship.Kind == RelationshipKind.Physical && relationship.PhysicalType == PhysicalRelationshipType.ForeignKey)
            .OrderBy(relationship => relationship.Identity.Value, StringComparer.Ordinal);
    }

    private static string ToStableString(XDocument document)
    {
        using var stream = new MemoryStream();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.None
        });
        document.Save(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed record ExistingLayout(
        IReadOnlyDictionary<string, CellGeometry> TableGeometries,
        IReadOnlyDictionary<string, XElement> RelationshipGeometries)
    {
        public static ExistingLayout Empty { get; } = new(
            new Dictionary<string, CellGeometry>(StringComparer.Ordinal),
            new Dictionary<string, XElement>(StringComparer.Ordinal));
    }

    private sealed record CellGeometry(string X, string Y, string Width, string Height)
    {
        public decimal NumericX => ParseDecimal(X);

        public decimal NumericY => ParseDecimal(Y);

        public decimal NumericWidth => ParseDecimal(Width);

        public decimal NumericHeight => ParseDecimal(Height);

        public static CellGeometry Create(int x, int y, int width, int height)
        {
            return new CellGeometry(
                x.ToString(CultureInfo.InvariantCulture),
                y.ToString(CultureInfo.InvariantCulture),
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture));
        }

        public static CellGeometry From(XElement geometry)
        {
            return new CellGeometry(
                geometry.Attribute("x")?.Value ?? "0",
                geometry.Attribute("y")?.Value ?? "0",
                geometry.Attribute("width")?.Value ?? "0",
                geometry.Attribute("height")?.Value ?? "0");
        }

        public TableGeometry ToTableGeometry()
        {
            return new TableGeometry(NumericX, NumericY, NumericWidth, NumericHeight);
        }

        private static decimal ParseDecimal(string value)
        {
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }
    }

    private sealed record TableGeometry(decimal X, decimal Y, decimal Width, decimal Height)
    {
        public bool Intersects(TableGeometry other)
        {
            return X < other.X + other.Width
                && X + Width > other.X
                && Y < other.Y + other.Height
                && Y + Height > other.Y;
        }
    }
}

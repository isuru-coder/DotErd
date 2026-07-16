using System.Xml.Linq;
using D400.DotErd.Core;

namespace D400.DotErd.Drawio.Tests;

public sealed class DrawioXmlUpdateTests
{
    [Fact]
    public void Update_AddingTable_PreservesExistingPositionsAndPlacesNewTableOnGrid()
    {
        var existingXml = DrawioXmlGenerator.Generate(CreateModel(includeOrders: true));

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: true, includeProducts: true), existingXml);
        var document = XDocument.Parse(updated);

        var customers = Geometry(document, TableIdentity("Customers"));
        var orders = Geometry(document, TableIdentity("Orders", "sales"));
        var products = Geometry(document, TableIdentity("Products"));

        Assert.Equal("400", customers.Attribute("x")?.Value);
        Assert.Equal("40", customers.Attribute("y")?.Value);
        Assert.Equal("40", orders.Attribute("x")?.Value);
        Assert.Equal("40", orders.Attribute("y")?.Value);
        Assert.Equal("760", products.Attribute("x")?.Value);
        Assert.Equal("40", products.Attribute("y")?.Value);
    }

    [Fact]
    public void Update_RemovingTable_RemovesGeneratedTableAndRelationshipCells()
    {
        var existingXml = DrawioXmlGenerator.Generate(CreateModel(includeOrders: true, includeProducts: true));

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: false, includeProducts: false), existingXml);
        var document = XDocument.Parse(updated);

        Assert.DoesNotContain(document.Descendants("mxCell"), cell => cell.Attribute("id")?.Value == DrawioXmlGenerator.ToMxCellId("table", TableIdentity("Orders", "sales")));
        Assert.DoesNotContain(document.Descendants("mxCell"), cell => cell.Attribute("edge")?.Value == "1");
        Assert.Contains(document.Descendants("mxCell"), cell => cell.Attribute("id")?.Value == DrawioXmlGenerator.ToMxCellId("table", TableIdentity("Customers")));
    }

    [Fact]
    public void Update_AddingColumn_UpdatesTableLabelButKeepsGeometry()
    {
        var existingDocument = XDocument.Parse(DrawioXmlGenerator.Generate(CreateModel(includeOrders: false)));
        SetGeometry(existingDocument, TableIdentity("Customers"), x: "123", y: "234", width: "345", height: "456");

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: false, includeCustomerPhone: true), existingDocument.ToString(SaveOptions.DisableFormatting));
        var updatedDocument = XDocument.Parse(updated);
        var customerCell = TableCell(updatedDocument, TableIdentity("Customers"));
        var geometry = RequiredGeometry(customerCell);

        Assert.Contains(ColumnValues(updatedDocument, TableIdentity("Customers")), value => value == "NULL PhoneNumber : nvarchar(32)");
        Assert.Equal("123", geometry.Attribute("x")?.Value);
        Assert.Equal("234", geometry.Attribute("y")?.Value);
        Assert.Equal("345", geometry.Attribute("width")?.Value);
        Assert.Equal("456", geometry.Attribute("height")?.Value);
    }

    [Fact]
    public void Update_RemovingColumn_UpdatesTableLabel()
    {
        var existingXml = DrawioXmlGenerator.Generate(CreateModel(includeOrders: false, includeCustomerPhone: true));

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: false, includeCustomerPhone: false), existingXml);
        var columns = ColumnValues(XDocument.Parse(updated), TableIdentity("Customers"));

        Assert.DoesNotContain(columns, value => value.Contains("PhoneNumber", StringComparison.Ordinal));
        Assert.Contains(columns, value => value.Contains("Name : nvarchar(200)", StringComparison.Ordinal));
    }

    [Fact]
    public void Update_KeepsExistingTablePositionsAndRelationshipGeometry()
    {
        var existingDocument = XDocument.Parse(DrawioXmlGenerator.Generate(CreateModel(includeOrders: true)));
        SetGeometry(existingDocument, TableIdentity("Customers"), x: "111", y: "222", width: "333", height: "444");
        SetRelationshipPoints(existingDocument);

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: true, includeCustomerPhone: true), existingDocument.ToString(SaveOptions.DisableFormatting));
        var updatedDocument = XDocument.Parse(updated);
        var customerGeometry = Geometry(updatedDocument, TableIdentity("Customers"));
        var relationshipGeometry = updatedDocument.Descendants("mxCell").Single(cell => cell.Attribute("edge")?.Value == "1").Element("mxGeometry");

        Assert.Equal("111", customerGeometry.Attribute("x")?.Value);
        Assert.Equal("222", customerGeometry.Attribute("y")?.Value);
        Assert.Equal("333", customerGeometry.Attribute("width")?.Value);
        Assert.Equal("444", customerGeometry.Attribute("height")?.Value);
        Assert.Contains(relationshipGeometry!.Descendants("mxPoint"), point => point.Attribute("x")?.Value == "222" && point.Attribute("y")?.Value == "333");
    }

    [Fact]
    public void Update_PreservesManualNotesLayerAndChildren()
    {
        var existingDocument = XDocument.Parse(DrawioXmlGenerator.Generate(CreateModel(includeOrders: true)));
        var root = existingDocument.Descendants("root").Single();
        root.Add(new XElement("mxCell",
            new XAttribute("id", "manual-note-1"),
            new XAttribute("value", "Do not touch"),
            new XAttribute("style", "text;html=1;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", "d400-layer-manual-notes"),
            new XElement("mxGeometry",
                new XAttribute("x", "17"),
                new XAttribute("y", "19"),
                new XAttribute("width", "200"),
                new XAttribute("height", "80"),
                new XAttribute("as", "geometry"))));

        var updated = DrawioXmlGenerator.Update(CreateModel(includeOrders: false), existingDocument.ToString(SaveOptions.DisableFormatting));
        var updatedDocument = XDocument.Parse(updated);

        Assert.Contains(updatedDocument.Descendants("mxCell"), cell =>
            cell.Attribute("id")?.Value == "manual-note-1"
            && cell.Attribute("value")?.Value == "Do not touch"
            && cell.Attribute("parent")?.Value == "d400-layer-manual-notes");
        Assert.Contains(updatedDocument.Descendants("mxCell"), cell =>
            cell.Attribute("id")?.Value == "d400-layer-manual-notes"
            && cell.Attribute("value")?.Value == "Manual Notes");
    }

    [Fact]
    public void Update_MalformedXml_FailsSafely()
    {
        var exception = Assert.Throws<DrawioXmlUpdateException>(() =>
            DrawioXmlGenerator.Update(CreateModel(includeOrders: false), "<mxfile><diagram>"));

        Assert.Contains("not valid XML", exception.Message);
    }

    [Fact]
    public void Update_WritesBeforeAndAfterFixtureFiles()
    {
        var existingDocument = XDocument.Parse(DrawioXmlGenerator.Generate(CreateModel(includeOrders: true)));
        SetGeometry(existingDocument, TableIdentity("Customers"), x: "111", y: "222", width: "333", height: "444");
        existingDocument.Descendants("root").Single().Add(new XElement("mxCell",
            new XAttribute("id", "manual-note-fixture"),
            new XAttribute("value", "Manual fixture note"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", "d400-layer-manual-notes"),
            new XElement("mxGeometry", new XAttribute("x", "10"), new XAttribute("y", "20"), new XAttribute("width", "160"), new XAttribute("height", "60"), new XAttribute("as", "geometry"))));

        var before = existingDocument.ToString(SaveOptions.DisableFormatting);
        var after = DrawioXmlGenerator.Update(CreateModel(includeOrders: true, includeProducts: true, includeCustomerPhone: true), before);
        var artifactDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Artifacts"));
        var beforePath = Path.Combine(artifactDirectory, "Milestone6.Before.drawio");
        var afterPath = Path.Combine(artifactDirectory, "Milestone6.After.drawio");

        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(beforePath, before);
        File.WriteAllText(afterPath, after);

        Assert.True(File.Exists(beforePath));
        Assert.True(File.Exists(afterPath));
    }

    private static DatabaseModel CreateModel(bool includeOrders, bool includeProducts = false, bool includeCustomerPhone = false)
    {
        var customerId = ColumnIdentity("Customers", "Id");
        var customerColumns = new List<DatabaseColumn>
        {
            new("Shop", "shop", "Customers", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
            new("Shop", "shop", "Customers", "Name", new SqlDataType("nvarchar(200)"), ColumnNullability.Required, 1)
        };

        if (includeCustomerPhone)
        {
            customerColumns.Add(new DatabaseColumn("Shop", "shop", "Customers", "PhoneNumber", new SqlDataType("nvarchar(32)"), ColumnNullability.Nullable, 2));
        }

        var customers = new DatabaseTable(
            "Shop",
            "shop",
            "Customers",
            customerColumns,
            new PrimaryKeyDefinition("Shop", "shop", "Customers", "PK_Customers", [customerId]));

        var shopTables = new List<DatabaseTable> { customers };
        var salesTables = new List<DatabaseTable>();
        var relationships = new List<RelationshipDefinition>();

        if (includeOrders)
        {
            var orderId = ColumnIdentity("Orders", "Id", "sales");
            var orderCustomerId = ColumnIdentity("Orders", "CustomerId", "sales");
            var foreignKey = new ForeignKeyDefinition(
                "Shop",
                "sales",
                "Orders",
                "FK_Orders_Customers_CustomerId",
                TableIdentity("Orders", "sales"),
                customers.Identity,
                [new ForeignKeyColumnMapping(orderCustomerId, customerId)]);
            var orders = new DatabaseTable(
                "Shop",
                "sales",
                "Orders",
                [
                    new DatabaseColumn("Shop", "sales", "Orders", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                    new DatabaseColumn("Shop", "sales", "Orders", "CustomerId", new SqlDataType("int"), ColumnNullability.Required, 1)
                ],
                new PrimaryKeyDefinition("Shop", "sales", "Orders", "PK_Orders", [orderId]),
                [foreignKey]);

            salesTables.Add(orders);
            relationships.Add(new RelationshipDefinition(
                "FK_Orders_Customers_CustomerId",
                RelationshipKind.Physical,
                RelationshipCardinality.OneToMany,
                customers.Identity,
                orders.Identity,
                physicalType: PhysicalRelationshipType.ForeignKey));
        }

        if (includeProducts)
        {
            var productId = ColumnIdentity("Products", "Id");
            shopTables.Add(new DatabaseTable(
                "Shop",
                "shop",
                "Products",
                [
                    new DatabaseColumn("Shop", "shop", "Products", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                    new DatabaseColumn("Shop", "shop", "Products", "Sku", new SqlDataType("nvarchar(64)"), ColumnNullability.Required, 1)
                ],
                new PrimaryKeyDefinition("Shop", "shop", "Products", "PK_Products", [productId])));
        }

        var schemas = salesTables.Count == 0
            ? [new DatabaseSchema("Shop", "shop", shopTables)]
            : new[] { new DatabaseSchema("Shop", "sales", salesTables), new DatabaseSchema("Shop", "shop", shopTables) };

        return new DatabaseModel("Shop", schemas, relationships);
    }

    private static XElement TableCell(XDocument document, LogicalIdentity tableIdentity)
    {
        var tableId = DrawioXmlGenerator.ToMxCellId("table", tableIdentity);
        return document.Descendants("mxCell").Single(cell => cell.Attribute("id")?.Value == tableId);
    }

    private static XElement Geometry(XDocument document, LogicalIdentity tableIdentity)
    {
        return RequiredGeometry(TableCell(document, tableIdentity));
    }

    private static string[] ColumnValues(XDocument document, LogicalIdentity tableIdentity)
    {
        var tableId = DrawioXmlGenerator.ToMxCellId("table", tableIdentity);
        return document.Descendants("mxCell")
            .Where(cell => cell.Attribute("parent")?.Value == tableId)
            .Select(cell => cell.Attribute("value")?.Value ?? string.Empty)
            .ToArray();
    }

    private static XElement RequiredGeometry(XElement cell)
    {
        return cell.Element("mxGeometry") ?? throw new InvalidOperationException("Expected mxGeometry.");
    }

    private static void SetGeometry(XDocument document, LogicalIdentity tableIdentity, string x, string y, string width, string height)
    {
        var geometry = Geometry(document, tableIdentity);
        geometry.SetAttributeValue("x", x);
        geometry.SetAttributeValue("y", y);
        geometry.SetAttributeValue("width", width);
        geometry.SetAttributeValue("height", height);
    }

    private static void SetRelationshipPoints(XDocument document)
    {
        var geometry = document.Descendants("mxCell").Single(cell => cell.Attribute("edge")?.Value == "1").Element("mxGeometry")!;
        geometry.Add(new XElement("Array",
            new XAttribute("as", "points"),
            new XElement("mxPoint", new XAttribute("x", "222"), new XAttribute("y", "333"))));
    }

    private static LogicalIdentity TableIdentity(string tableName, string schemaName = "shop")
    {
        return LogicalIdentity.Create(SchemaObjectKind.Table, "Shop", schemaName, tableName);
    }

    private static LogicalIdentity ColumnIdentity(string tableName, string columnName, string schemaName = "shop")
    {
        return LogicalIdentity.Create(SchemaObjectKind.Column, "Shop", schemaName, tableName, columnName);
    }
}

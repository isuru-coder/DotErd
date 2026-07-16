using System.Xml.Linq;
using D400.DotErd.Core;
using D400.DotErd.Drawio;
using D400.DotErd.EfCore;
using D400.DotErd.Samples.SimpleShop;
using Microsoft.EntityFrameworkCore;

namespace D400.DotErd.Drawio.Tests;

public sealed class DrawioXmlGeneratorTests
{
    [Fact]
    public void Generate_CreatesParseableUncompressedDrawioDocumentWithLayers()
    {
        var xml = DrawioXmlGenerator.Generate(CreateModel());

        var document = XDocument.Parse(xml);
        var mxfile = Assert.IsType<XElement>(document.Root);
        var cells = document.Descendants("mxCell").ToArray();

        Assert.Equal("mxfile", mxfile.Name.LocalName);
        Assert.Equal("false", mxfile.Attribute("compressed")?.Value);
        Assert.Contains(cells, cell => cell.Attribute("value")?.Value == "Generated ERD" && cell.Attribute("parent")?.Value == "0");
        Assert.Contains(cells, cell => cell.Attribute("value")?.Value == "Manual Notes" && cell.Attribute("parent")?.Value == "0");
        Assert.Contains(cells, cell => cell.Attribute("vertex")?.Value == "1" && cell.Attribute("parent")?.Value == "d400-layer-generated-erd");
        Assert.Contains(cells, cell => cell.Attribute("edge")?.Value == "1" && cell.Attribute("parent")?.Value == "d400-layer-generated-erd");
        Assert.Contains(cells, cell => cell.Attribute("style")?.Value.Contains("shape=table", StringComparison.Ordinal) == true);
        Assert.Contains(cells, cell => cell.Attribute("edge")?.Value == "1" && cell.Attribute("style")?.Value.Contains("startArrow=ERone", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Generate_RendersTableColumnsIndicatorsAndSqlTypes()
    {
        var xml = DrawioXmlGenerator.Generate(CreateModel());
        var document = XDocument.Parse(xml);
        var labels = document.Descendants("mxCell")
            .Where(cell => cell.Attribute("vertex")?.Value == "1")
            .Select(cell => cell.Attribute("value")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(labels, label => label.Contains("shop.Customers", StringComparison.Ordinal));
        Assert.Contains(labels, label => label.Contains("PK REQ Id : int", StringComparison.Ordinal));
        Assert.Contains(labels, label => label.Contains("FK REQ CustomerId : int", StringComparison.Ordinal));
        Assert.Contains(labels, label => label.Contains("NULL Notes : nvarchar(400)", StringComparison.Ordinal));
        Assert.Contains(document.Descendants("mxCell"), cell =>
            cell.Attribute("value")?.Value == "PK REQ Id : int"
            && cell.Attribute("parent")?.Value == DrawioXmlGenerator.ToMxCellId("table", TableIdentity("Customers")));
    }

    [Fact]
    public void Generate_UsesDeterministicIdentifiersAndStableOutput()
    {
        var model = CreateModel();

        var first = DrawioXmlGenerator.Generate(model);
        var second = DrawioXmlGenerator.Generate(model);
        var expectedId = DrawioXmlGenerator.ToMxCellId("table", TableIdentity("Customers"));

        Assert.Equal(first, second);
        Assert.Contains($"id=\"{expectedId}\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_EscapesXmlSafely()
    {
        var model = new DatabaseModel(
            "EscapeDb",
            [
                new DatabaseSchema(
                    "EscapeDb",
                    "odd",
                    [
                        new DatabaseTable(
                            "EscapeDb",
                            "odd",
                            "A&B<Table>",
                            [
                                new DatabaseColumn("EscapeDb", "odd", "A&B<Table>", "Name<Unsafe>&", new SqlDataType("nvarchar(50)"), ColumnNullability.Nullable, 0)
                            ])
                    ])
            ]);

        var xml = DrawioXmlGenerator.Generate(model);
        var document = XDocument.Parse(xml);
        var values = document.Descendants("mxCell").Select(cell => cell.Attribute("value")?.Value ?? string.Empty).ToArray();

        Assert.Contains(values, value => value == "odd.A&B<Table>");
        Assert.Contains(values, value => value == "NULL Name<Unsafe>& : nvarchar(50)");
        Assert.Contains("odd.A&amp;B&lt;Table&gt;", xml);
        Assert.Contains("Name&lt;Unsafe&gt;&amp; : nvarchar(50)", xml);
    }

    [Fact]
    public void Generate_WritesSimpleShopArtifactForManualInspection()
    {
        using var context = CreateSimpleShopContext();
        var model = EfCoreRelationalModelExtractor.Extract(context, new EfCoreExtractionOptions("SimpleShop"));
        var xml = DrawioXmlGenerator.Generate(model, new DrawioGenerationOptions(PageName: "SimpleShop"));
        var artifactPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Artifacts", "SimpleShop.drawio"));

        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllText(artifactPath, xml);

        var document = XDocument.Parse(File.ReadAllText(artifactPath));
        Assert.Equal("mxfile", document.Root?.Name.LocalName);
        Assert.Contains(document.Descendants("mxCell"), cell => cell.Attribute("value")?.Value?.Contains("shop.Customers", StringComparison.Ordinal) == true);
    }

    private static DatabaseModel CreateModel()
    {
        var customerId = ColumnIdentity("Customers", "Id");
        var orderId = ColumnIdentity("Orders", "Id");
        var orderCustomerId = ColumnIdentity("Orders", "CustomerId", "sales");
        var customerTable = new DatabaseTable(
            "Shop",
            "shop",
            "Customers",
            [
                new DatabaseColumn("Shop", "shop", "Customers", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                new DatabaseColumn("Shop", "shop", "Customers", "Name", new SqlDataType("nvarchar(200)"), ColumnNullability.Required, 1)
            ],
            new PrimaryKeyDefinition("Shop", "shop", "Customers", "PK_Customers", [customerId]));

        var orderForeignKey = new ForeignKeyDefinition(
            "Shop",
            "sales",
            "Orders",
            "FK_Orders_Customers_CustomerId",
            TableIdentity("Orders", "sales"),
            TableIdentity("Customers"),
            [new ForeignKeyColumnMapping(orderCustomerId, customerId)],
            RelationshipCardinality.OneToMany);

        var orderTable = new DatabaseTable(
            "Shop",
            "sales",
            "Orders",
            [
                new DatabaseColumn("Shop", "sales", "Orders", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                new DatabaseColumn("Shop", "sales", "Orders", "CustomerId", new SqlDataType("int"), ColumnNullability.Required, 1),
                new DatabaseColumn("Shop", "sales", "Orders", "Notes", new SqlDataType("nvarchar(400)"), ColumnNullability.Nullable, 2)
            ],
            new PrimaryKeyDefinition("Shop", "sales", "Orders", "PK_Orders", [orderId]),
            [orderForeignKey]);

        var relationship = new RelationshipDefinition(
            "FK_Orders_Customers_CustomerId",
            RelationshipKind.Physical,
            RelationshipCardinality.OneToMany,
            customerTable.Identity,
            orderTable.Identity,
            physicalType: PhysicalRelationshipType.ForeignKey);

        return new DatabaseModel(
            "Shop",
            [
                new DatabaseSchema("Shop", "sales", [orderTable]),
                new DatabaseSchema("Shop", "shop", [customerTable])
            ],
            [relationship]);
    }

    private static SimpleShopDbContext CreateSimpleShopContext()
    {
        var options = new DbContextOptionsBuilder<SimpleShopDbContext>()
            .UseSqlServer("Server=not-used;Database=SimpleShop;User Id=ignored;Password=ignored;")
            .Options;

        return new SimpleShopDbContext(options);
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

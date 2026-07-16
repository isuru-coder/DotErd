using System.Text.Json;
using D400.DotErd.Core;
using D400.DotErd.Diff;

namespace D400.DotErd.Diff.Tests;

public sealed class SchemaDiffTests
{
    [Fact]
    public void Compare_DetectsRequiredSemanticChanges()
    {
        var result = SchemaDiff.Compare(CreateOldModel(), CreateNewModel());

        Assert.True(result.HasChanges);
        Assert.Contains(result.Changes, change => change.ChangeType == "Added" && change.ObjectType == "schema" && change.DisplayName == "archive");
        Assert.Contains(result.Changes, change => change.ChangeType == "Removed" && change.ObjectType == "schema" && change.DisplayName == "legacy");
        Assert.Contains(result.Changes, change => change.ChangeType == "Added" && change.ObjectType == "table" && change.DisplayName == "archive.AuditRows");
        Assert.Contains(result.Changes, change => change.ChangeType == "Removed" && change.ObjectType == "table" && change.DisplayName == "legacy.LegacyRows");
        Assert.Contains(result.Changes, change => change.ChangeType == "Added" && change.ObjectType == "column" && change.DisplayName == "shop.Customers.NewCode");
        Assert.Contains(result.Changes, change => change.ChangeType == "Removed" && change.ObjectType == "column" && change.DisplayName == "shop.Customers.LegacyCode");
        Assert.Contains(result.Changes, change => change.ChangeType == "Changed" && change.ObjectType == "column" && change.DisplayName == "shop.Customers.Id" && change.Detail!.Contains("SQL type", StringComparison.Ordinal));
        Assert.Contains(result.Changes, change => change.ChangeType == "Changed" && change.ObjectType == "column" && change.DisplayName == "shop.Customers.Name" && change.Detail!.Contains("nullability", StringComparison.Ordinal));
        Assert.Contains(result.Changes, change => change.ChangeType == "Changed" && change.ObjectType == "primary key" && change.DisplayName == "shop.Customers primary key");
        Assert.Contains(result.Changes, change => change.ChangeType == "Changed" && change.ObjectType == "foreign key" && change.DisplayName == "sales.Orders.FK_Orders_Customers_CustomerId");
        Assert.Contains(result.Changes, change => change.ChangeType == "Changed" && change.ObjectType == "index" && change.DisplayName == "sales.Orders.IX_Orders_CustomerId");
    }

    [Fact]
    public void CompareJson_IgnoresNonSemanticOrderingDifferences()
    {
        var first = CreateOldModel();
        var second = new DatabaseModel(
            first.Name,
            first.Schemas.Reverse(),
            first.Relationships.Reverse());

        var result = SchemaDiff.CompareJson(ToJson(first), ToJson(second));

        Assert.False(result.HasChanges);
        Assert.Equal("Schema is current. No semantic differences detected.", result.ToConsoleSummary());
    }

    [Fact]
    public void ToMarkdown_GeneratesReadableReport()
    {
        var result = SchemaDiff.Compare(CreateOldModel(), CreateNewModel());
        var markdown = result.ToMarkdown();

        Assert.StartsWith("# Schema Difference Report", markdown, StringComparison.Ordinal);
        Assert.Contains("## Column", markdown, StringComparison.Ordinal);
        Assert.Contains("**Changed** `shop.Customers.Id`", markdown, StringComparison.Ordinal);
    }

    private static string ToJson(DatabaseModel model)
    {
        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }

    private static DatabaseModel CreateOldModel()
    {
        var customerId = ColumnIdentity("Customers", "Id");
        var orderId = ColumnIdentity("Orders", "Id", "sales");
        var orderCustomerId = ColumnIdentity("Orders", "CustomerId", "sales");
        var customers = new DatabaseTable(
            "Shop",
            "shop",
            "Customers",
            [
                new DatabaseColumn("Shop", "shop", "Customers", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                new DatabaseColumn("Shop", "shop", "Customers", "Name", new SqlDataType("nvarchar(200)"), ColumnNullability.Required, 1),
                new DatabaseColumn("Shop", "shop", "Customers", "LegacyCode", new SqlDataType("nvarchar(40)"), ColumnNullability.Nullable, 2)
            ],
            new PrimaryKeyDefinition("Shop", "shop", "Customers", "PK_Customers", [customerId]),
            indexes: [new IndexDefinition("Shop", "shop", "Customers", "IX_Customers_Name", [new IndexColumn(ColumnIdentity("Customers", "Name"), IndexSortDirection.Ascending, 0)], isUnique: false)]);

        var ordersForeignKey = new ForeignKeyDefinition(
            "Shop",
            "sales",
            "Orders",
            "FK_Orders_Customers_CustomerId",
            TableIdentity("Orders", "sales"),
            TableIdentity("Customers"),
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
            [ordersForeignKey],
            [new IndexDefinition("Shop", "sales", "Orders", "IX_Orders_CustomerId", [new IndexColumn(orderCustomerId, IndexSortDirection.Ascending, 0)], isUnique: false)]);

        var legacy = new DatabaseTable(
            "Shop",
            "legacy",
            "LegacyRows",
            [new DatabaseColumn("Shop", "legacy", "LegacyRows", "Id", new SqlDataType("int"), ColumnNullability.Required, 0)]);

        return new DatabaseModel(
            "Shop",
            [
                new DatabaseSchema("Shop", "shop", [customers]),
                new DatabaseSchema("Shop", "sales", [orders]),
                new DatabaseSchema("Shop", "legacy", [legacy])
            ]);
    }

    private static DatabaseModel CreateNewModel()
    {
        var customerId = ColumnIdentity("Customers", "Id");
        var customerName = ColumnIdentity("Customers", "Name");
        var orderId = ColumnIdentity("Orders", "Id", "sales");
        var orderCustomerId = ColumnIdentity("Orders", "CustomerId", "sales");
        var customers = new DatabaseTable(
            "Shop",
            "shop",
            "Customers",
            [
                new DatabaseColumn("Shop", "shop", "Customers", "Id", new SqlDataType("bigint"), ColumnNullability.Required, 0),
                new DatabaseColumn("Shop", "shop", "Customers", "Name", new SqlDataType("nvarchar(200)"), ColumnNullability.Nullable, 1),
                new DatabaseColumn("Shop", "shop", "Customers", "NewCode", new SqlDataType("nvarchar(40)"), ColumnNullability.Nullable, 2)
            ],
            new PrimaryKeyDefinition("Shop", "shop", "Customers", "PK_Customers", [customerId, customerName]),
            indexes: [new IndexDefinition("Shop", "shop", "Customers", "IX_Customers_Name", [new IndexColumn(customerName, IndexSortDirection.Ascending, 0)], isUnique: false)]);

        var ordersForeignKey = new ForeignKeyDefinition(
            "Shop",
            "sales",
            "Orders",
            "FK_Orders_Customers_CustomerId",
            TableIdentity("Orders", "sales"),
            TableIdentity("Customers"),
            [new ForeignKeyColumnMapping(orderCustomerId, customerName)],
            RelationshipCardinality.OneToOne);
        var orders = new DatabaseTable(
            "Shop",
            "sales",
            "Orders",
            [
                new DatabaseColumn("Shop", "sales", "Orders", "Id", new SqlDataType("int"), ColumnNullability.Required, 0),
                new DatabaseColumn("Shop", "sales", "Orders", "CustomerId", new SqlDataType("int"), ColumnNullability.Required, 1)
            ],
            new PrimaryKeyDefinition("Shop", "sales", "Orders", "PK_Orders", [orderId]),
            [ordersForeignKey],
            [new IndexDefinition("Shop", "sales", "Orders", "IX_Orders_CustomerId", [new IndexColumn(orderCustomerId, IndexSortDirection.Ascending, 0)], isUnique: true)]);

        var audit = new DatabaseTable(
            "Shop",
            "archive",
            "AuditRows",
            [new DatabaseColumn("Shop", "archive", "AuditRows", "Id", new SqlDataType("int"), ColumnNullability.Required, 0)]);

        return new DatabaseModel(
            "Shop",
            [
                new DatabaseSchema("Shop", "archive", [audit]),
                new DatabaseSchema("Shop", "sales", [orders]),
                new DatabaseSchema("Shop", "shop", [customers])
            ]);
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

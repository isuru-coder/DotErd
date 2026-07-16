using D400.DotErd.Core;

namespace D400.DotErd.Core.Tests;

public sealed class SchemaModelTests
{
    [Fact]
    public void LogicalIdentity_IsDeterministicAndCaseInsensitive()
    {
        var first = LogicalIdentity.Create(SchemaObjectKind.Table, "ShopDb", "Sales", "Orders");
        var second = LogicalIdentity.Create(SchemaObjectKind.Table, " shopdb ", "sales", "orders");

        Assert.Equal(first, second);
        Assert.Equal("table:shopdb/sales/orders", first.Value);
    }

    [Fact]
    public void DatabaseTable_UsesStructuralEqualityForNestedLists()
    {
        var first = new DatabaseTable("ShopDb", "shop", "Products", [Column("Products", "Id", "int", 0)]);
        var second = new DatabaseTable("ShopDb", "shop", "Products", [Column("Products", "Id", "int", 0)]);

        Assert.Equal(first, second);
        Assert.NotSame(first.Columns, second.Columns);
    }

    [Fact]
    public void PrimaryKey_IdentifiesCompositeKeys()
    {
        var orderId = LogicalIdentity.Create(SchemaObjectKind.Column, "ShopDb", "sales", "OrderItems", "OrderId");
        var productId = LogicalIdentity.Create(SchemaObjectKind.Column, "ShopDb", "sales", "OrderItems", "ProductId");
        var primaryKey = new PrimaryKeyDefinition("ShopDb", "sales", "OrderItems", "PK_OrderItems", [orderId, productId]);

        Assert.True(primaryKey.IsComposite);
        Assert.Equal("primarykey:shopdb/sales/orderitems/pk_orderitems", primaryKey.Identity.Value);
    }

    [Fact]
    public void RelationshipDefinition_RepresentsPhysicalAndLogicalRelationshipTypes()
    {
        var source = LogicalIdentity.Create(SchemaObjectKind.Table, "ShopDb", "shop", "Products");
        var target = LogicalIdentity.Create(SchemaObjectKind.Table, "ShopDb", "shop", "Categories");

        var physical = new RelationshipDefinition(
            "ProductCategories",
            RelationshipKind.Physical,
            RelationshipCardinality.ManyToMany,
            source,
            target,
            physicalType: PhysicalRelationshipType.JoinTable);

        var logical = new RelationshipDefinition(
            "ProductCatalogOwner",
            RelationshipKind.Logical,
            RelationshipCardinality.OneToMany,
            source,
            target,
            logicalType: LogicalRelationshipType.CrossServiceReference);

        Assert.Equal(RelationshipCardinality.ManyToMany, physical.Cardinality);
        Assert.Equal(PhysicalRelationshipType.JoinTable, physical.PhysicalType);
        Assert.Equal(RelationshipKind.Logical, logical.Kind);
        Assert.Equal(LogicalRelationshipType.CrossServiceReference, logical.LogicalType);
    }

    private static DatabaseColumn Column(string tableName, string columnName, string storeType = "int", int ordinal = 0)
    {
        return new DatabaseColumn(
            "ShopDb",
            "shop",
            tableName,
            columnName,
            new SqlDataType(storeType),
            ColumnNullability.Required,
            ordinal);
    }
}


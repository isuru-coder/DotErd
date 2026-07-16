using D400.DotErd.Core;
using D400.DotErd.EfCore;
using D400.DotErd.Samples.SimpleShop;
using Microsoft.EntityFrameworkCore;

namespace D400.DotErd.EfCore.Tests;

public sealed class SimpleShopExtractionTests
{
    [Fact]
    public void Extract_ReadsConfiguredTablesSchemasColumnsAndStoreTypes()
    {
        using var context = CreateContext();

        var model = EfCoreRelationalModelExtractor.Extract(context, new EfCoreExtractionOptions("SimpleShop"));

        var salesOrders = RequiredTable(model, "sales", "Orders");
        var shopProducts = RequiredTable(model, "shop", "Products");
        var orderNumber = RequiredColumn(salesOrders, "OrderNumber");
        var productPrice = RequiredColumn(shopProducts, "Price");

        Assert.Equal("nvarchar(40)", orderNumber.DataType.StoreType);
        Assert.Equal(ColumnNullability.Required, orderNumber.Nullability);
        Assert.Equal("decimal(18,2)", productPrice.DataType.StoreType);
        Assert.DoesNotContain(AllTables(model), table => table.Name == "__EFMigrationsHistory");
    }

    [Fact]
    public void Extract_ReadsPrimaryKeysCompositeKeysForeignKeysAndIndexes()
    {
        using var context = CreateContext();

        var model = EfCoreRelationalModelExtractor.Extract(context, new EfCoreExtractionOptions("SimpleShop"));

        var orderItems = RequiredTable(model, "sales", "OrderItems");
        var customers = RequiredTable(model, "shop", "Customers");
        var orders = RequiredTable(model, "sales", "Orders");

        Assert.NotNull(orderItems.PrimaryKey);
        Assert.True(orderItems.PrimaryKey.IsComposite);
        Assert.Equal(["orderid", "productid"], orderItems.PrimaryKey.ColumnIdentities.Select(identity => identity.Value.Split('/').Last()).ToArray());
        Assert.Contains(orders.ForeignKeys, foreignKey => foreignKey.PrincipalTableIdentity == customers.Identity);
        Assert.Contains(customers.Indexes, index => index.Name == "IX_Customers_Email" && index.IsUnique);
    }

    [Fact]
    public void Extract_ClassifiesOneToOneOneToManyAndManyToManyRelationships()
    {
        using var context = CreateContext();

        var model = EfCoreRelationalModelExtractor.Extract(context, new EfCoreExtractionOptions("SimpleShop"));

        Assert.Contains(model.Relationships, relationship =>
            relationship.Cardinality == RelationshipCardinality.OneToOne
            && relationship.PhysicalType == PhysicalRelationshipType.ForeignKey
            && relationship.TargetIdentity == TableIdentity("CustomerProfiles"));

        Assert.Contains(model.Relationships, relationship =>
            relationship.Cardinality == RelationshipCardinality.OneToMany
            && relationship.PhysicalType == PhysicalRelationshipType.ForeignKey
            && relationship.TargetIdentity == LogicalIdentity.Create(SchemaObjectKind.Table, "SimpleShop", "sales", "Orders"));

        Assert.Contains(model.Relationships, relationship =>
            relationship.Cardinality == RelationshipCardinality.ManyToMany
            && relationship.PhysicalType == PhysicalRelationshipType.JoinTable
            && relationship.Name == "ProductCategories");

        Assert.DoesNotContain(model.Relationships, relationship =>
            relationship.Cardinality == RelationshipCardinality.ManyToMany
            && relationship.Name == "OrderItems");

        var joinTable = RequiredTable(model, "shop", "ProductCategories");
        Assert.True(joinTable.PrimaryKey?.IsComposite);
        Assert.Equal(2, joinTable.ForeignKeys.Count);
    }

    [Fact]
    public void ExtractFactory_ProducesActionableContextCreationErrorsWithoutConnectionStrings()
    {
        var exception = Assert.Throws<EfCoreRelationalModelExtractionException>(() =>
            EfCoreRelationalModelExtractor.Extract(
                () => throw new InvalidOperationException("Server=secret;Password=very-secret"),
                new EfCoreExtractionOptions("SimpleShop")));

        Assert.Contains("Unable to create the DbContext", exception.Message);
        Assert.DoesNotContain("very-secret", exception.Message);
    }

    [Fact]
    public void Extract_DoesNotRequireLiveDatabaseConnection()
    {
        using var context = CreateContext("Server=not-a-real-server;Database=SimpleShop;User Id=ignored;Password=ignored;");

        var model = EfCoreRelationalModelExtractor.Extract(context, new EfCoreExtractionOptions("SimpleShop"));

        Assert.Contains(AllTables(model), table => table.Name == "Customers");
    }

    private static SimpleShopDbContext CreateContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<SimpleShopDbContext>()
            .UseSqlServer(connectionString ?? "Server=(localdb)\\MSSQLLocalDB;Database=SimpleShop;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new SimpleShopDbContext(options);
    }

    private static DatabaseTable RequiredTable(DatabaseModel model, string schemaName, string tableName)
    {
        return AllTables(model).Single(table =>
            string.Equals(table.SchemaName, schemaName, StringComparison.Ordinal)
            && string.Equals(table.Name, tableName, StringComparison.Ordinal));
    }

    private static DatabaseColumn RequiredColumn(DatabaseTable table, string columnName)
    {
        return table.Columns.Single(column => string.Equals(column.Name, columnName, StringComparison.Ordinal));
    }

    private static IEnumerable<DatabaseTable> AllTables(DatabaseModel model)
    {
        return model.Schemas.SelectMany(schema => schema.Tables);
    }

    private static LogicalIdentity TableIdentity(string tableName)
    {
        return LogicalIdentity.Create(SchemaObjectKind.Table, "SimpleShop", "shop", tableName);
    }
}

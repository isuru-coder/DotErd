using D400.DotErd.Core;

namespace D400.DotErd.Core.Tests;

public sealed class SchemaValidationTests
{
    [Fact]
    public void Validate_ReturnsValidResultForConsistentModel()
    {
        var customer = Table("Customers", [Column("Customers", "Id")]);
        var order = Table(
            "Orders",
            [Column("Orders", "Id"), Column("Orders", "CustomerId")],
            foreignKeys:
            [
                new ForeignKeyDefinition(
                    "ShopDb",
                    "shop",
                    "Orders",
                    "FK_Orders_Customers",
                    TableIdentity("Orders"),
                    TableIdentity("Customers"),
                    [new ForeignKeyColumnMapping(ColumnIdentity("Orders", "CustomerId"), ColumnIdentity("Customers", "Id"))])
            ]);

        var model = Model(customer, order);

        var result = SchemaModelValidator.Validate(model);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ReportsDuplicateTables()
    {
        var model = Model(
            Table("Customers", [Column("Customers", "Id")]),
            Table("Customers", [Column("Customers", "Id")]));

        var result = SchemaModelValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SchemaValidationErrorCodes.DuplicateTable);
    }

    [Fact]
    public void Validate_ReportsDuplicateColumns()
    {
        var model = Model(Table("Products", [Column("Products", "Id"), Column("Products", "Id")]));

        var result = SchemaModelValidator.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SchemaValidationErrorCodes.DuplicateColumn);
    }

    [Fact]
    public void Validate_ReportsMissingForeignKeyTableReferences()
    {
        var order = Table(
            "Orders",
            [Column("Orders", "Id"), Column("Orders", "CustomerId")],
            foreignKeys:
            [
                new ForeignKeyDefinition(
                    "ShopDb",
                    "shop",
                    "Orders",
                    "FK_Orders_Customers",
                    TableIdentity("Orders"),
                    TableIdentity("MissingCustomers"),
                    [new ForeignKeyColumnMapping(ColumnIdentity("Orders", "CustomerId"), ColumnIdentity("Customers", "Id"))])
            ]);

        var result = SchemaModelValidator.Validate(Model(order));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SchemaValidationErrorCodes.InvalidForeignKeyReference);
    }

    [Fact]
    public void Validate_ReportsMissingForeignKeyColumnReferences()
    {
        var customer = Table("Customers", [Column("Customers", "Id")]);
        var order = Table(
            "Orders",
            [Column("Orders", "Id")],
            foreignKeys:
            [
                new ForeignKeyDefinition(
                    "ShopDb",
                    "shop",
                    "Orders",
                    "FK_Orders_Customers",
                    TableIdentity("Orders"),
                    TableIdentity("Customers"),
                    [new ForeignKeyColumnMapping(ColumnIdentity("Orders", "MissingCustomerId"), ColumnIdentity("Customers", "Id"))])
            ]);

        var result = SchemaModelValidator.Validate(Model(customer, order));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SchemaValidationErrorCodes.InvalidForeignKeyReference);
    }

    private static DatabaseModel Model(params DatabaseTable[] tables)
    {
        return new DatabaseModel("ShopDb", [new DatabaseSchema("ShopDb", "shop", tables)]);
    }

    private static DatabaseTable Table(
        string name,
        IEnumerable<DatabaseColumn> columns,
        IEnumerable<ForeignKeyDefinition>? foreignKeys = null)
    {
        return new DatabaseTable("ShopDb", "shop", name, columns, foreignKeys: foreignKeys);
    }

    private static DatabaseColumn Column(string tableName, string columnName)
    {
        return new DatabaseColumn(
            "ShopDb",
            "shop",
            tableName,
            columnName,
            new SqlDataType("int"),
            ColumnNullability.Required,
            0);
    }

    private static LogicalIdentity TableIdentity(string tableName)
    {
        return LogicalIdentity.Create(SchemaObjectKind.Table, "ShopDb", "shop", tableName);
    }

    private static LogicalIdentity ColumnIdentity(string tableName, string columnName)
    {
        return LogicalIdentity.Create(SchemaObjectKind.Column, "ShopDb", "shop", tableName, columnName);
    }
}


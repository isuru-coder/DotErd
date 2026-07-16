namespace D400.DotErd.Samples.SimpleShop;

public sealed class Product
{
    public int Id { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    public ICollection<OrderItem> OrderItems { get; } = new List<OrderItem>();

    public ICollection<Category> Categories { get; } = new List<Category>();
}


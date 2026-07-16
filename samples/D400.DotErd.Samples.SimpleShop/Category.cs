namespace D400.DotErd.Samples.SimpleShop;

public sealed class Category
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public ICollection<Product> Products { get; } = new List<Product>();
}


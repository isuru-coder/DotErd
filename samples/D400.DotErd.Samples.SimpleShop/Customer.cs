namespace D400.DotErd.Samples.SimpleShop;

public sealed class Customer
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    public CustomerProfile? Profile { get; set; }

    public ICollection<Order> Orders { get; } = new List<Order>();
}


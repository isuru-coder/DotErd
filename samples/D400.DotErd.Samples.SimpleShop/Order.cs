namespace D400.DotErd.Samples.SimpleShop;

public sealed class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public required string OrderNumber { get; set; }

    public DateTime PlacedAtUtc { get; set; }

    public Customer Customer { get; set; } = null!;

    public ICollection<OrderItem> Items { get; } = new List<OrderItem>();
}


namespace D400.DotErd.Samples.SimpleShop;

public sealed class CustomerProfile
{
    public int CustomerId { get; set; }

    public required string DisplayName { get; set; }

    public string? PhoneNumber { get; set; }

    public Customer Customer { get; set; } = null!;
}


using Microsoft.EntityFrameworkCore;

namespace D400.DotErd.Samples.SimpleShop;

public sealed class SimpleShopDbContext(DbContextOptions<SimpleShopDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shop");

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Id).ValueGeneratedOnAdd();
            entity.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
            entity.Property(customer => customer.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(customer => customer.Email).IsUnique();
            entity.HasMany(customer => customer.Orders)
                .WithOne(order => order.Customer)
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(customer => customer.Profile)
                .WithOne(profile => profile.Customer)
                .HasForeignKey<CustomerProfile>(profile => profile.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.ToTable("CustomerProfiles");
            entity.HasKey(profile => profile.CustomerId);
            entity.Property(profile => profile.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(profile => profile.PhoneNumber).HasMaxLength(32);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders", "sales");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(order => order.PlacedAtUtc).HasColumnType("datetime2").IsRequired();
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasMany(order => order.Items)
                .WithOne(item => item.Order)
                .HasForeignKey(item => item.OrderId);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", "sales");
            entity.HasKey(item => new { item.OrderId, item.ProductId });
            entity.Property(item => item.Quantity).IsRequired();
            entity.Property(item => item.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
            entity.HasOne(item => item.Product)
                .WithMany(product => product.OrderItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Sku).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Price).HasColumnType("decimal(18,2)").IsRequired();
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.HasMany(product => product.Categories)
                .WithMany(category => category.Products)
                .UsingEntity<Dictionary<string, object>>(
                    "ProductCategory",
                    right => right.HasOne<Category>()
                        .WithMany()
                        .HasForeignKey("CategoryId")
                        .OnDelete(DeleteBehavior.Cascade),
                    left => left.HasOne<Product>()
                        .WithMany()
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade),
                    join =>
                    {
                        join.ToTable("ProductCategories", "shop");
                        join.HasKey("ProductId", "CategoryId");
                        join.IndexerProperty<int>("ProductId").HasColumnName("ProductId");
                        join.IndexerProperty<int>("CategoryId").HasColumnName("CategoryId");
                    });
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(category => category.Name);
        });
    }
}

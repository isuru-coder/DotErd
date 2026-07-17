using D400.DotErd.Samples.SimpleShop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace D400.DotErd.Samples.SimpleShop.Api;

public sealed class SimpleShopDesignTimeFactory : IDesignTimeDbContextFactory<D400.DotErd.Samples.SimpleShop.SimpleShopDbContext>
{
    public D400.DotErd.Samples.SimpleShop.SimpleShopDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<D400.DotErd.Samples.SimpleShop.SimpleShopDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SimpleShop;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new D400.DotErd.Samples.SimpleShop.SimpleShopDbContext(options);
    }
}

public sealed class SimpleShopDbContext(DbContextOptions<SimpleShopDbContext> options) : DbContext(options);

public sealed class BrokenFactoryDbContext(DbContextOptions<BrokenFactoryDbContext> options) : DbContext(options);

public sealed class BrokenFactory : IDesignTimeDbContextFactory<BrokenFactoryDbContext>
{
    public BrokenFactoryDbContext CreateDbContext(string[] args)
    {
        throw new InvalidOperationException("Design-time fixture failure.");
    }
}

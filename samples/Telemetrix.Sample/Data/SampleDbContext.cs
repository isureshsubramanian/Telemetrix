using Microsoft.EntityFrameworkCore;

namespace Telemetrix.Sample.Data;

/// <summary>A product in the sample catalog.</summary>
public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

/// <summary>An order placed against the sample catalog.</summary>
public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public string Status { get; set; } = "Placed";
}

/// <summary>The sample EF Core context, backed by a local SQLite file.</summary>
public sealed class SampleDbContext : DbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>Seeds the sample database on first run.</summary>
public static class SampleData
{
    public static void Seed(SampleDbContext db)
    {
        if (db.Products.Any())
        {
            return;
        }

        db.Products.AddRange(
            new Product { Name = "USB-C Cable 2m", Category = "Cables", Price = 12.99m, Stock = 240 },
            new Product { Name = "USB-C Cable 1m", Category = "Cables", Price = 9.99m, Stock = 410 },
            new Product { Name = "HDMI Cable 3m", Category = "Cables", Price = 14.50m, Stock = 120 },
            new Product { Name = "Mechanical Keyboard", Category = "Peripherals", Price = 89.00m, Stock = 64 },
            new Product { Name = "Wireless Mouse", Category = "Peripherals", Price = 34.95m, Stock = 150 },
            new Product { Name = "27\" 4K Monitor", Category = "Displays", Price = 329.00m, Stock = 28 },
            new Product { Name = "Laptop Stand", Category = "Accessories", Price = 42.00m, Stock = 96 },
            new Product { Name = "Webcam 1080p", Category = "Peripherals", Price = 58.00m, Stock = 73 },
            new Product { Name = "USB Hub 7-port", Category = "Accessories", Price = 27.99m, Stock = 188 },
            new Product { Name = "Noise-cancelling Headset", Category = "Audio", Price = 119.00m, Stock = 41 },
            new Product { Name = "Desk Microphone", Category = "Audio", Price = 76.50m, Stock = 35 },
            new Product { Name = "Ethernet Cable 5m", Category = "Cables", Price = 8.25m, Stock = 305 });

        db.SaveChanges();
    }
}

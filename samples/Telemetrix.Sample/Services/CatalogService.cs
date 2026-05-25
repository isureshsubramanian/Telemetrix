using Microsoft.EntityFrameworkCore;
using Telemetrix.Sample.Data;

namespace Telemetrix.Sample.Services;

/// <summary>
/// A small domain service that sits between the HTTP endpoints and Entity Framework Core.
/// Each method opens a hand-rolled span and writes structured logs, so the dashboard shows a
/// realistic multi-layer waterfall: HTTP request → catalog span → EF Core SQL span.
/// </summary>
public sealed class CatalogService
{
    private readonly SampleDbContext _db;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(SampleDbContext db, ILogger<CatalogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Searches the catalog by name or category.</summary>
    public async Task<List<Product>> SearchAsync(string? term)
    {
        using var activity = SampleTelemetry.Activity.StartActivity("catalog.search");
        activity?.SetTag("catalog.term", term ?? "(all)");

        _logger.LogInformation("Searching catalog for {SearchTerm}", string.IsNullOrWhiteSpace(term) ? "all products" : term);

        var query = _db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(p => p.Name.Contains(term) || p.Category.Contains(term));
        }

        var results = await query.OrderBy(p => p.Name).ToListAsync();
        activity?.SetTag("catalog.result_count", results.Count);
        SampleTelemetry.CatalogSearches.Add(1);
        return results;
    }

    /// <summary>Looks up a single product by id.</summary>
    public async Task<Product?> FindAsync(int id)
    {
        using var activity = SampleTelemetry.Activity.StartActivity("catalog.find");
        activity?.SetTag("catalog.product_id", id);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
        {
            _logger.LogWarning("Product {ProductId} was not found", id);
        }

        return product;
    }

    /// <summary>Places an order for the given products, exercising several SQL round-trips.</summary>
    public async Task<Order> PlaceOrderAsync(string customer, IReadOnlyCollection<int> productIds)
    {
        using var activity = SampleTelemetry.Activity.StartActivity("catalog.place_order");
        activity?.SetTag("order.customer", customer);
        activity?.SetTag("order.requested_items", productIds.Count);

        _logger.LogInformation("Placing order for {Customer} with {ItemCount} item(s)", customer, productIds.Count);

        var ids = productIds.Distinct().ToArray();
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        if (products.Count == 0)
        {
            _logger.LogWarning("Order for {Customer} matched no known products", customer);
        }

        var order = new Order
        {
            Customer = customer,
            CreatedUtc = DateTime.UtcNow,
            ItemCount = products.Count,
            Total = products.Sum(p => p.Price),
            Status = products.Count > 0 ? "Placed" : "Rejected",
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("order.total", order.Total);
        SampleTelemetry.OrdersPlaced.Add(1, new KeyValuePair<string, object?>("status", order.Status));

        _logger.LogInformation("Order {OrderId} created for {Customer}, total {Total:C}", order.Id, customer, order.Total);
        return order;
    }
}

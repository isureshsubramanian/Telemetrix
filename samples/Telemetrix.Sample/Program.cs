using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Telemetrix;
using Telemetrix.Sample;
using Telemetrix.Sample.Data;
using Telemetrix.Sample.Services;
using Telemetrix.Sample.Workers;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Telemetrix — one line registers the in-process OpenTelemetry pipeline,
// the SQL parameter inspector and the embedded dashboard services.
// ---------------------------------------------------------------------------
builder.AddTelemetrix();

// Optional: richer Metrics tab. This composes with the pipeline Telemetrix set up.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddRuntimeInstrumentation());
}

builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Sample") ?? "Data Source=telemetrix-sample.db"));
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<TrafficGenerator>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Telemetrix — mount the dashboard at /telemetrix (Development only).
// ---------------------------------------------------------------------------
app.UseTelemetrixDashboard();

// Create and seed the local SQLite database on first run.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();
    SampleData.Seed(db);
}

app.MapGet("/", () => Results.Content(HomePage.Html, "text/html; charset=utf-8"));

app.MapGet("/api/products", async (string? search, CatalogService catalog) =>
    Results.Ok(await catalog.SearchAsync(search)));

app.MapGet("/api/products/{id:int}", async (int id, CatalogService catalog) =>
{
    var product = await catalog.FindAsync(id);
    return product is null
        ? Results.NotFound(new { message = $"Product {id} was not found." })
        : Results.Ok(product);
});

app.MapPost("/api/orders", async (OrderRequest request, CatalogService catalog) =>
{
    var order = await catalog.PlaceOrderAsync(request.Customer, request.ProductIds);
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/slow", async (ILogger<Program> logger) =>
{
    using var pipeline = SampleTelemetry.Activity.StartActivity("work.pipeline");
    logger.LogInformation("Running the slow multi-step pipeline");
    await RunStep("validate", 45);
    await RunStep("compute", 130);
    await RunStep("serialize", 35);
    return Results.Ok(new { status = "completed", steps = 3 });
});

app.MapGet("/api/chain", async (HttpContext context, IHttpClientFactory factory, ILogger<Program> logger) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
    using var activity = SampleTelemetry.Activity.StartActivity("chain.aggregate");
    logger.LogInformation("Fanning out to the catalog API");

    var client = factory.CreateClient();
    var products = await client.GetFromJsonAsync<List<Product>>($"{baseUrl}/api/products?search=cable");
    return Results.Ok(new { upstream = "/api/products?search=cable", received = products?.Count ?? 0 });
});

app.MapGet("/api/boom", (ILogger<Program> logger) =>
{
    // Demonstrates a failed trace. The exception is created, recorded on the current span
    // and logged — but NOT left unhandled. Throwing it would trip the debugger and flood
    // the console; recording it produces the same red, failed trace in Telemetrix cleanly.
    var failure = new InvalidOperationException("Simulated downstream failure while charging the customer.");
    Activity.Current?.AddException(failure);
    Activity.Current?.SetStatus(ActivityStatusCode.Error, failure.Message);
    logger.LogError(failure, "Simulated downstream failure while charging the customer");

    return Results.Problem(
        title: "Simulated failure",
        detail: failure.Message,
        statusCode: StatusCodes.Status500InternalServerError);
});

app.Run();

// Runs one named, instrumented step of the slow pipeline.
static async Task RunStep(string name, int budgetMs)
{
    using var step = SampleTelemetry.Activity.StartActivity($"work.{name}");
    step?.SetTag("work.budget_ms", budgetMs);
    await Task.Delay(budgetMs);
    SampleTelemetry.WorkDuration.Record(budgetMs, new KeyValuePair<string, object?>("step", name));
}

/// <summary>Request body for <c>POST /api/orders</c>.</summary>
internal sealed record OrderRequest(string Customer, int[] ProductIds);

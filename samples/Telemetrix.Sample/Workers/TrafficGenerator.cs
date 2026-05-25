using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Telemetrix.Sample.Workers;

/// <summary>
/// Generates a steady trickle of synthetic requests against the sample's own endpoints so the
/// Telemetrix dashboard has live traces, logs and metrics to show the moment it is opened.
/// Disable with <c>"Sample:GenerateTraffic": false</c> in configuration.
/// </summary>
public sealed class TrafficGenerator : BackgroundService
{
    private static readonly string[] GetEndpoints =
    [
        "/api/products",
        "/api/products?search=cable",
        "/api/products?search=audio",
        "/api/slow",
        "/api/chain",
        "/api/products/4",
        "/api/products/9999",
    ];

    private static readonly string[] Customers =
        ["Ava Bennett", "Liam Carter", "Noah Diaz", "Mia Evans", "Zoe Foster", "Owen Grant"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServer _server;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TrafficGenerator> _logger;

    public TrafficGenerator(
        IHttpClientFactory httpClientFactory,
        IServer server,
        IConfiguration configuration,
        ILogger<TrafficGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _server = server;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Sample:GenerateTraffic", true))
        {
            _logger.LogInformation("Synthetic traffic generation is disabled.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);

        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
        {
            _logger.LogWarning("Traffic generator could not resolve the server address; skipping synthetic traffic.");
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);

        var random = new Random();
        _logger.LogInformation("Synthetic traffic generator running against {BaseUrl}", baseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendOneAsync(client, random, stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(random.Next(1400, 3800)), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Synthetic request failed (ignored).");
            }
        }
    }

    private static async Task SendOneAsync(HttpClient client, Random random, CancellationToken token)
    {
        // Mostly successful traffic; the intentionally-failing /api/boom endpoint is hit
        // only occasionally so error traces still appear without flooding the console.
        var roll = random.Next(100);
        if (roll < 82)
        {
            using var response = await client.GetAsync(GetEndpoints[random.Next(GetEndpoints.Length)], token)
                .ConfigureAwait(false);
        }
        else if (roll < 96)
        {
            var order = new
            {
                customer = Customers[random.Next(Customers.Length)],
                productIds = Enumerable.Range(0, random.Next(1, 4))
                    .Select(_ => random.Next(1, 13))
                    .ToArray(),
            };
            using var response = await client.PostAsJsonAsync("/api/orders", order, token).ConfigureAwait(false);
        }
        else
        {
            using var response = await client.GetAsync("/api/boom", token).ConfigureAwait(false);
        }
    }

    private string? ResolveBaseUrl()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null || addresses.Count == 0)
        {
            return null;
        }

        var preferred = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.First();

        return preferred
            .Replace("[::]", "localhost", StringComparison.Ordinal)
            .Replace("0.0.0.0", "localhost", StringComparison.Ordinal)
            .Replace("+", "localhost", StringComparison.Ordinal);
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Telemetrix.Storage;

namespace Telemetrix.Dashboard;

/// <summary>
/// Terminal middleware that serves the embedded Telemetrix dashboard and its JSON API under a
/// configurable path prefix. Requests that fall outside the prefix are passed straight through,
/// so the middleware is safe to place anywhere in the pipeline.
/// </summary>
internal sealed class TelemetrixDashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TelemetrixDashboardOptions _options;
    private readonly TelemetrixStore _store;
    private readonly IHostEnvironment _environment;
    private readonly EmbeddedAssets _assets = new();
    private readonly string _version;

    public TelemetrixDashboardMiddleware(
        RequestDelegate next,
        TelemetrixDashboardOptions options,
        TelemetrixStore store,
        IHostEnvironment environment)
    {
        _next = next;
        _options = options;
        _store = store;
        _environment = environment;
        _version = typeof(TelemetrixDashboardMiddleware).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(
                _options.Path, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            await _next(context);
            return;
        }

        var route = (remaining.Value ?? string.Empty).Trim('/');

        if (route.Length == 0)
        {
            await ServeIndexAsync(context);
        }
        else if (route.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            await ServeAssetAsync(context, route["assets/".Length..]);
        }
        else if (route.Equals("api", StringComparison.OrdinalIgnoreCase)
                 || route.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await ServeApiAsync(context, route.Length > 4 ? route[4..] : string.Empty);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    private async Task ServeIndexAsync(HttpContext context)
    {
        var html = _assets.GetText("index.html");
        if (html is null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("Telemetrix dashboard asset 'index.html' is missing from the assembly.");
            return;
        }

        var config = JsonSerializer.Serialize(
            new
            {
                basePath = _options.Path,
                title = _options.Title,
                refreshMs = _options.RefreshIntervalMs,
                accent = _options.AccentColor,
                environment = _environment.EnvironmentName,
                version = _version,
            },
            TelemetrixJson.Options);

        html = html
            .Replace("{{BASE}}", _options.Path, StringComparison.Ordinal)
            .Replace("{{CONFIG}}", config, StringComparison.Ordinal);

        SetNoCache(context);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, Encoding.UTF8);
    }

    private async Task ServeAssetAsync(HttpContext context, string assetName)
    {
        if (assetName.Length == 0
            || assetName.Contains("..", StringComparison.Ordinal)
            || assetName.Contains('/', StringComparison.Ordinal)
            || assetName.Contains('\\', StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var bytes = _assets.GetBytes(assetName);
        if (bytes is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        SetNoCache(context);
        context.Response.ContentType = EmbeddedAssets.ContentType(assetName);
        await context.Response.Body.WriteAsync(bytes);
    }

    private async Task ServeApiAsync(HttpContext context, string apiRoute)
    {
        switch (apiRoute)
        {
            case "stats":
                await WriteJsonAsync(context, _store.GetStats(_environment.EnvironmentName));
                return;

            case "traces":
                await WriteJsonAsync(context, new
                {
                    traces = _store.GetTraces(Query(context, "q"), Query(context, "status"), IntQuery(context, "limit", 150)),
                });
                return;

            case "logs":
                await WriteJsonAsync(context, new
                {
                    logs = _store.GetLogs(Query(context, "level"), Query(context, "q"), Query(context, "traceId"), IntQuery(context, "limit", 300)),
                });
                return;

            case "metrics":
                await WriteJsonAsync(context, new { metrics = _store.GetMetrics() });
                return;

            case "clear":
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }

                _store.Clear();
                await WriteJsonAsync(context, new { ok = true });
                return;
        }

        if (apiRoute.StartsWith("traces/", StringComparison.OrdinalIgnoreCase))
        {
            var traceId = apiRoute["traces/".Length..];
            var detail = string.IsNullOrEmpty(traceId) ? null : _store.GetTrace(traceId);
            if (detail is null)
            {
                await WriteJsonAsync(context, new { error = "Trace not found." }, StatusCodes.Status404NotFound);
                return;
            }

            await WriteJsonAsync(context, detail);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static async Task WriteJsonAsync(HttpContext context, object? value, int statusCode = StatusCodes.Status200OK)
    {
        SetNoCache(context);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, TelemetrixJson.Options);
    }

    private static void SetNoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
    }

    private static string? Query(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int IntQuery(HttpContext context, string key, int fallback)
        => int.TryParse(context.Request.Query[key].ToString(), out var value) ? value : fallback;
}

# Telemetrix

**In-process OpenTelemetry trace, log and metric visualizer with an embedded executive dashboard.**

Telemetrix is a zero-dependency, local alternative to Jaeger, Zipkin and the Aspire
dashboard for ASP.NET Core development. It captures OpenTelemetry traces, logs and metrics
*inside your running application* and renders them in an embedded dashboard — no Docker, no
external collector, no orchestration.

```csharp
builder.AddTelemetrix();          // capture traces, logs, metrics + EF Core SQL
app.UseTelemetrixDashboard();     // dashboard at /telemetrix
```

Open `https://localhost:<port>/telemetrix` and the waterfalls, SQL and metrics are right there.


---

## Why

.NET 8, 9 and 10 lean heavily on native `Activity`, `DiagnosticSource` and OpenTelemetry,
so distributed tracing is everywhere. But *seeing* that telemetry locally usually means
spinning up Docker containers for Jaeger/Zipkin/Aspire, or squinting at log files.

Telemetrix removes that friction. It is a plain NuGet package: an in-process exporter that
funnels spans, logs and metrics straight into an embedded dashboard served by your own app.

## Features

- **Instant waterfall graphs** — trace trees that match request lifetimes across HTTP
  calls, internal layers and database round-trips.
- **SQL parameter inspector** — Entity Framework Core commands are captured automatically
  with their *bound parameter values*, cleanly formatted SQL, row counts and the exact
  application line of code that issued the query.
- **Logs, correlated** — every log entry is linked to its trace and span.
- **Metrics** — counters, gauges and histograms charted with [uPlot](https://github.com/leeoniya/uPlot).
- **Executive dashboard** — a refined light/dark UI with an overview, traces, logs and
  metrics tabs.
- **Zero dependency setup** — one call wires a complete OpenTelemetry pipeline; the
  dashboard is a single self-contained middleware. Nothing to deploy alongside your app.
- **Development-only** — Telemetrix is dormant outside the Development environment: no
  exporters, no capture, no dashboard, no measurable overhead.

## Requirements

- .NET 10 SDK (developed against `10.0.300`)
- An ASP.NET Core app using the minimal hosting model (`WebApplication.CreateBuilder`)


```bash
dotnet pack src/Telemetrix/Telemetrix.csproj -c Release
```

Then add a package or project reference to `Telemetrix`.

## Quick start

```csharp
using Telemetrix;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Telemetrix. This wires a full OpenTelemetry pipeline
//    (ASP.NET Core + HttpClient instrumentation, traces, metrics, logs)
//    and the Entity Framework Core SQL inspector.
builder.AddTelemetrix();

var app = builder.Build();

// 2. Mount the dashboard. Place it early in the pipeline.
app.UseTelemetrixDashboard();

app.MapGet("/", () => "Hello world");
app.Run();
```

Browse to `/telemetrix`.

## The dashboard

| Tab | What it shows |
| --- | --- |
| **Overview** | KPIs (throughput, error rate, p95 latency), throughput and latency charts, recent errors. |
| **Traces** | Searchable, filterable trace list. Open one for the **span waterfall**. |
| **Logs** | Filterable log stream; expand a row for attributes, exceptions and the trace link. |
| **Metrics** | A card and uPlot chart per counter / gauge / histogram series. |

Open any trace to see the waterfall. Click a span to expand its attributes, events,
correlated logs — and, for database spans, the **SQL parameter inspector**:

- the formatted SQL statement,
- a table of every bound parameter (name, value, type, direction),
- the file and line of application code that issued the command,
- duration and rows affected.

The dashboard supports light and dark themes and a live (auto-refresh) mode.

## How it works

`AddTelemetrix()` registers:

- a singleton **`TelemetrixStore`** — bounded, in-memory ring buffers for traces, logs and metrics;
- custom OpenTelemetry exporters (`BaseExporter<Activity>`, `BaseExporter<LogRecord>`,
  `BaseExporter<Metric>`) that snapshot telemetry into the store;
- a `DiagnosticListener` subscriber that watches the `Microsoft.EntityFrameworkCore`
  diagnostic source and turns each command into a synthesised SQL span. EF Core payloads
  are read by reflection, so Telemetrix never forces an Entity Framework Core dependency on
  apps that do not use it.

`UseTelemetrixDashboard()` adds a terminal middleware that serves the embedded dashboard
(HTML/CSS/JS compiled into the assembly) and a small JSON API. Requests outside the
dashboard path pass straight through.

Everything is gated on `IHostEnvironment.IsDevelopment()`. Outside Development the
registration calls are no-ops.

## Configuration

```csharp
builder.AddTelemetrix(options =>
{
    options.MaxTraces = 500;             // trace ring-buffer size
    options.MaxLogEntries = 2000;        // log ring-buffer size
    options.CaptureSqlParameters = true; // include bound SQL values
    options.CaptureCodeLocation = true;  // resolve the calling source line
    options.AddInstrumentation = true;   // set false if you configure OpenTelemetry yourself
});

app.UseTelemetrixDashboard(dashboard =>
{
    dashboard.Path = "/telemetrix";      // dashboard base path
    dashboard.RefreshIntervalMs = 2000;  // live-mode poll interval
    dashboard.AccentColor = "#6366f1";   // executive theme accent
});
```

### Already configure OpenTelemetry yourself?

Use the lower-level API so Telemetrix does not add duplicate instrumentation:

```csharp
builder.Services.AddTelemetrixCore();   // store + options + SQL capture only

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddTelemetrixExporter());       // plug Telemetrix into your own pipeline
```

## Sample application

`samples/Telemetrix.Sample` is a runnable ASP.NET Core + EF Core (SQLite) app. It exposes
endpoints that exercise HTTP hierarchies, nested custom spans, EF Core queries and errors,
and includes a background traffic generator so the dashboard has live data immediately.

```bash
dotnet run --project samples/Telemetrix.Sample
```

It opens `/telemetrix` automatically.

## Build from source

```bash
dotnet build Telemetrix.slnx
dotnet test  Telemetrix.slnx
```

## Notes

- **Package versions** in `Directory.Packages.props` are pinned to known-good OpenTelemetry
  releases. They are safe to bump to the latest on nuget.org.
- **uPlot** is loaded by the dashboard from a CDN. For fully offline use, drop
  `uplot.js` and `uplot.css` into `src/Telemetrix/wwwroot/` — they are embedded
  automatically and served locally in preference to the CDN.
- The dashboard is **unauthenticated** and intended for local Development only. Enabling
  `EnabledOutsideDevelopment` is strongly discouraged.

## Repository layout

```
src/Telemetrix            The library (NuGet package)
samples/Telemetrix.Sample Runnable ASP.NET Core + EF Core demo
tests/Telemetrix.Tests    xUnit unit tests
```

## License

MIT — see [LICENSE](LICENSE).

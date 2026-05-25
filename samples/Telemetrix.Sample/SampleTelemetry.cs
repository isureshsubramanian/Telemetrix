using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Telemetrix.Sample;

/// <summary>
/// Custom instrumentation for the sample application. Telemetrix captures every
/// <see cref="ActivitySource"/> and <see cref="Meter"/> by default, so simply creating
/// these is enough for the spans and metrics to show up on the dashboard.
/// </summary>
public static class SampleTelemetry
{
    /// <summary>The logical service name.</summary>
    public const string ServiceName = "Telemetrix.Sample";

    /// <summary>Activity source for hand-rolled internal spans.</summary>
    public static readonly ActivitySource Activity = new(ServiceName);

    /// <summary>Meter for the sample's custom metrics.</summary>
    public static readonly Meter Meter = new(ServiceName);

    /// <summary>Counts orders placed through the catalog service.</summary>
    public static readonly Counter<long> OrdersPlaced =
        Meter.CreateCounter<long>("sample.orders.placed", unit: "orders", description: "Orders placed by the sample app");

    /// <summary>Counts catalog searches.</summary>
    public static readonly Counter<long> CatalogSearches =
        Meter.CreateCounter<long>("sample.catalog.searches", unit: "searches", description: "Catalog searches performed");

    /// <summary>Records the duration of simulated work steps.</summary>
    public static readonly Histogram<double> WorkDuration =
        Meter.CreateHistogram<double>("sample.work.duration", unit: "ms", description: "Duration of simulated work steps");
}

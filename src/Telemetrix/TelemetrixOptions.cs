using Microsoft.Extensions.Hosting;

namespace Telemetrix;

/// <summary>
/// Configures how Telemetrix captures and retains telemetry. Tune via the
/// <c>AddTelemetrix</c> callback. All retention buffers are in-process and bounded.
/// </summary>
public sealed class TelemetrixOptions
{
    /// <summary>Maximum number of traces retained in the ring buffer. Default <c>500</c>.</summary>
    public int MaxTraces { get; set; } = 500;

    /// <summary>Maximum number of log entries retained in the ring buffer. Default <c>2000</c>.</summary>
    public int MaxLogEntries { get; set; } = 2000;

    /// <summary>Maximum number of points retained per metric series. Default <c>600</c>.</summary>
    public int MaxMetricPoints { get; set; } = 600;

    /// <summary>How often metrics are collected from the SDK, in milliseconds. Default <c>2000</c>.</summary>
    public int MetricExportIntervalMs { get; set; } = 2000;

    /// <summary>
    /// Capture EF Core / ADO.NET database commands via <see cref="System.Diagnostics.DiagnosticListener"/>.
    /// Default <see langword="true"/>.
    /// </summary>
    public bool CaptureSql { get; set; } = true;

    /// <summary>
    /// Include bound parameter values in the SQL inspector. Disable to record parameter
    /// names and types only. Default <see langword="true"/> (Telemetrix runs in Development).
    /// </summary>
    public bool CaptureSqlParameters { get; set; } = true;

    /// <summary>
    /// Walk the stack when a database command is issued to record the originating
    /// application source line. Requires debug symbols. Default <see langword="true"/>.
    /// </summary>
    public bool CaptureCodeLocation { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, <c>AddTelemetrix</c> registers ASP.NET Core and
    /// <c>HttpClient</c> instrumentation for you. Set to <see langword="false"/> if your
    /// application already configures its own OpenTelemetry instrumentation, to avoid
    /// duplicate spans. Telemetrix still captures every <see cref="System.Diagnostics.ActivitySource"/>
    /// regardless of this setting. Default <see langword="true"/>.
    /// </summary>
    public bool AddInstrumentation { get; set; } = true;

    /// <summary>
    /// Allow Telemetrix to run outside the Development environment. Strongly discouraged:
    /// the dashboard is unauthenticated and the store keeps raw telemetry in memory.
    /// Default <see langword="false"/>.
    /// </summary>
    public bool EnabledOutsideDevelopment { get; set; }

    /// <summary>
    /// Wildcard filter for which <see cref="System.Diagnostics.ActivitySource"/> instances
    /// are collected. Default <c>"*"</c> (everything). Supports <c>*</c> and <c>?</c>.
    /// </summary>
    public string ActivitySourceFilter { get; set; } = "*";

    /// <summary>
    /// Wildcard filter for which meters are collected. Default <c>"*"</c> (everything).
    /// Supports <c>*</c> and <c>?</c>.
    /// </summary>
    public string MeterFilter { get; set; } = "*";

    /// <summary>The dashboard options. Also configurable through <c>UseTelemetrixDashboard</c>.</summary>
    public TelemetrixDashboardOptions Dashboard { get; } = new();

    /// <summary>Returns <see langword="true"/> when Telemetrix should be active for the given environment.</summary>
    public bool IsEnabledFor(IHostEnvironment? environment)
        => EnabledOutsideDevelopment || (environment?.IsDevelopment() ?? false);
}

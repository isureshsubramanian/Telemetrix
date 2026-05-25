namespace Telemetrix.Models;

/// <summary>A compact, list-friendly projection of a trace.</summary>
/// <param name="TraceId">The 32-character hex trace id.</param>
/// <param name="RootName">The display name of the root span.</param>
/// <param name="RootKind">The kind of the root span.</param>
/// <param name="ServiceLabel">A best-effort service / source label.</param>
/// <param name="StartTimeUtc">When the trace started.</param>
/// <param name="DurationMs">Total wall-clock duration in milliseconds.</param>
/// <param name="SpanCount">Number of spans (including synthesised SQL spans).</param>
/// <param name="ErrorCount">Number of spans in an error state.</param>
/// <param name="SqlCount">Number of database command spans.</param>
/// <param name="Status">The aggregate trace status.</param>
/// <param name="HttpMethod">The HTTP method when the root is an HTTP request.</param>
/// <param name="HttpStatusCode">The HTTP response status code when known.</param>
public sealed record TraceSummary(
    string TraceId,
    string RootName,
    SpanKind RootKind,
    string? ServiceLabel,
    DateTime StartTimeUtc,
    double DurationMs,
    int SpanCount,
    int ErrorCount,
    int SqlCount,
    SpanStatus Status,
    string? HttpMethod,
    int? HttpStatusCode);

/// <summary>A fully expanded trace: every span plus the logs correlated to it.</summary>
/// <param name="TraceId">The 32-character hex trace id.</param>
/// <param name="RootName">The display name of the root span.</param>
/// <param name="StartTimeUtc">When the trace started.</param>
/// <param name="DurationMs">Total wall-clock duration in milliseconds.</param>
/// <param name="SpanCount">Number of spans.</param>
/// <param name="ErrorCount">Number of spans in an error state.</param>
/// <param name="SqlCount">Number of database command spans.</param>
/// <param name="Status">The aggregate trace status.</param>
/// <param name="Spans">Every span in the trace, ordered for waterfall rendering.</param>
/// <param name="Logs">Log entries whose trace id matches this trace.</param>
public sealed record TraceDetail(
    string TraceId,
    string RootName,
    DateTime StartTimeUtc,
    double DurationMs,
    int SpanCount,
    int ErrorCount,
    int SqlCount,
    SpanStatus Status,
    IReadOnlyList<SpanRecord> Spans,
    IReadOnlyList<LogEntry> Logs);

/// <summary>A single point on an overview time series.</summary>
/// <param name="TimestampUtc">The bucket timestamp.</param>
/// <param name="Value">The bucket value.</param>
public sealed record TimePoint(DateTime TimestampUtc, double Value);

/// <summary>Aggregate counters and time series powering the dashboard overview.</summary>
/// <param name="TraceCount">Number of retained traces.</param>
/// <param name="SpanCount">Total spans across retained traces.</param>
/// <param name="SqlCount">Total database command spans.</param>
/// <param name="ErrorCount">Number of traces ending in an error.</param>
/// <param name="ErrorRatePercent">Errored traces as a percentage of all traces.</param>
/// <param name="RequestsPerMinute">Throughput over the retained window.</param>
/// <param name="P50Ms">Median trace duration.</param>
/// <param name="P95Ms">95th percentile trace duration.</param>
/// <param name="P99Ms">99th percentile trace duration.</param>
/// <param name="MaxMs">Slowest retained trace.</param>
/// <param name="LogTotal">Number of retained log entries.</param>
/// <param name="LogLevelCounts">Retained log counts keyed by level name.</param>
/// <param name="MetricSeriesCount">Number of distinct metric series.</param>
/// <param name="Throughput">Per-minute trace counts.</param>
/// <param name="Latency">Per-minute p95 latency.</param>
/// <param name="RecentErrors">The most recent errored traces.</param>
/// <param name="GeneratedUtc">When the snapshot was produced.</param>
/// <param name="Environment">The host environment name.</param>
public sealed record StatsView(
    int TraceCount,
    int SpanCount,
    int SqlCount,
    int ErrorCount,
    double ErrorRatePercent,
    double RequestsPerMinute,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    int LogTotal,
    IReadOnlyDictionary<string, int> LogLevelCounts,
    int MetricSeriesCount,
    IReadOnlyList<TimePoint> Throughput,
    IReadOnlyList<TimePoint> Latency,
    IReadOnlyList<TraceSummary> RecentErrors,
    DateTime GeneratedUtc,
    string Environment);

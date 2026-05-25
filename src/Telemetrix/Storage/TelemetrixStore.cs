using System.Collections.Concurrent;
using System.Text;
using Telemetrix.Models;

namespace Telemetrix.Storage;

/// <summary>
/// The in-process telemetry store. Holds traces, logs and metrics in bounded ring buffers
/// and projects them into the read models consumed by the dashboard. Registered as a
/// singleton; every method is safe to call concurrently.
/// </summary>
public sealed class TelemetrixStore
{
    private const int OverviewWindowMinutes = 30;
    private const int MaxMetricSeries = 400;

    private readonly TelemetrixOptions _options;

    private readonly ConcurrentDictionary<string, TraceAggregate> _traces = new(StringComparer.Ordinal);
    private readonly object _traceOrderGate = new();
    private readonly Queue<string> _traceOrder = new();

    private readonly object _logGate = new();
    private readonly LinkedList<LogEntry> _logs = new();
    private long _logSequence;

    private readonly ConcurrentDictionary<string, MetricSeries> _metrics = new(StringComparer.Ordinal);

    /// <summary>Creates a store bound to the supplied options.</summary>
    public TelemetrixStore(TelemetrixOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The options this store was created with.</summary>
    public TelemetrixOptions Options => _options;

    // ---------------------------------------------------------------- ingestion

    /// <summary>Adds a span snapshot, creating its trace and evicting the oldest when full.</summary>
    public void AddSpan(SpanRecord span)
    {
        if (span is null || string.IsNullOrEmpty(span.TraceId))
        {
            return;
        }

        if (!_traces.TryGetValue(span.TraceId, out var aggregate))
        {
            var created = new TraceAggregate(span.TraceId);
            if (_traces.TryAdd(span.TraceId, created))
            {
                aggregate = created;
                string? evicted = null;
                lock (_traceOrderGate)
                {
                    _traceOrder.Enqueue(span.TraceId);
                    if (_traceOrder.Count > Math.Max(1, _options.MaxTraces))
                    {
                        evicted = _traceOrder.Dequeue();
                    }
                }

                if (evicted is not null)
                {
                    _traces.TryRemove(evicted, out _);
                }
            }
            else if (!_traces.TryGetValue(span.TraceId, out aggregate))
            {
                return;
            }
        }

        aggregate!.Add(span);
    }

    /// <summary>Adds a log entry, assigning it a sequence number and evicting the oldest when full.</summary>
    public void AddLog(LogEntry entry)
    {
        if (entry is null)
        {
            return;
        }

        lock (_logGate)
        {
            entry.Sequence = ++_logSequence;
            _logs.AddLast(entry);
            while (_logs.Count > Math.Max(1, _options.MaxLogEntries))
            {
                _logs.RemoveFirst();
            }
        }
    }

    /// <summary>Records a single metric measurement into the appropriate time series.</summary>
    public void RecordMetric(
        string name,
        string meterName,
        string? unit,
        string? description,
        string instrumentType,
        IReadOnlyList<TagItem> tags,
        DateTime timestampUtc,
        double value,
        long count,
        double sum)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var signature = BuildTagSignature(tags);
        var key = string.Concat(meterName, "\u001F", name, "\u001F", signature);

        if (!_metrics.TryGetValue(key, out var series))
        {
            if (_metrics.Count >= MaxMetricSeries)
            {
                return;
            }

            series = _metrics.GetOrAdd(key, _ => new MetricSeries(
                name, meterName, unit, description, instrumentType, signature, tags, _options.MaxMetricPoints));
        }

        series.Add(new MetricSample(timestampUtc, value, count, sum));
    }

    // ---------------------------------------------------------------- reads

    /// <summary>Returns trace summaries, newest first, after applying the supplied filters.</summary>
    public IReadOnlyList<TraceSummary> GetTraces(string? search, string? statusFilter, int limit)
    {
        var summaries = SummarizeAll();
        IEnumerable<TraceSummary> query = summaries;

        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = statusFilter.Equals("error", StringComparison.OrdinalIgnoreCase)
                ? query.Where(s => s.Status == SpanStatus.Error)
                : query.Where(s => s.Status != SpanStatus.Error);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                s.RootName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || s.TraceId.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (s.ServiceLabel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.Take(Math.Clamp(limit, 1, 1000)).ToArray();
    }

    /// <summary>Returns the fully expanded detail for a single trace, or <see langword="null"/> if unknown.</summary>
    public TraceDetail? GetTrace(string traceId)
    {
        if (string.IsNullOrEmpty(traceId) || !_traces.TryGetValue(traceId, out var aggregate))
        {
            return null;
        }

        var spans = aggregate.Snapshot();
        if (spans.Count == 0)
        {
            return null;
        }

        LogEntry[] logs;
        lock (_logGate)
        {
            logs = _logs.Where(l => l.TraceId == traceId).ToArray();
        }

        var summary = Summarize(traceId, spans);
        var ordered = spans.OrderBy(s => s.StartTimeUtc).ToArray();

        return new TraceDetail(
            traceId,
            summary.RootName,
            summary.StartTimeUtc,
            summary.DurationMs,
            summary.SpanCount,
            summary.ErrorCount,
            summary.SqlCount,
            summary.Status,
            ordered,
            logs);
    }

    /// <summary>Returns log entries, newest first, after applying the supplied filters.</summary>
    public IReadOnlyList<LogEntry> GetLogs(string? levelFilter, string? search, string? traceId, int limit)
    {
        LogEntry[] snapshot;
        lock (_logGate)
        {
            snapshot = _logs.ToArray();
        }

        IEnumerable<LogEntry> query = snapshot;

        if (!string.IsNullOrWhiteSpace(levelFilter) && !levelFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(levelFilter, out var minSeverity))
            {
                query = query.Where(l => l.Severity >= minSeverity);
            }
            else
            {
                query = query.Where(l => l.Level.Equals(levelFilter, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            query = query.Where(l => l.TraceId == traceId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l =>
                l.Message.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (l.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query
            .OrderByDescending(l => l.Sequence)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToArray();
    }

    /// <summary>Returns a snapshot of every metric series, sorted by meter then instrument name.</summary>
    public IReadOnlyList<MetricSeriesView> GetMetrics()
        => _metrics.Values
            .Select(s => s.Snapshot())
            .OrderBy(s => s.MeterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => string.Join(',', s.Tags.Select(t => t.Value)), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Computes the aggregate counters and time series for the overview tab.</summary>
    public StatsView GetStats(string environmentName)
    {
        var summaries = SummarizeAll();
        var now = DateTime.UtcNow;

        var durations = summaries.Select(s => s.DurationMs).OrderBy(x => x).ToArray();
        var traceCount = summaries.Count;
        var errorCount = summaries.Count(s => s.Status == SpanStatus.Error);
        var spanCount = summaries.Sum(s => s.SpanCount);
        var sqlCount = summaries.Sum(s => s.SqlCount);
        var rpm = summaries.Count(s => s.StartTimeUtc >= now.AddMinutes(-1));
        var errorRate = traceCount == 0 ? 0d : Math.Round(errorCount * 100d / traceCount, 1);

        LogEntry[] logsSnapshot;
        lock (_logGate)
        {
            logsSnapshot = _logs.ToArray();
        }

        var levelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trace"] = 0,
            ["Debug"] = 0,
            ["Information"] = 0,
            ["Warning"] = 0,
            ["Error"] = 0,
            ["Critical"] = 0,
        };
        foreach (var log in logsSnapshot)
        {
            levelCounts[log.Level] = levelCounts.GetValueOrDefault(log.Level) + 1;
        }

        var bucketStart = Floor(now, TimeSpan.FromMinutes(1)).AddMinutes(-(OverviewWindowMinutes - 1));
        var counts = new double[OverviewWindowMinutes];
        var latencyBuckets = new List<double>[OverviewWindowMinutes];
        for (var i = 0; i < OverviewWindowMinutes; i++)
        {
            latencyBuckets[i] = [];
        }

        foreach (var summary in summaries)
        {
            var index = (int)Math.Floor((summary.StartTimeUtc - bucketStart).TotalMinutes);
            if (index >= 0 && index < OverviewWindowMinutes)
            {
                counts[index]++;
                latencyBuckets[index].Add(summary.DurationMs);
            }
        }

        var throughput = new List<TimePoint>(OverviewWindowMinutes);
        var latency = new List<TimePoint>(OverviewWindowMinutes);
        for (var i = 0; i < OverviewWindowMinutes; i++)
        {
            var stamp = bucketStart.AddMinutes(i);
            throughput.Add(new TimePoint(stamp, counts[i]));
            var bucket = latencyBuckets[i];
            bucket.Sort();
            latency.Add(new TimePoint(stamp, bucket.Count == 0 ? 0 : Percentile(bucket, 0.95)));
        }

        var recentErrors = summaries
            .Where(s => s.Status == SpanStatus.Error)
            .Take(8)
            .ToArray();

        return new StatsView(
            traceCount,
            spanCount,
            sqlCount,
            errorCount,
            errorRate,
            rpm,
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            Percentile(durations, 0.99),
            durations.Length == 0 ? 0 : durations[^1],
            logsSnapshot.Length,
            levelCounts,
            _metrics.Count,
            throughput,
            latency,
            recentErrors,
            now,
            environmentName);
    }

    /// <summary>Drops every retained trace, log and metric.</summary>
    public void Clear()
    {
        _traces.Clear();
        lock (_traceOrderGate)
        {
            _traceOrder.Clear();
        }

        lock (_logGate)
        {
            _logs.Clear();
        }

        _metrics.Clear();
    }

    // ---------------------------------------------------------------- projection

    private List<TraceSummary> SummarizeAll()
    {
        var result = new List<TraceSummary>(_traces.Count);
        foreach (var aggregate in _traces.Values)
        {
            var spans = aggregate.Snapshot();
            if (spans.Count == 0)
            {
                continue;
            }

            result.Add(Summarize(aggregate.TraceId, spans));
        }

        result.Sort((a, b) => b.StartTimeUtc.CompareTo(a.StartTimeUtc));
        return result;
    }

    private static TraceSummary Summarize(string traceId, IReadOnlyList<SpanRecord> spans)
    {
        var root = FindRoot(spans);

        var start = DateTime.MaxValue;
        var end = DateTime.MinValue;
        var errorCount = 0;
        var sqlCount = 0;
        foreach (var span in spans)
        {
            if (span.StartTimeUtc < start)
            {
                start = span.StartTimeUtc;
            }

            if (span.EndTimeUtc > end)
            {
                end = span.EndTimeUtc;
            }

            if (span.Status == SpanStatus.Error)
            {
                errorCount++;
            }

            if (span.Source == SpanSource.Sql)
            {
                sqlCount++;
            }
        }

        var coverageMs = (end - start).TotalMilliseconds;
        var durationMs = Math.Max(root?.DurationMs ?? 0d, coverageMs);

        var serviceLabel = GetTag(root, "service.name") ?? root?.SourceName;
        var httpMethod = GetTag(root, "http.request.method", "http.method");
        int? httpStatus = null;
        var rawStatus = GetTag(root, "http.response.status_code", "http.status_code");
        if (rawStatus is not null && int.TryParse(rawStatus, out var parsed))
        {
            httpStatus = parsed;
        }

        return new TraceSummary(
            traceId,
            root?.Name ?? spans[0].Name,
            root?.Kind ?? SpanKind.Internal,
            serviceLabel,
            start,
            durationMs,
            spans.Count,
            errorCount,
            sqlCount,
            errorCount > 0 ? SpanStatus.Error : SpanStatus.Ok,
            httpMethod,
            httpStatus);
    }

    private static SpanRecord? FindRoot(IReadOnlyList<SpanRecord> spans)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            ids.Add(span.SpanId);
        }

        SpanRecord? root = null;
        foreach (var span in spans)
        {
            var isRoot = string.IsNullOrEmpty(span.ParentSpanId) || !ids.Contains(span.ParentSpanId);
            if (isRoot && (root is null || span.StartTimeUtc < root.StartTimeUtc))
            {
                root = span;
            }
        }

        if (root is not null)
        {
            return root;
        }

        // Defensive: a cycle or self-parenting span — fall back to the earliest span.
        foreach (var span in spans)
        {
            if (root is null || span.StartTimeUtc < root.StartTimeUtc)
            {
                root = span;
            }
        }

        return root;
    }

    private static string? GetTag(SpanRecord? span, params string[] keys)
    {
        if (span is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            foreach (var tag in span.Tags)
            {
                if (tag.Key == key && !string.IsNullOrEmpty(tag.Value))
                {
                    return tag.Value;
                }
            }
        }

        return null;
    }

    private static string BuildTagSignature(IReadOnlyList<TagItem> tags)
    {
        if (tags.Count == 0)
        {
            return string.Empty;
        }

        var ordered = tags.OrderBy(t => t.Key, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var tag in ordered)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(tag.Key).Append('=').Append(tag.Value);
        }

        return sb.ToString();
    }

    private static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0)
        {
            return 0;
        }

        if (sortedAscending.Count == 1)
        {
            return Math.Round(sortedAscending[0], 2);
        }

        var rank = percentile * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        var weight = rank - lower;
        var value = (sortedAscending[lower] * (1 - weight)) + (sortedAscending[upper] * weight);
        return Math.Round(value, 2);
    }

    private static DateTime Floor(DateTime value, TimeSpan interval)
        => new(value.Ticks - (value.Ticks % interval.Ticks), value.Kind);
}

using Telemetrix.Models;

namespace Telemetrix.Storage;

/// <summary>
/// Accumulates the spans that belong to a single trace. Spans arrive independently and
/// out of order (each as it ends), so the aggregate simply collects them; projection into
/// a tree happens at read time.
/// </summary>
internal sealed class TraceAggregate
{
    private readonly object _gate = new();
    private readonly List<SpanRecord> _spans = [];

    public TraceAggregate(string traceId) => TraceId = traceId;

    /// <summary>The trace id shared by every span in this aggregate.</summary>
    public string TraceId { get; }

    /// <summary>Adds a span snapshot to the aggregate.</summary>
    public void Add(SpanRecord span)
    {
        lock (_gate)
        {
            _spans.Add(span);
        }
    }

    /// <summary>Returns an immutable copy of the spans collected so far.</summary>
    public IReadOnlyList<SpanRecord> Snapshot()
    {
        lock (_gate)
        {
            return _spans.ToArray();
        }
    }

    /// <summary>The number of spans collected so far.</summary>
    public int SpanCount
    {
        get
        {
            lock (_gate)
            {
                return _spans.Count;
            }
        }
    }
}

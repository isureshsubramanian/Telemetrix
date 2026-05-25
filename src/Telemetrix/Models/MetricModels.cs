namespace Telemetrix.Models;

/// <summary>A single point in a metric time series.</summary>
/// <param name="TimestampUtc">When the measurement was collected.</param>
/// <param name="Value">The primary value (sum for counters, last value for gauges, sum for histograms).</param>
/// <param name="Count">The histogram sample count, or <c>0</c> for non-histogram instruments.</param>
/// <param name="Sum">The histogram sum, or equal to <paramref name="Value"/> for non-histogram instruments.</param>
public sealed record MetricSample(DateTime TimestampUtc, double Value, long Count, double Sum);

/// <summary>An immutable view of a metric series and its recent points, returned to the dashboard.</summary>
/// <param name="Name">The instrument name.</param>
/// <param name="MeterName">The owning meter name.</param>
/// <param name="Unit">The unit of measure, when declared.</param>
/// <param name="Description">The instrument description, when declared.</param>
/// <param name="InstrumentType">A friendly instrument type (<c>Counter</c>, <c>Gauge</c>, <c>Histogram</c>, …).</param>
/// <param name="Tags">The tag set that distinguishes this series.</param>
/// <param name="Points">The retained time-series points, oldest first.</param>
public sealed record MetricSeriesView(
    string Name,
    string MeterName,
    string? Unit,
    string? Description,
    string InstrumentType,
    IReadOnlyList<TagItem> Tags,
    IReadOnlyList<MetricSample> Points);

/// <summary>
/// A mutable, ring-buffered metric time series owned by the store. One instance exists per
/// unique instrument + tag-set combination.
/// </summary>
public sealed class MetricSeries
{
    private readonly object _gate = new();
    private readonly Queue<MetricSample> _points = new();
    private readonly int _maxPoints;

    /// <summary>Creates a new series.</summary>
    public MetricSeries(
        string name,
        string meterName,
        string? unit,
        string? description,
        string instrumentType,
        string tagSignature,
        IReadOnlyList<TagItem> tags,
        int maxPoints)
    {
        Name = name;
        MeterName = meterName;
        Unit = unit;
        Description = description;
        InstrumentType = instrumentType;
        TagSignature = tagSignature;
        Tags = tags;
        _maxPoints = Math.Max(2, maxPoints);
    }

    /// <summary>The instrument name.</summary>
    public string Name { get; }

    /// <summary>The owning meter name.</summary>
    public string MeterName { get; }

    /// <summary>The unit of measure.</summary>
    public string? Unit { get; }

    /// <summary>The instrument description.</summary>
    public string? Description { get; }

    /// <summary>A friendly instrument type.</summary>
    public string InstrumentType { get; }

    /// <summary>A stable signature of the tag set, used as the dictionary key.</summary>
    public string TagSignature { get; }

    /// <summary>The tag set that distinguishes this series.</summary>
    public IReadOnlyList<TagItem> Tags { get; }

    /// <summary>Appends a point, evicting the oldest when the buffer is full.</summary>
    public void Add(MetricSample sample)
    {
        lock (_gate)
        {
            _points.Enqueue(sample);
            while (_points.Count > _maxPoints)
            {
                _points.Dequeue();
            }
        }
    }

    /// <summary>Takes an immutable snapshot of the series for serialization.</summary>
    public MetricSeriesView Snapshot()
    {
        lock (_gate)
        {
            return new MetricSeriesView(
                Name,
                MeterName,
                Unit,
                Description,
                InstrumentType,
                Tags,
                _points.ToArray());
        }
    }
}

using OpenTelemetry;
using OpenTelemetry.Metrics;
using Telemetrix.Internal;
using Telemetrix.Models;
using Telemetrix.Storage;

namespace Telemetrix.Exporters;

/// <summary>
/// An OpenTelemetry metric exporter that flattens each <see cref="Metric"/> and its points
/// into time series held by the in-process <see cref="TelemetrixStore"/>. Driven by a
/// <see cref="PeriodicExportingMetricReader"/>.
/// </summary>
internal sealed class TelemetrixMetricExporter : BaseExporter<Metric>
{
    private readonly TelemetrixStore _store;

    public TelemetrixMetricExporter(TelemetrixStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override ExportResult Export(in Batch<Metric> batch)
    {
        var fallbackTimestamp = DateTime.UtcNow;

        foreach (var metric in batch)
        {
            try
            {
                var instrumentType = FriendlyType(metric.MetricType);

                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var tags = new List<TagItem>();
                    foreach (var tag in point.Tags)
                    {
                        tags.Add(new TagItem(tag.Key, ValueFormatter.Format(tag.Value)));
                    }

                    var (value, count, sum) = ReadPoint(metric.MetricType, in point);

                    var endTime = point.EndTime;
                    var timestamp = endTime == default ? fallbackTimestamp : endTime.UtcDateTime;

                    _store.RecordMetric(
                        metric.Name,
                        metric.MeterName,
                        string.IsNullOrEmpty(metric.Unit) ? null : metric.Unit,
                        string.IsNullOrEmpty(metric.Description) ? null : metric.Description,
                        instrumentType,
                        tags,
                        timestamp,
                        value,
                        count,
                        sum);
                }
            }
            catch
            {
                // Never let telemetry capture break the host application.
            }
        }

        return ExportResult.Success;
    }

    private static (double Value, long Count, double Sum) ReadPoint(MetricType type, in MetricPoint point)
    {
        switch (type)
        {
            case MetricType.LongSum:
            case MetricType.LongSumNonMonotonic:
                var longSum = point.GetSumLong();
                return (longSum, 0, longSum);

            case MetricType.DoubleSum:
            case MetricType.DoubleSumNonMonotonic:
                var doubleSum = point.GetSumDouble();
                return (doubleSum, 0, doubleSum);

            case MetricType.LongGauge:
                var longGauge = point.GetGaugeLastValueLong();
                return (longGauge, 0, longGauge);

            case MetricType.DoubleGauge:
                var doubleGauge = point.GetGaugeLastValueDouble();
                return (doubleGauge, 0, doubleGauge);

            case MetricType.Histogram:
            case MetricType.ExponentialHistogram:
                var histogramCount = point.GetHistogramCount();
                var histogramSum = point.GetHistogramSum();
                var average = histogramCount > 0 ? histogramSum / histogramCount : 0d;
                return (average, histogramCount, histogramSum);

            default:
                return (0, 0, 0);
        }
    }

    private static string FriendlyType(MetricType type) => type switch
    {
        MetricType.LongSum or MetricType.DoubleSum => "Counter",
        MetricType.LongSumNonMonotonic or MetricType.DoubleSumNonMonotonic => "UpDownCounter",
        MetricType.LongGauge or MetricType.DoubleGauge => "Gauge",
        MetricType.Histogram => "Histogram",
        MetricType.ExponentialHistogram => "ExponentialHistogram",
        _ => "Unknown",
    };
}

using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Telemetrix.Internal;
using Telemetrix.Models;
using Telemetrix.Storage;

namespace Telemetrix.Exporters;

/// <summary>
/// An OpenTelemetry log exporter that snapshots each <see cref="LogRecord"/> into the
/// in-process <see cref="TelemetrixStore"/>, preserving trace correlation.
/// </summary>
internal sealed class TelemetrixLogExporter : BaseExporter<LogRecord>
{
    private readonly TelemetrixStore _store;

    public TelemetrixLogExporter(TelemetrixStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var record in batch)
        {
            try
            {
                _store.AddLog(Convert(record));
            }
            catch
            {
                // Never let telemetry capture break logging.
            }
        }

        return ExportResult.Success;
    }

    private static LogEntry Convert(LogRecord record)
    {
        var (level, severity) = MapLevel(record.LogLevel);

        var attributes = new List<TagItem>();
        if (record.Attributes is not null)
        {
            foreach (var attribute in record.Attributes)
            {
                if (attribute.Key == "{OriginalFormat}")
                {
                    continue;
                }

                attributes.Add(new TagItem(attribute.Key, ValueFormatter.Format(attribute.Value)));
            }
        }

        var message = record.FormattedMessage;

        ExceptionInfo? exception = record.Exception is { } ex
            ? new ExceptionInfo(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace)
            : null;

        return new LogEntry
        {
            TimestampUtc = record.Timestamp == default ? DateTime.UtcNow : record.Timestamp.ToUniversalTime(),
            Level = level,
            Severity = severity,
            Category = record.CategoryName,
            Message = string.IsNullOrEmpty(message) ? "(no message)" : message,
            EventId = record.EventId.Id,
            EventName = record.EventId.Name,
            TraceId = record.TraceId == default ? null : record.TraceId.ToHexString(),
            SpanId = record.SpanId == default ? null : record.SpanId.ToHexString(),
            Exception = exception,
            Attributes = attributes,
        };
    }

    private static (string Name, int Severity) MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => ("Trace", 0),
        LogLevel.Debug => ("Debug", 1),
        LogLevel.Information => ("Information", 2),
        LogLevel.Warning => ("Warning", 3),
        LogLevel.Error => ("Error", 4),
        LogLevel.Critical => ("Critical", 5),
        _ => ("None", 6),
    };
}

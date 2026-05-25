using System.Diagnostics;
using OpenTelemetry;
using Telemetrix.Models;
using Telemetrix.Storage;

namespace Telemetrix.Exporters;

/// <summary>
/// An OpenTelemetry span exporter that snapshots each completed <see cref="Activity"/>
/// into the in-process <see cref="TelemetrixStore"/>.
/// </summary>
internal sealed class TelemetrixActivityExporter : BaseExporter<Activity>
{
    private readonly TelemetrixStore _store;

    public TelemetrixActivityExporter(TelemetrixStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            try
            {
                // Database commands are captured in far richer detail (parameters, code
                // location) by the diagnostic-source pipeline, so drop any spans an EF Core
                // instrumentation would also emit to avoid showing each query twice.
                if (activity.Source.Name.Contains("EntityFrameworkCore", StringComparison.Ordinal))
                {
                    continue;
                }

                _store.AddSpan(SpanRecord.FromActivity(activity));
            }
            catch
            {
                // Telemetry capture must never destabilise the host application.
            }
        }

        return ExportResult.Success;
    }
}

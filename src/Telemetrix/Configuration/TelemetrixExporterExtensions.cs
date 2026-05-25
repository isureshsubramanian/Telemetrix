using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Telemetrix.Exporters;
using Telemetrix.Storage;

namespace Telemetrix;

/// <summary>
/// Advanced composition hooks for applications that build their own OpenTelemetry pipeline.
/// Most applications should use <see cref="TelemetrixHostingExtensions.AddTelemetrix"/> instead.
/// </summary>
public static class TelemetrixExporterExtensions
{
    /// <summary>
    /// Adds the Telemetrix span exporter to an existing <see cref="TracerProviderBuilder"/>.
    /// Requires <see cref="TelemetrixHostingExtensions.AddTelemetrixCore"/> (or
    /// <see cref="TelemetrixHostingExtensions.AddTelemetrix"/>) to have registered the store.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddTelemetrixExporter(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProcessor(static serviceProvider =>
        {
            var store = serviceProvider.GetService<TelemetrixStore>()
                ?? throw new InvalidOperationException(
                    "Telemetrix is not registered. Call services.AddTelemetrixCore() (or " +
                    "builder.AddTelemetrix()) before TracerProviderBuilder.AddTelemetrixExporter().");

            return new SimpleActivityExportProcessor(new TelemetrixActivityExporter(store));
        });
    }
}

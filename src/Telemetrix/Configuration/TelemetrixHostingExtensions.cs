using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Telemetrix.Diagnostics;
using Telemetrix.Exporters;
using Telemetrix.Storage;

namespace Telemetrix;

/// <summary>Registration entry points for Telemetrix.</summary>
public static class TelemetrixHostingExtensions
{
    /// <summary>
    /// Registers Telemetrix and wires a complete, zero-configuration OpenTelemetry pipeline
    /// (tracing, metrics and logging) that feeds the in-process dashboard. ASP.NET Core and
    /// <c>HttpClient</c> instrumentation is added automatically, and Entity Framework Core
    /// commands are captured with full parameter detail.
    /// <para>
    /// Telemetrix only activates in the Development environment unless
    /// <see cref="TelemetrixOptions.EnabledOutsideDevelopment"/> is set. Pair this call with
    /// <c>app.UseTelemetrixDashboard()</c>.
    /// </para>
    /// </summary>
    /// <param name="builder">The application host builder (for example a <c>WebApplicationBuilder</c>).</param>
    /// <param name="configure">An optional callback to tune <see cref="TelemetrixOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddTelemetrix(
        this IHostApplicationBuilder builder,
        Action<TelemetrixOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(static d => d.ServiceType == typeof(TelemetrixStore)))
        {
            // Already registered — keep the first configuration and avoid duplicate exporters.
            return builder;
        }

        var options = new TelemetrixOptions();
        configure?.Invoke(options);

        if (!options.IsEnabledFor(builder.Environment))
        {
            // Outside Development Telemetrix stays completely dormant: no exporters, no
            // capture, no dashboard, no measurable overhead.
            return builder;
        }

        var store = RegisterCore(builder.Services, options);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(options.ActivitySourceFilter);

                if (options.AddInstrumentation)
                {
                    tracing.AddAspNetCoreInstrumentation();
                    tracing.AddHttpClientInstrumentation();
                }

                tracing.AddProcessor(new SimpleActivityExportProcessor(new TelemetrixActivityExporter(store)));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(options.MeterFilter);

                if (options.AddInstrumentation)
                {
                    metrics.AddAspNetCoreInstrumentation();
                    metrics.AddHttpClientInstrumentation();
                }

                metrics.AddReader(new PeriodicExportingMetricReader(
                    new TelemetrixMetricExporter(store),
                    options.MetricExportIntervalMs));
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.AddProcessor(new SimpleLogRecordExportProcessor(new TelemetrixLogExporter(store)));
        });

        return builder;
    }

    /// <summary>
    /// Registers only the Telemetrix store, options and SQL capture — without touching the
    /// OpenTelemetry pipeline. Use this when your application configures OpenTelemetry itself;
    /// add <c>AddTelemetrixExporter()</c> to your own <c>TracerProviderBuilder</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to tune <see cref="TelemetrixOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddTelemetrixCore(
        this IServiceCollection services,
        Action<TelemetrixOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new TelemetrixOptions();
        configure?.Invoke(options);
        RegisterCore(services, options);
        return services;
    }

    private static TelemetrixStore RegisterCore(IServiceCollection services, TelemetrixOptions options)
    {
        var existing = services
            .FirstOrDefault(d => d.ServiceType == typeof(TelemetrixStore))?
            .ImplementationInstance as TelemetrixStore;
        if (existing is not null)
        {
            return existing;
        }

        var store = new TelemetrixStore(options);
        services.AddSingleton(options);
        services.AddSingleton(store);
        services.AddHostedService<TelemetrixDiagnosticSubscriber>();
        return store;
    }
}

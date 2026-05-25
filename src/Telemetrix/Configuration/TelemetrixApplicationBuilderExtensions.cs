using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telemetrix.Dashboard;

namespace Telemetrix;

/// <summary>Pipeline entry point for the embedded Telemetrix dashboard.</summary>
public static class TelemetrixApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts the embedded Telemetrix dashboard and its JSON API. The dashboard is served from
    /// <see cref="TelemetrixDashboardOptions.Path"/> (default <c>/telemetrix</c>).
    /// <para>
    /// This call is a safe no-op when <c>AddTelemetrix</c> was not called or Telemetrix is
    /// disabled for the current environment, so it can be left in the pipeline unconditionally.
    /// Place it early in the pipeline so the dashboard is reachable even when authentication is
    /// configured.
    /// </para>
    /// </summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <param name="configure">An optional callback to tune <see cref="TelemetrixDashboardOptions"/>.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseTelemetrixDashboard(
        this IApplicationBuilder app,
        Action<TelemetrixDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<TelemetrixOptions>();
        var environment = app.ApplicationServices.GetService<IHostEnvironment>();

        if (options is null || !options.IsEnabledFor(environment))
        {
            // Telemetrix is not registered or is disabled outside Development — do not mount.
            return app;
        }

        var dashboardOptions = options.Dashboard;
        configure?.Invoke(dashboardOptions);

        app.UseMiddleware<TelemetrixDashboardMiddleware>(dashboardOptions);
        return app;
    }
}

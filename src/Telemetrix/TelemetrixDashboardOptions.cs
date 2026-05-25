namespace Telemetrix;

/// <summary>Configures the embedded Telemetrix dashboard surfaced by <c>UseTelemetrixDashboard</c>.</summary>
public sealed class TelemetrixDashboardOptions
{
    private string _path = "/telemetrix";

    /// <summary>
    /// The base path the dashboard and its JSON API are served from. Must start with
    /// <c>/</c>. Default <c>/telemetrix</c>.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var normalized = "/" + value.Trim().Trim('/');
            _path = normalized;
        }
    }

    /// <summary>The product name shown in the dashboard header. Default <c>Telemetrix</c>.</summary>
    public string Title { get; set; } = "Telemetrix";

    /// <summary>
    /// How often, in milliseconds, the dashboard polls for fresh data while live mode is on.
    /// Default <c>2000</c>.
    /// </summary>
    public int RefreshIntervalMs { get; set; } = 2000;

    /// <summary>
    /// The accent colour (any CSS colour) applied to the executive theme. Default a calm indigo.
    /// </summary>
    public string AccentColor { get; set; } = "#6366f1";
}

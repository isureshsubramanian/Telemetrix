using System.Text.Json;
using System.Text.Json.Serialization;

namespace Telemetrix.Dashboard;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for the dashboard JSON API.</summary>
internal static class TelemetrixJson
{
    /// <summary>camelCase, string enums, nulls omitted.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

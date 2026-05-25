using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Telemetrix.Dashboard;

/// <summary>
/// Loads and caches the dashboard's static assets (HTML, CSS, JS) which are embedded into
/// the Telemetrix assembly. This is what makes the dashboard genuinely zero-dependency: there
/// is nothing to copy to <c>wwwroot</c> and nothing to deploy alongside the application.
/// </summary>
internal sealed class EmbeddedAssets
{
    private static readonly Assembly OwningAssembly = typeof(EmbeddedAssets).Assembly;

    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the raw bytes of an embedded asset, or <see langword="null"/> when missing.</summary>
    public byte[]? GetBytes(string fileName) => _cache.GetOrAdd(fileName, Load);

    /// <summary>Returns an embedded asset decoded as UTF-8 text, or <see langword="null"/> when missing.</summary>
    public string? GetText(string fileName)
    {
        var bytes = GetBytes(fileName);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static byte[]? Load(string fileName)
    {
        // Embedded resource names are "<RootNamespace>.wwwroot.<file>"; match by suffix so the
        // lookup is resilient to the exact root namespace.
        var suffix = "wwwroot." + fileName;
        var resourceName = Array.Find(
            OwningAssembly.GetManifestResourceNames(),
            name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return null;
        }

        using var stream = OwningAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Maps a file extension to a response content type.</summary>
    public static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}

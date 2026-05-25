using System.Collections;
using System.Globalization;
using System.Text;

namespace Telemetrix.Internal;

/// <summary>
/// Converts arbitrary telemetry attribute values into safe, bounded, human-readable strings.
/// Telemetry tags can contain anything (arrays, byte blobs, nested objects); this keeps the
/// dashboard payloads small and never throws.
/// </summary>
internal static class ValueFormatter
{
    private const int MaxLength = 2048;

    public static string? Format(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return Truncate(s);
            case bool b:
                return b ? "true" : "false";
            case byte[] bytes:
                var head = Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32));
                return $"0x{head}{(bytes.Length > 32 ? "…" : string.Empty)} ({bytes.Length} bytes)";
            case IFormattable formattable:
                return Truncate(formattable.ToString(null, CultureInfo.InvariantCulture));
        }

        if (value is IEnumerable enumerable)
        {
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append(Format(item));
                if (sb.Length > MaxLength)
                {
                    sb.Append(", …");
                    break;
                }
            }

            sb.Append(']');
            return Truncate(sb.ToString());
        }

        try
        {
            return Truncate(value.ToString());
        }
        catch
        {
            return value.GetType().Name;
        }
    }

    private static string? Truncate(string? value)
        => value is null
            ? null
            : value.Length <= MaxLength
                ? value
                : string.Concat(value.AsSpan(0, MaxLength), "…");
}

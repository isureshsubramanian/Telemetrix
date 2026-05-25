using System.Text.RegularExpressions;

namespace Telemetrix.Diagnostics;

/// <summary>
/// Lightweight, dependency-free SQL prettifier. It does not parse SQL; it simply places
/// major clauses on their own lines so the inspector stays readable. Entity Framework Core
/// already emits multi-line SQL, in which case the text is returned untouched.
/// </summary>
internal static partial class SqlFormatter
{
    [GeneratedRegex(
        @"\s+(LEFT\s+OUTER\s+JOIN|RIGHT\s+OUTER\s+JOIN|FULL\s+OUTER\s+JOIN|INNER\s+JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|CROSS\s+JOIN|OUTER\s+APPLY|CROSS\s+APPLY|GROUP\s+BY|ORDER\s+BY|UNION\s+ALL|JOIN|FROM|WHERE|HAVING|VALUES|LIMIT|OFFSET|UNION|EXCEPT|INTERSECT|RETURNING)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClauseRegex();

    [GeneratedRegex(@"^[\s;]*(--[^\n]*\n|/\*.*?\*/)*\s*([A-Za-z]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex LeadingKeywordRegex();

    private static readonly HashSet<string> KnownOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER",
        "DROP", "TRUNCATE", "EXEC", "EXECUTE", "WITH", "CALL", "PRAGMA",
    };

    /// <summary>Re-indents single-line SQL onto multiple lines; leaves multi-line SQL unchanged.</summary>
    public static string Format(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return string.Empty;
        }

        var sql = commandText.Trim();
        if (sql.Contains('\n'))
        {
            return sql;
        }

        return ClauseRegex().Replace(sql, match =>
        {
            var clause = Regex.Replace(match.Groups[1].Value, @"\s+", " ").ToUpperInvariant();
            return $"\n{clause} ";
        });
    }

    /// <summary>Returns the leading SQL verb (for example <c>SELECT</c>), or <c>QUERY</c> when unknown.</summary>
    public static string Operation(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return "QUERY";
        }

        var match = LeadingKeywordRegex().Match(commandText);
        if (match.Success)
        {
            var keyword = match.Groups[2].Value.ToUpperInvariant();
            if (KnownOperations.Contains(keyword))
            {
                return keyword;
            }
        }

        return "QUERY";
    }
}

using System.Diagnostics;
using Telemetrix.Models;

namespace Telemetrix.Diagnostics;

/// <summary>
/// Walks the current call stack to find the first application frame, so the SQL inspector
/// can show the exact line of code that issued a database command. Framework and Telemetrix
/// frames are skipped. Requires debug symbols to resolve file and line information.
/// </summary>
internal static class StackTraceResolver
{
    private static readonly string[] FrameworkNamespacePrefixes =
    [
        "System.",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
        "OpenTelemetry",
        "Npgsql",
        "Pomelo.",
        "MySqlConnector",
        "Dapper",
        "Castle.",
    ];

    /// <summary>Resolves the first application stack frame, or <see langword="null"/> if none is found.</summary>
    public static CodeLocation? Resolve()
    {
        try
        {
            var selfAssembly = typeof(StackTraceResolver).Assembly;
            var trace = new StackTrace(fNeedFileInfo: true);

            foreach (var frame in trace.GetFrames())
            {
                var method = frame.GetMethod();
                var declaringType = method?.DeclaringType;
                if (declaringType is null)
                {
                    continue;
                }

                // Skip Telemetrix's own frames by assembly identity (not namespace), so that a
                // host application whose namespace happens to start with "Telemetrix" is not skipped.
                if (declaringType.Assembly == selfAssembly)
                {
                    continue;
                }

                var ns = declaringType.Namespace;
                if (ns is not null && IsFrameworkNamespace(ns))
                {
                    continue;
                }

                var file = frame.GetFileName();
                var line = frame.GetFileLineNumber();
                var member = $"{declaringType.Name}.{method!.Name}";
                return new CodeLocation(file, line, member, ns);
            }
        }
        catch
        {
            // Stack resolution is best-effort; never throw into telemetry capture.
        }

        return null;
    }

    private static bool IsFrameworkNamespace(string ns)
    {
        foreach (var prefix in FrameworkNamespacePrefixes)
        {
            if (ns.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

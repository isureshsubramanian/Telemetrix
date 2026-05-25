namespace Telemetrix.Models;

/// <summary>An immutable snapshot of a single log record.</summary>
public sealed class LogEntry
{
    /// <summary>A monotonically increasing id assigned by the store; used for paging and ordering.</summary>
    public long Sequence { get; internal set; }

    /// <summary>UTC timestamp of the log record.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>The level name (<c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Critical</c>).</summary>
    public required string Level { get; init; }

    /// <summary>Numeric severity (0 = Trace … 5 = Critical) for filtering and sorting.</summary>
    public int Severity { get; init; }

    /// <summary>The logger category name.</summary>
    public string? Category { get; init; }

    /// <summary>The formatted log message.</summary>
    public required string Message { get; init; }

    /// <summary>The numeric event id.</summary>
    public int EventId { get; init; }

    /// <summary>The event name, when supplied.</summary>
    public string? EventName { get; init; }

    /// <summary>The correlated trace id, when the log was written inside a span.</summary>
    public string? TraceId { get; init; }

    /// <summary>The correlated span id, when the log was written inside a span.</summary>
    public string? SpanId { get; init; }

    /// <summary>Exception detail, when an exception was logged.</summary>
    public ExceptionInfo? Exception { get; init; }

    /// <summary>Structured state attributes attached to the log record.</summary>
    public IReadOnlyList<TagItem> Attributes { get; init; } = [];
}

/// <summary>A captured exception.</summary>
/// <param name="Type">The exception CLR type name.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">The stack trace text, when available.</param>
public sealed record ExceptionInfo(string Type, string Message, string? StackTrace);

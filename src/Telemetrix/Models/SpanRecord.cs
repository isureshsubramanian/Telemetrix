using System.Diagnostics;
using Telemetrix.Internal;

namespace Telemetrix.Models;

/// <summary>A key/value attribute attached to a span, event or log entry.</summary>
/// <param name="Key">The attribute name.</param>
/// <param name="Value">The attribute value, formatted for display.</param>
public sealed record TagItem(string Key, string? Value);

/// <summary>A timestamped event recorded on a span.</summary>
/// <param name="Name">The event name (for example <c>exception</c>).</param>
/// <param name="TimestampUtc">When the event occurred.</param>
/// <param name="Tags">Attributes attached to the event.</param>
public sealed record SpanEventItem(string Name, DateTime TimestampUtc, IReadOnlyList<TagItem> Tags);

/// <summary>
/// An immutable snapshot of a single span. Snapshots are taken eagerly because the
/// OpenTelemetry SDK recycles <see cref="Activity"/> and <c>LogRecord</c> instances after export.
/// </summary>
public sealed class SpanRecord
{
    /// <summary>The 32-character hex trace id this span belongs to.</summary>
    public required string TraceId { get; init; }

    /// <summary>The 16-character hex span id.</summary>
    public required string SpanId { get; init; }

    /// <summary>The parent span id, or <see langword="null"/> for a root span.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>The display name of the span.</summary>
    public required string Name { get; init; }

    /// <summary>The span kind.</summary>
    public SpanKind Kind { get; init; }

    /// <summary>How the span was captured.</summary>
    public SpanSource Source { get; init; }

    /// <summary>The originating <see cref="ActivitySource"/> name, when applicable.</summary>
    public string? SourceName { get; init; }

    /// <summary>UTC start time.</summary>
    public DateTime StartTimeUtc { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>The resolved status.</summary>
    public SpanStatus Status { get; init; }

    /// <summary>An optional human-readable status description.</summary>
    public string? StatusDescription { get; init; }

    /// <summary>The span attributes, in insertion order.</summary>
    public IReadOnlyList<TagItem> Tags { get; init; } = [];

    /// <summary>Events recorded on the span.</summary>
    public IReadOnlyList<SpanEventItem> Events { get; init; } = [];

    /// <summary>Database command detail; non-null only when <see cref="Source"/> is <see cref="SpanSource.Sql"/>.</summary>
    public SqlCommandInfo? Sql { get; init; }

    /// <summary>UTC end time (start + duration).</summary>
    public DateTime EndTimeUtc => StartTimeUtc.AddMilliseconds(DurationMs);

    /// <summary>Builds an immutable snapshot from a completed <see cref="Activity"/>.</summary>
    public static SpanRecord FromActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var tags = new List<TagItem>();
        foreach (var tag in activity.TagObjects)
        {
            tags.Add(new TagItem(tag.Key, ValueFormatter.Format(tag.Value)));
        }

        var events = new List<SpanEventItem>();
        foreach (var ev in activity.Events)
        {
            var eventTags = new List<TagItem>();
            foreach (var tag in ev.Tags)
            {
                eventTags.Add(new TagItem(tag.Key, ValueFormatter.Format(tag.Value)));
            }

            events.Add(new SpanEventItem(ev.Name, ev.Timestamp.UtcDateTime, eventTags));
        }

        var parentSpanId = activity.ParentSpanId.ToHexString();
        if (string.IsNullOrEmpty(parentSpanId) || parentSpanId == "0000000000000000")
        {
            parentSpanId = null;
        }

        var name = string.IsNullOrEmpty(activity.DisplayName) ? activity.OperationName : activity.DisplayName;

        return new SpanRecord
        {
            TraceId = activity.TraceId.ToHexString(),
            SpanId = activity.SpanId.ToHexString(),
            ParentSpanId = parentSpanId,
            Name = string.IsNullOrEmpty(name) ? "(unnamed span)" : name,
            Kind = (SpanKind)(int)activity.Kind,
            Source = SpanSource.Activity,
            SourceName = activity.Source.Name,
            StartTimeUtc = activity.StartTimeUtc,
            DurationMs = activity.Duration.TotalMilliseconds,
            Status = ResolveStatus(activity),
            StatusDescription = activity.StatusDescription,
            Tags = tags,
            Events = events,
        };
    }

    private static SpanStatus ResolveStatus(Activity activity)
    {
        if (activity.Status == ActivityStatusCode.Error)
        {
            return SpanStatus.Error;
        }

        if (activity.Status == ActivityStatusCode.Ok)
        {
            return SpanStatus.Ok;
        }

        // OpenTelemetry instrumentation often leaves the native status Unset and signals
        // failure through attributes instead. Infer an error so the waterfall stays accurate.
        foreach (var tag in activity.TagObjects)
        {
            switch (tag.Key)
            {
                case "http.response.status_code" or "http.status_code"
                    when tag.Value is not null
                         && int.TryParse(tag.Value.ToString(), out var statusCode)
                         && statusCode >= 500:
                    return SpanStatus.Error;
                case "otel.status_code"
                    when string.Equals(tag.Value?.ToString(), "ERROR", StringComparison.OrdinalIgnoreCase):
                    return SpanStatus.Error;
                case "error" when string.Equals(tag.Value?.ToString(), "true", StringComparison.OrdinalIgnoreCase):
                    return SpanStatus.Error;
            }
        }

        foreach (var ev in activity.Events)
        {
            if (ev.Name == "exception")
            {
                return SpanStatus.Error;
            }
        }

        return SpanStatus.Unset;
    }
}

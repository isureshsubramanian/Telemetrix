using Telemetrix;
using Telemetrix.Models;
using Telemetrix.Storage;
using Xunit;

namespace Telemetrix.Tests;

public sealed class TelemetrixStoreTests
{
    private static readonly DateTime Base = new(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc);

    private static SpanRecord Span(
        string traceId,
        string spanId,
        string? parentSpanId,
        string name,
        SpanStatus status = SpanStatus.Ok,
        SpanSource source = SpanSource.Activity,
        double durationMs = 10,
        int startOffsetSeconds = 0)
        => new()
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            Name = name,
            Kind = SpanKind.Server,
            Source = source,
            StartTimeUtc = Base.AddSeconds(startOffsetSeconds),
            DurationMs = durationMs,
            Status = status,
        };

    private static LogEntry Log(string level, int severity, string message, string? traceId = null, string? spanId = null)
        => new()
        {
            TimestampUtc = Base,
            Level = level,
            Severity = severity,
            Message = message,
            TraceId = traceId,
            SpanId = spanId,
        };

    [Fact]
    public void AddSpan_AssemblesSpansIntoOneTrace()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddSpan(Span("trace-a", "root", null, "GET /orders"));
        store.AddSpan(Span("trace-a", "child", "root", "DB SELECT", source: SpanSource.Sql));

        var traces = store.GetTraces(null, null, 100);

        var trace = Assert.Single(traces);
        Assert.Equal("trace-a", trace.TraceId);
        Assert.Equal("GET /orders", trace.RootName);
        Assert.Equal(2, trace.SpanCount);
        Assert.Equal(1, trace.SqlCount);
    }

    [Fact]
    public void GetTrace_ReturnsDetailWithCorrelatedLogs()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddSpan(Span("trace-b", "root", null, "GET /report", startOffsetSeconds: 0));
        store.AddSpan(Span("trace-b", "child", "root", "work.compute", startOffsetSeconds: 1));
        store.AddLog(Log("Information", 2, "inside trace", traceId: "trace-b", spanId: "child"));
        store.AddLog(Log("Information", 2, "unrelated log", traceId: "other-trace"));

        var detail = store.GetTrace("trace-b");

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.SpanCount);
        Assert.Equal("root", detail.Spans[0].SpanId); // ordered by start time
        var log = Assert.Single(detail.Logs);
        Assert.Equal("inside trace", log.Message);
    }

    [Fact]
    public void GetTrace_ReturnsNullForUnknownTrace()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        Assert.Null(store.GetTrace("does-not-exist"));
    }

    [Fact]
    public void AddSpan_EvictsOldestTracesBeyondLimit()
    {
        var store = new TelemetrixStore(new TelemetrixOptions { MaxTraces = 3 });
        for (var i = 0; i < 6; i++)
        {
            store.AddSpan(Span($"trace-{i}", "root", null, $"op-{i}", startOffsetSeconds: i));
        }

        var traces = store.GetTraces(null, null, 100);

        Assert.Equal(3, traces.Count);
        Assert.Null(store.GetTrace("trace-0"));
        Assert.NotNull(store.GetTrace("trace-5"));
    }

    [Fact]
    public void GetTraces_FiltersByStatusAndSearch()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddSpan(Span("ok-trace", "r", null, "GET /healthy", startOffsetSeconds: 0));
        store.AddSpan(Span("err-trace", "r", null, "GET /broken", SpanStatus.Error, startOffsetSeconds: 1));

        Assert.Single(store.GetTraces(null, "error", 100));
        Assert.Single(store.GetTraces(null, "ok", 100));
        Assert.Equal(2, store.GetTraces(null, "all", 100).Count);
        Assert.Single(store.GetTraces("broken", null, 100));
        Assert.Empty(store.GetTraces("nonsense", null, 100));
    }

    [Fact]
    public void GetStats_CountsTracesErrorsAndSpans()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddSpan(Span("ok-trace", "r", null, "GET /a", startOffsetSeconds: 0));
        store.AddSpan(Span("err-trace", "r", null, "GET /b", SpanStatus.Error, startOffsetSeconds: 1));
        store.AddSpan(Span("err-trace", "db", "r", "DB SELECT", source: SpanSource.Sql, startOffsetSeconds: 1));

        var stats = store.GetStats("Development");

        Assert.Equal(2, stats.TraceCount);
        Assert.Equal(1, stats.ErrorCount);
        Assert.Equal(3, stats.SpanCount);
        Assert.Equal(1, stats.SqlCount);
        Assert.Equal("Development", stats.Environment);
        Assert.Equal(30, stats.Throughput.Count);
    }

    [Fact]
    public void AddLog_RingBufferEnforcesLimitAndAssignsSequence()
    {
        var store = new TelemetrixStore(new TelemetrixOptions { MaxLogEntries = 5 });
        for (var i = 0; i < 12; i++)
        {
            store.AddLog(Log("Information", 2, $"message {i}"));
        }

        var logs = store.GetLogs(null, null, null, 100);

        Assert.Equal(5, logs.Count);
        Assert.Equal("message 11", logs[0].Message); // newest first
        Assert.All(logs, l => Assert.True(l.Sequence > 0));
    }

    [Fact]
    public void GetLogs_FiltersByLevelAndSearch()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddLog(Log("Information", 2, "started up"));
        store.AddLog(Log("Warning", 3, "disk getting full"));
        store.AddLog(Log("Error", 4, "payment failed"));

        Assert.Equal(2, store.GetLogs("3", null, null, 100).Count); // Warning+
        Assert.Single(store.GetLogs("Error", null, null, 100));
        Assert.Single(store.GetLogs(null, "payment", null, 100));
    }

    [Fact]
    public void RecordMetric_AppendsPointsToASeries()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.RecordMetric("requests", "MyMeter", "count", "Total requests", "Counter", [], Base, 5, 0, 5);
        store.RecordMetric("requests", "MyMeter", "count", "Total requests", "Counter", [], Base.AddSeconds(2), 9, 0, 9);

        var series = Assert.Single(store.GetMetrics());
        Assert.Equal("requests", series.Name);
        Assert.Equal(2, series.Points.Count);
        Assert.Equal(9, series.Points[^1].Value);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var store = new TelemetrixStore(new TelemetrixOptions());
        store.AddSpan(Span("t", "r", null, "op"));
        store.AddLog(Log("Information", 2, "msg"));
        store.RecordMetric("m", "Meter", null, null, "Counter", [], Base, 1, 0, 1);

        store.Clear();

        Assert.Empty(store.GetTraces(null, null, 100));
        Assert.Empty(store.GetLogs(null, null, null, 100));
        Assert.Empty(store.GetMetrics());
    }
}

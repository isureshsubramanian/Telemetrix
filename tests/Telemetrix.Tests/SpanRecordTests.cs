using System.Diagnostics;
using Telemetrix.Models;
using Xunit;

namespace Telemetrix.Tests;

public sealed class SpanRecordTests
{
    [Fact]
    public void FromActivity_CapturesIdentityKindTagsAndStatus()
    {
        using var source = new ActivitySource("Telemetrix.Tests.Source");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Telemetrix.Tests.Source",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("checkout", ActivityKind.Server);
        Assert.NotNull(activity);
        activity!.SetTag("http.request.method", "POST");
        activity.SetStatus(ActivityStatusCode.Error, "boom");
        activity.Stop();

        var span = SpanRecord.FromActivity(activity);

        Assert.Equal("checkout", span.Name);
        Assert.Equal(SpanKind.Server, span.Kind);
        Assert.Equal(SpanSource.Activity, span.Source);
        Assert.Equal(activity.TraceId.ToHexString(), span.TraceId);
        Assert.Equal(activity.SpanId.ToHexString(), span.SpanId);
        Assert.Equal(SpanStatus.Error, span.Status);
        Assert.Contains(span.Tags, t => t.Key == "http.request.method" && t.Value == "POST");
    }

    [Fact]
    public void FromActivity_InfersErrorFromHttpStatusCodeTag()
    {
        using var source = new ActivitySource("Telemetrix.Tests.Http");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Telemetrix.Tests.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("GET /thing", ActivityKind.Server);
        Assert.NotNull(activity);
        activity!.SetTag("http.response.status_code", 503);
        activity.Stop();

        var span = SpanRecord.FromActivity(activity);

        Assert.Equal(SpanStatus.Error, span.Status);
    }

    [Fact]
    public void FromActivity_RootSpanHasNoParent()
    {
        using var source = new ActivitySource("Telemetrix.Tests.Root");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Telemetrix.Tests.Root",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("root-op");
        Assert.NotNull(activity);
        activity!.Stop();

        var span = SpanRecord.FromActivity(activity);
        Assert.Null(span.ParentSpanId);
    }
}

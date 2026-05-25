using Telemetrix.Internal;
using Xunit;

namespace Telemetrix.Tests;

public sealed class ValueFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsNull()
        => Assert.Null(ValueFormatter.Format(null));

    [Fact]
    public void Format_Bool_ReturnsLowercaseLiteral()
    {
        Assert.Equal("true", ValueFormatter.Format(true));
        Assert.Equal("false", ValueFormatter.Format(false));
    }

    [Fact]
    public void Format_Enumerable_RendersBracketedList()
        => Assert.Equal("[1, 2, 3]", ValueFormatter.Format(new[] { 1, 2, 3 }));

    [Fact]
    public void Format_LongString_IsTruncated()
    {
        var formatted = ValueFormatter.Format(new string('x', 6000));

        Assert.NotNull(formatted);
        Assert.True(formatted!.Length < 6000);
        Assert.EndsWith("…", formatted);
    }

    [Fact]
    public void Format_Integer_UsesInvariantCulture()
        => Assert.Equal("4096", ValueFormatter.Format(4096));
}

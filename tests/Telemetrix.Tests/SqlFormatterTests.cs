using Telemetrix.Diagnostics;
using Xunit;

namespace Telemetrix.Tests;

public sealed class SqlFormatterTests
{
    [Theory]
    [InlineData("SELECT * FROM Products", "SELECT")]
    [InlineData("  insert into Orders values (1)", "INSERT")]
    [InlineData("UPDATE Products SET Stock = 0", "UPDATE")]
    [InlineData("delete from Orders where Id = 1", "DELETE")]
    [InlineData("EXEC sp_GetProducts", "EXEC")]
    [InlineData("", "QUERY")]
    [InlineData("gibberish text", "QUERY")]
    public void Operation_DetectsLeadingVerb(string sql, string expected)
        => Assert.Equal(expected, SqlFormatter.Operation(sql));

    [Fact]
    public void Format_BreaksMajorClausesOntoNewLines()
    {
        var result = SqlFormatter.Format("SELECT p.Name FROM Products p WHERE p.Stock > 0 ORDER BY p.Name");

        Assert.Contains("\nFROM", result);
        Assert.Contains("\nWHERE", result);
        Assert.Contains("\nORDER BY", result);
    }

    [Fact]
    public void Format_LeavesMultiLineSqlUntouched()
    {
        const string input = "SELECT 1\nFROM Products";
        Assert.Equal(input, SqlFormatter.Format(input));
    }

    [Fact]
    public void Format_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Equal(string.Empty, SqlFormatter.Format(null));
        Assert.Equal(string.Empty, SqlFormatter.Format("   "));
    }
}

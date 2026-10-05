using FantasyWorld.Cli;

namespace FantasyWorld.Sim.Tests.Cli;

public class DurationParserTests
{
    [Theory]
    [InlineData("30d", 30)]
    [InlineData("4mo", 120)]
    [InlineData("7y", 2520)]
    [InlineData(" 1y ", 360)]
    public void ParsesUnits(string text, long expectedDays) =>
        Assert.Equal(expectedDays, DurationParser.ParseDays(text));

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("3w")]
    [InlineData("-2d")]
    public void RejectsBadInput(string text) =>
        Assert.Throws<FormatException>(() => DurationParser.ParseDays(text));
}

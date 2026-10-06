using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class WorldDateTests
{
    [Fact]
    public void DayZero_IsFirstDayOfYearOne()
    {
        var d = new WorldDate(0);
        Assert.Equal(1, d.Year);
        Assert.Equal(1, d.Month);
        Assert.Equal(1, d.DayOfMonth);
        Assert.Equal(Season.Spring, d.Season);
        Assert.True(d.IsFirstOfMonth);
        Assert.True(d.IsFirstOfYear);
    }

    [Fact]
    public void FromYmd_RoundTrips()
    {
        var d = WorldDate.FromYmd(312, 5, 16);
        Assert.Equal(312, d.Year);
        Assert.Equal(5, d.Month);
        Assert.Equal(16, d.DayOfMonth);
        Assert.False(d.IsFirstOfMonth);
    }

    [Fact]
    public void YearHas360Days()
    {
        Assert.Equal(WorldDate.FromYmd(2, 1, 1), WorldDate.FromYmd(1, 1, 1).AddDays(360));
    }

    [Theory]
    [InlineData(1, Season.Spring)]
    [InlineData(3, Season.Spring)]
    [InlineData(4, Season.Summer)]
    [InlineData(7, Season.Autumn)]
    [InlineData(12, Season.Winter)]
    public void MonthMapsToSeason(int month, Season expected)
    {
        Assert.Equal(expected, WorldDate.FromYmd(1, month, 1).Season);
    }

    [Fact]
    public void ComparisonOperators()
    {
        Assert.True(new WorldDate(1) < new WorldDate(2));
        Assert.True(new WorldDate(2) > new WorldDate(1));
        Assert.True(new WorldDate(2) <= new WorldDate(2));
        Assert.True(new WorldDate(2) >= new WorldDate(2));
    }

    [Fact]
    public void ToString_IsReadable()
    {
        Assert.Equal("Y312 M5 D16", WorldDate.FromYmd(312, 5, 16).ToString());
    }
}

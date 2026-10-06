using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class TimeScaleTests
{
    [Fact]
    public void OneRealDay_IsOneWorldYear() =>
        Assert.Equal(360, TimeScale.WholeWorldDays(TimeSpan.FromDays(1)));

    [Fact]
    public void EightHoursOfSleep_IsFourMonths() =>
        Assert.Equal(120, TimeScale.WholeWorldDays(TimeSpan.FromHours(8)));

    [Fact]
    public void PartialWorldDay_RoundsDown() =>
        Assert.Equal(0, TimeScale.WholeWorldDays(TimeSpan.FromSeconds(239)));

    [Fact]
    public void NegativeElapsed_IsZero() =>
        Assert.Equal(0, TimeScale.WholeWorldDays(TimeSpan.FromHours(-3)));

    [Fact]
    public void CatchUpCap_IsThirtyYears() =>
        Assert.Equal(10_800, TimeScale.MaxCatchUpDays);
}

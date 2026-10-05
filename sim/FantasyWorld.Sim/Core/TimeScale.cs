namespace FantasyWorld.Sim.Core;

/// <summary>1 real day = 1 world year, so 1 world day = 240 real seconds.</summary>
public static class TimeScale
{
    public static readonly TimeSpan RealTimePerWorldDay =
        TimeSpan.FromSeconds(86_400.0 / WorldDate.DaysPerYear);

    public const long MaxCatchUpDays = 30L * WorldDate.DaysPerYear;

    public static long WholeWorldDays(TimeSpan real) =>
        real <= TimeSpan.Zero ? 0 : real.Ticks / RealTimePerWorldDay.Ticks;
}

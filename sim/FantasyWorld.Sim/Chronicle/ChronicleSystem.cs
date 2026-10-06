using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// Keeps the chronicle compact, last in the tick order: at the start of each decade, the decade that has just become
/// <see cref="Compactor.AgeYears"/> years old is compacted into one summary event. Running inside the simulation
/// makes compaction deterministic: a world advanced in one go or in many sessions is the same.
/// </summary>
public sealed class ChronicleSystem : ISimSystem
{
    public string Name => "chronicle";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var year = ctx.Date.Year;
        if ((year - 1) % 10 != 0) return;
        var last = year - Compactor.AgeYears - 1;
        if (last < 1) return;
        Compactor.Compact(ctx.World, ctx.Log, ctx.Queue, Math.Max(1, last - 9), last);
    }
}

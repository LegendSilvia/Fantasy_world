using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Spec §5 "World generation": a new world runs about a century before the player arrives, so they find existing
/// history, grudges, ruins and artifacts. The rules engine runs it (no LLM spend); the real-time clock is left
/// alone, so catch-up does not count those years, and nothing is marked seen, so the first digest tells the story
/// so far.
/// </summary>
public static class Prehistory
{
    public const int DefaultYears = 100;

    public static int DefaultYearsFor(WorldSize size) => size == WorldSize.Tiny ? 0 : DefaultYears;

    public static Simulator Run(World world, int years, IProgress<WorldDate>? progress = null, bool checkInvariants = false)
    {
        if (years < 0) throw new ArgumentOutOfRangeException(nameof(years), "Prehistory cannot be negative.");
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);
        sim.AdvanceDays((long)years * WorldDate.DaysPerYear, progress);
        return sim;
    }
}

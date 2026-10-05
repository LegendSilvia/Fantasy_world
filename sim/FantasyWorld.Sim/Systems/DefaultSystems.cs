using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Systems.Demo;

namespace FantasyWorld.Sim.Systems;

/// <summary>The fixed system order for monthly ticks. Demo systems are replaced in Plans 2–4.</summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[] { new HarvestSystem(), new UnrestSystem() };
}

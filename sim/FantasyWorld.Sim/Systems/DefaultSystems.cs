using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Economy → Regions → Politics → …).
/// Settlements stand in for politics until realms arrive in Plan 4.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() =>
        new ISimSystem[] { new EconomySystem(), new RegionSystem(), new SettlementSystem() };
}

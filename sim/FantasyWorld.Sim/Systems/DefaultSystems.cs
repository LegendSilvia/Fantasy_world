using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures). Settlements stand in for politics until realms
/// arrive in Plan 4; the character systems follow.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new FamilySystem(),
        new RelationsSystem(),
        new DeedsSystem(),
        new ArtifactSystem(),
        new PromotionSystem(),
    };
}

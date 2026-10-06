using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems.
/// </summary>
public static class DefaultSystems
{
    public static IReadOnlyList<ISimSystem> Create() => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(),
        new DiplomacySystem(),
        new MilitarySystem(),
        new FamilySystem(),
        new RelationsSystem(),
        new DeedsSystem(),
        new ArtifactSystem(),
        new PromotionSystem(),
    };
}

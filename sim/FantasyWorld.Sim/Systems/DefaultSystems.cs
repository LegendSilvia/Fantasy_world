using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The fixed system order for monthly and yearly ticks (spec §7: Aging → Economy → Regions →
/// Politics → Military → Characters → Structures): settlements, realms and diplomacy are politics,
/// then armies, then the character systems, then structures, and last the chronicle's compaction.
/// </summary>
public static class DefaultSystems
{
    /// <summary>
    /// All systems, sharing one decider, arbiter and inventor (the LLM ones in a live game, Plan 5); null means the
    /// rules implementations. Both paths draw the same randomness, so offline worlds are unchanged by the choice.
    /// </summary>
    public static IReadOnlyList<ISimSystem> Create(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) => new ISimSystem[]
    {
        new LifeSystem(),
        new EconomySystem(),
        new RegionSystem(),
        new SettlementSystem(),
        new RealmSystem(inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new MonsterSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new ChronicleSystem(),
    };
}

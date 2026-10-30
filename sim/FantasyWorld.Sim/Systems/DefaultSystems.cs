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
        new RevolutionSystem(inventor),
        new SagaSystem(decider, arbiter, inventor),
        new FestivalSystem(inventor),
        new PrayerSystem(),
        new IncidentSystem(decider, inventor),
        new DiplomacySystem(decider, inventor),
        new MilitarySystem(arbiter, decider),
        new TradeSystem(),
        new FaithSystem(inventor),
        new MagicSystem(),
        new MonsterSystem(inventor),
        new GreatEventsSystem(inventor),
        new FamilySystem(decider),
        new RelationsSystem(),
        new DeedsSystem(decider, arbiter, inventor),
        new ClashSystem(),
        new TransformSystem(inventor),
        new IdentitySystem(inventor),
        new CompanionSystem(inventor),
        new ArtifactSystem(),
        new PromotionSystem(),
        new StructureSystem(inventor),
        new LoreSystem(inventor),
        new GoalSystem(),
        new GrowthSystem(),
        new MentorSystem(),
        new LevelSystem(),
        new ChronicleSystem(),
    };
}

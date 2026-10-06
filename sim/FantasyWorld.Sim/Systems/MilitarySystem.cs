using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Armies and campaigns (spec §6 "Campaign flow"), monthly: every realm at war raises an army (levies
/// from its settlements, men-at-arms its treasury can hire, its knights and mages, led by its marshal);
/// armies take orders (besiege the war's target or the nearest enemy holding; defend against invaders),
/// march along the region graph (30 travel days a month), forage or draw supply, pay upkeep, and suffer
/// attrition and desertion. Battles, sieges and peace live in the other parts of this class.
/// </summary>
public sealed partial class MilitarySystem : ISimSystem
{
    public const double LevyShare = 0.05;
    public const int MarchDaysPerMonth = 30;

    private readonly IArbiter _arbiter;
    private readonly IDecider _decider;

    public MilitarySystem(IArbiter? arbiter = null, IDecider? decider = null)
    {
        _arbiter = arbiter ?? new RulesArbiter();
        _decider = decider ?? new RulesDecider();
    }

    public string Name => "military";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var realmId in war.Attackers.Concat(war.Defenders).ToList())
                if (world.Realms[realmId] is { Destroyed: false } realm && ArmyOf(world, war, realm) is null && (ctx.Date.Month % 6 == 1 || war.StartDay >= ctx.Date.Day - 30))
                    Mobilise(ctx, war, realm);

        // Occupations lapse when occupier and sovereign are no longer at war (the land changed hands mid-war).
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
            if (Realms.SovereignOf(world, region) is not { } sovereign || !AtWarWith(world, region.OccupierRealmId, sovereign.Id))
                region.OccupierRealmId = 0;

        foreach (var army in Active(world).ToList())
        {
            if (army.Disbanded) continue;
            if (!world.Characters.TryGetValue(army.CommanderId, out var leader) || !leader.IsActive)
            {
                var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
                var successor = Commander(world, world.Realms[army.RealmId], serving, ctx.Date);
                army.CommanderId = successor?.Id ?? 0;
                if (successor is not null) army.Knights.Remove(successor.Id);
            }
            GiveOrders(world, army);
            March(world, army);
            Provision(ctx, army);
        }
        FightBattles(ctx);
        Besiege(ctx);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) Score(ctx, war);
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList()) ConsiderPeace(ctx, war);
    }

    partial void FightBattles(SimContext ctx);

    partial void Besiege(SimContext ctx);

    partial void Score(SimContext ctx, War war);

    partial void ConsiderPeace(SimContext ctx, War war);

    public static IEnumerable<Army> Active(World world) => world.Armies.Values.Where(a => !a.Disbanded);

    public static Army? ArmyOf(World world, War war, Realm realm) =>
        Active(world).FirstOrDefault(a => a.WarId == war.Id && a.RealmId == realm.Id);

    /// <summary>Raises the realm's host for this war at its capital.</summary>
    public static Army Mobilise(SimContext ctx, War war, Realm realm)
    {
        var world = ctx.World;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var raised = new SortedDictionary<long, int>();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)))
        {
            var levies = (int)(s.Adults * LevyShare);
            if (levies <= 0) continue;
            s.Adults -= levies;
            raised[s.Id] = levies;
        }
        var menAtArms = (int)Math.Clamp(realm.Treasury / 20, 0, 500);
        realm.Treasury -= menAtArms * 5;
        var serving = Active(world).SelectMany(a => a.Knights.Append(a.CommanderId)).ToHashSet();
        var knights = world.ActiveCharacters
            .Where(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId) && !serving.Contains(c.Id)
                        && c.Stage(ctx.Date) == LifeStage.Adult)
            .Select(c => c.Id).ToList();
        var mages = realm.MagicLaw == MagicLaw.Outlawed ? 0
            : world.ActiveCharacters.Count(c => c.Role == Role.Mage && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        var commander = Commander(world, realm, serving, ctx.Date);
        if (commander is not null) knights.Remove(commander.Id);

        var army = new Army
        {
            Id = world.Ids.Take(Army.Kind),
            Name = $"The Host of {realm.Name}",
            RealmId = realm.Id,
            WarId = war.Id,
            CommanderId = commander?.Id ?? 0,
            Levies = raised.Values.Sum(),
            MenAtArms = menAtArms,
            Knights = knights,
            Mages = mages,
            RegionId = world.Settlements[realm.CapitalId].RegionId,
            Objective = ArmyObjective.Defend,
            RaisedFrom = raised,
        };
        world.Armies[army.Id] = army;
        var actors = new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["war"] = war.Ref, ["army"] = army.Ref };
        if (commander is not null) actors["commander"] = commander.Ref;
        ctx.Emit(new EventDraft("ArmyRaised", Actors: actors, Place: world.Regions[army.RegionId].Ref,
            Payload: new Dictionary<string, string> { ["soldiers"] = army.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Trigger, 1.0) }, Importance: 25, Tags: new[] { "War" }));
        return army;
    }

    /// <summary>The marshal, else the adult ruler, else the most martial knight.</summary>
    private static Character? Commander(World world, Realm realm, HashSet<long> serving, Core.WorldDate today)
    {
        bool Free(Character c) => c.IsActive && !serving.Contains(c.Id) && c.Stage(today) != LifeStage.Child;
        if (realm.Council.TryGetValue(CouncilSeats.Marshal, out var marshal) && Free(world.Characters[marshal])) return world.Characters[marshal];
        if (Realms.RulerOf(world, realm) is { } ruler && Free(ruler)) return ruler;
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        return world.ActiveCharacters.Where(c => c.Role == Role.Knight && Free(c) && territory.Contains(world.Settlements[c.SettlementId].RegionId))
            .OrderByDescending(c => c.Attributes.Martial).ThenBy(c => c.Id).FirstOrDefault();
    }

    /// <summary>
    /// Orders, in priority: a beaten army goes home (and rejoins the war once its morale recovers); anyone retakes their side's occupied land; defenders meet
    /// invaders; attackers besiege the war's target, else the nearest enemy holding; strong defenders strike back.
    /// </summary>
    private static void GiveOrders(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var graph = world.Geography!.Graph;
        long target;
        var enemies = war.EnemiesOf(Side(war, realm));
        var invader = Active(world).Where(a => enemies.Contains(a.RealmId) && Friendly(world, war, realm, world.Regions[a.RegionId]))
            .OrderBy(a => graph.ShortestPath(army.RegionId, a.RegionId)?.Days ?? double.MaxValue).ThenBy(a => a.Id).FirstOrDefault();
        var lost = world.Regions.Values.Where(r => r.OccupierRealmId != 0 && !Hostile(world, war, realm, r)
                && !Friendly(world, war, realm, r) && Realms.SovereignOf(world, r) is { } owner && OurSide(war, realm).Contains(owner.Id))
            .OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
        var home = world.Settlements[realm.CapitalId].RegionId;
        if (army.Objective == ArmyObjective.Return && army.RegionId == home && army.Morale >= 50)
            army.Objective = ArmyObjective.Defend; // regrouped at home: back to the war
        if (army.Objective == ArmyObjective.Return)
            target = home;
        else if (lost is not null)
        {
            target = lost.Id;
            army.Objective = ArmyObjective.Defend;
        }
        else if (invader is not null && !war.IsAttacker(Side(war, realm)))
        {
            target = invader.RegionId;
            army.Objective = ArmyObjective.Defend;
        }
        else
        {
            var goals = world.Regions.Values.Where(r => Hostile(world, war, realm, r) && world.LivingSettlements(r).Any()).ToList();
            var preferred = goals.FirstOrDefault(r => r.Id == war.TargetRegionId);
            var nearest = goals.OrderBy(r => graph.ShortestPath(army.RegionId, r.Id)?.Days ?? double.MaxValue).ThenBy(r => r.Id).FirstOrDefault();
            var goal = war.IsAttacker(Side(war, realm)) ? preferred ?? nearest : invader is null && Strong(world, army) ? nearest : null;
            target = goal?.Id ?? world.Settlements[realm.CapitalId].RegionId;
            army.Objective = goal is null ? ArmyObjective.Defend : ArmyObjective.Siege;
        }
        if (target == army.TargetRegionId && (army.Path.Count > 0 || army.RegionId == target)) return;
        army.TargetRegionId = target;
        army.Path.Clear();
        army.DaysToNext = 0;
        if (graph.ShortestPath(army.RegionId, target) is { } route) army.Path.AddRange(route.Path.Skip(1));
    }

    private static void March(World world, Army army)
    {
        var budget = (double)MarchDaysPerMonth;
        var graph = world.Geography!.Graph;
        while (army.Path.Count > 0 && budget > 0)
        {
            if (army.DaysToNext <= 0) army.DaysToNext = graph.Between(army.RegionId, army.Path[0])?.TravelDays ?? 1;
            var step = Math.Min(budget, army.DaysToNext);
            army.DaysToNext -= step;
            budget -= step;
            if (army.DaysToNext > 0) break;
            army.RegionId = army.Path[0];
            army.Path.RemoveAt(0);
            if (EnemyArmyIn(world, army, army.RegionId)) break;
        }
    }

    /// <summary>Supply from friendly land is paid for; in enemy land the army forages and devastates.</summary>
    private static void Provision(SimContext ctx, Army army)
    {
        var world = ctx.World;
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var region = world.Regions[army.RegionId];
        if (Friendly(world, war, realm, region)) army.Supply = Math.Min(100, army.Supply + 20);
        else
        {
            var need = army.Soldiers * 0.5;
            var taken = Math.Min(region.Food, need);
            region.Food -= taken;
            region.Fertility = Math.Max(0, region.Fertility - 1);
            army.Supply = Math.Clamp(army.Supply + (taken >= need ? 10 : -15), 0, 100);
        }

        realm.Treasury -= army.MenAtArms * 0.5 + army.Knights.Count * 2 + army.Mages * 3;
        if (realm.Treasury < 0)
        {
            army.MenAtArms = (int)(army.MenAtArms * 0.9);
            army.Morale = Math.Max(0, army.Morale - 10);
        }
        if (army.Supply < 30)
        {
            army.Levies = (int)(army.Levies * 0.95);
            army.MenAtArms = (int)(army.MenAtArms * 0.97);
            army.Morale = Math.Max(0, army.Morale - 5);
        }
        else army.Morale = Math.Min(100, army.Morale + 2);
        foreach (var r in war.Attackers.Concat(war.Defenders)) world.Realms[r].WarWeariness = Math.Min(100, world.Realms[r].WarWeariness + 0.2);
    }

    public static IReadOnlyList<long> OurSide(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;

    /// <summary>The side leader a realm fights for in this war (its own id if it leads).</summary>
    public static long Side(War war, Realm realm) => war.IsAttacker(realm.Id) ? war.AttackerId : war.DefenderId;

    /// <summary>Held (and not occupied) by our side, or occupied by our side.</summary>
    public static bool Friendly(World world, War war, Realm realm, Region region)
    {
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        if (region.OccupierRealmId != 0) return ours.Contains(region.OccupierRealmId);
        return Realms.SovereignOf(world, region) is { } owner && ours.Contains(owner.Id);
    }

    /// <summary>Held by the other side and not already occupied by ours.</summary>
    public static bool Hostile(World world, War war, Realm realm, Region region)
    {
        var theirs = war.IsAttacker(realm.Id) ? war.Defenders : war.Attackers;
        var ours = war.IsAttacker(realm.Id) ? war.Attackers : war.Defenders;
        return Realms.SovereignOf(world, region) is { } owner && theirs.Contains(owner.Id) && !ours.Contains(region.OccupierRealmId);
    }

    private static bool EnemyArmyIn(World world, Army army, long regionId)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.RegionId == regionId && a.WarId == war.Id && enemies.Contains(a.RealmId));
    }

    /// <summary>Two realms fight on opposite sides of some ongoing war.</summary>
    private static bool AtWarWith(World world, long a, long b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.Attackers.Contains(a) && w.Defenders.Contains(b) || w.Attackers.Contains(b) && w.Defenders.Contains(a)));

    private static bool Strong(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        var theirs = Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId)).Sum(a => a.Soldiers);
        return army.Soldiers >= theirs * 1.2;
    }
}

using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Armies and campaigns (spec §6 "Campaign flow"), monthly: every realm at war raises an army (levies
/// from its settlements, men-at-arms its treasury can hire, its knights and mages, led by its marshal);
/// armies take orders (take the enemy holdings on the way to the war's target, nearest first; defend against invaders),
/// make one move a month to a neighbouring region along army roads that go round mountains or through their passes
/// (sub-project 10, <see cref="Campaigns"/>), forage or draw supply, pay upkeep, and suffer attrition and desertion.
/// Battles, sieges and peace live in the other parts of this class.
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
        // A war still mustering raises no host; once its year is out, both sides raise theirs at once.
        foreach (var war in world.Wars.Values.Where(w => !w.Ended && !w.Mustering(ctx.Date)).ToList())
            foreach (var realmId in war.Attackers.Concat(war.Defenders).ToList())
                if (world.Realms[realmId] is { Destroyed: false } realm && ArmyOf(world, war, realm) is null && (ctx.Date.Month % 6 == 1 || war.OpenDay >= ctx.Date.Day - 30))
                    Mobilise(ctx, war, realm);

        // Occupations lapse when occupier and sovereign are no longer at war (the land changed hands mid-war).
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
            if (Realms.SovereignOf(world, region) is not { } sovereign || !AtWarWith(world, region.OccupierRealmId, sovereign.Id))
            {
                // Recorded (and important enough to outlast compaction) so the history replay sees it, citing the change of hands or peace behind it.
                var occupier = world.Realms[region.OccupierRealmId];
                var ended = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded")
                    .Concat(ctx.Log.Involving(occupier.Ref).Where(e => e.Type is "PeaceSigned" or "IndependenceDeclared"))
                    .Select(e => e.Id).DefaultIfEmpty().Max();
                ctx.Emit(new EventDraft("OccupationLapsed", Actors: new Dictionary<string, EntityRef> { ["realm"] = occupier.Ref },
                    Place: region.Ref, Causes: ended == 0 ? null : new[] { new Cause(ended, CauseKind.Trigger, 0.8) },
                    Importance: Chronicle.Compactor.MinorBelow, Tags: new[] { "War" }));
                region.OccupierRealmId = 0;
            }

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
            // A host locked in a long battle holds the field: it takes no new orders and does not march until it ends.
            if (!army.Engaged(ctx.Date))
            {
                GiveOrders(world, army);
                ChooseMove(ctx, army);
                March(ctx, army);
            }
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
        // Arming the host draws on the realm's ore (sub-project 12).
        Stocks.Use(ctx, realm, ResourceKinds.Ore, army.Soldiers * Stocks.OreToRaise, war.DeclaredEventId);
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
    /// Orders, in priority: a beaten army goes home (and rejoins the war once its morale recovers); anyone meets a host of
    /// this war standing in their side's land; then retakes their side's occupied land; attackers take the enemy holdings
    /// on the way to the war's target, the nearest first, so the front creeps forward (sub-project 10); strong defenders
    /// strike back. Distances are army roads: a holding behind mountains without a pass cannot be a goal. The army is
    /// given only its next move, a step to a neighbouring region; the rest of the way is chosen again as the war unfolds.
    /// </summary>
    private static void GiveOrders(World world, Army army)
    {
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var (roads, _) = Campaigns.Roads(world, army.RegionId);
        double Days(long region) => roads.TryGetValue(region, out var d) ? d : double.MaxValue;
        long target;
        var enemies = war.EnemiesOf(Side(war, realm));
        // Only hosts of this war: battles are fought between armies of the same war, so chasing another war's army would never end.
        var invader = Active(world).Where(a => a.WarId == war.Id && enemies.Contains(a.RealmId) && Friendly(world, war, realm, world.Regions[a.RegionId]))
            .Where(a => roads.ContainsKey(a.RegionId)).OrderBy(a => Days(a.RegionId)).ThenBy(a => a.Id).FirstOrDefault();
        var lost = world.Regions.Values.Where(r => r.OccupierRealmId != 0 && !Hostile(world, war, realm, r)
                && !Friendly(world, war, realm, r) && Realms.SovereignOf(world, r) is { } owner && OurSide(war, realm).Contains(owner.Id)
                && roads.ContainsKey(r.Id))
            .OrderBy(r => Days(r.Id)).ThenBy(r => r.Id).FirstOrDefault();
        var home = world.Settlements[realm.CapitalId].RegionId;
        if (army.Objective == ArmyObjective.Return && army.RegionId == home && army.Morale >= 50)
            army.Objective = ArmyObjective.Defend; // regrouped at home: back to the war
        if (army.Objective == ArmyObjective.Return)
            target = home;
        else if (invader is not null)
        {
            // Meet the invader before retaking what it took: chasing lost regions while it takes the next one makes
            // the two hosts trade places month after month without ever fighting.
            target = invader.RegionId;
            army.Objective = ArmyObjective.Defend;
        }
        else if (lost is not null)
        {
            target = lost.Id;
            army.Objective = ArmyObjective.Defend;
        }
        else
        {
            var goals = world.Regions.Values.Where(r => Hostile(world, war, realm, r) && world.LivingSettlements(r).Any() && roads.ContainsKey(r.Id)).ToList();
            var nearest = goals.OrderBy(r => Days(r.Id)).ThenBy(r => r.Id).FirstOrDefault();
            // Attackers take what lies on the way to the war's target first: near, and nearer the target.
            var (toTarget, _) = Campaigns.Roads(world, war.TargetRegionId);
            var onTheWay = goals.OrderBy(r => Days(r.Id) + 0.5 * toTarget.GetValueOrDefault(r.Id, 0)).ThenBy(r => r.Id).FirstOrDefault();
            var goal = war.IsAttacker(Side(war, realm)) ? onTheWay : invader is null && Strong(world, army) ? nearest : null;
            target = goal?.Id ?? world.Settlements[realm.CapitalId].RegionId;
            army.Objective = goal is null ? ArmyObjective.Defend : ArmyObjective.Siege;
        }
        // A move under way is kept while the goal holds; otherwise the next move is the first step of the road there.
        if (target == army.TargetRegionId && (army.Path.Count > 0 || army.RegionId == target)) return;
        army.TargetRegionId = target;
        army.Path.Clear();
        army.DaysToNext = 0;
        if (target != army.RegionId && Campaigns.Road(world, army.RegionId, target) is { Path.Count: > 1 } road) army.Path.Add(road.Path[1]);
    }

    /// <summary>
    /// One move: the army marches on towards the neighbouring region its orders name, arriving once the way's days are
    /// spent (a hard pass may take more than a month), and goes no further that month. A way found closed (mountains
    /// without a pass) is given up, and new orders follow. Each arrival is recorded as a move.
    /// </summary>
    private static void March(SimContext ctx, Army army)
    {
        var world = ctx.World;
        if (army.Path.Count == 0) return;
        var from = army.RegionId;
        if (army.DaysToNext <= 0)
        {
            if (world.Geography!.Graph.Between(from, army.Path[0]) is not { } link || Campaigns.StepDays(world, link, from) is not { } days)
            {
                army.Path.Clear();
                army.TargetRegionId = 0;
                return;
            }
            army.DaysToNext = days;
        }
        // A mounted host covers a fifth more ground (sub-project 12).
        army.DaysToNext -= MarchDaysPerMonth * (Stocks.Mounted(world.Realms[army.RealmId]) ? 1.2 : 1);
        if (army.DaysToNext > 0) return;
        army.DaysToNext = 0;
        army.RegionId = army.Path[0];
        army.Path.RemoveAt(0);
        Moved(ctx, army, from);
    }

    /// <summary>
    /// A move made (sub-project 10): the host, its realm and war, whence it came and where it stands. Crossing into the
    /// enemy's land is news; a step through one's own is a line in the chronicle that later years condense away.
    /// </summary>
    private static void Moved(SimContext ctx, Army army, long from)
    {
        var world = ctx.World;
        var war = world.Wars[army.WarId];
        var realm = world.Realms[army.RealmId];
        var (before, now) = (world.Regions[from], world.Regions[army.RegionId]);
        var into = Hostile(world, war, realm, now) ? "enemy" : Friendly(world, war, realm, now) ? "own" : "neutral";
        var invades = into == "enemy" && !Hostile(world, war, realm, before);
        // The commander's reasoning for the move, if one was given, is its motive.
        using var motive = ctx.Annotate("motive", army.Orders);
        army.Orders = null;
        ctx.Emit(new EventDraft("ArmyMoved",
            Actors: new Dictionary<string, EntityRef> { ["army"] = army.Ref, ["realm"] = realm.Ref, ["war"] = war.Ref, ["from"] = before.Ref },
            Place: now.Ref,
            Payload: new Dictionary<string, string> { ["into"] = into, ["objective"] = army.Objective.ToString().ToLowerInvariant() },
            Importance: invades ? 26 : 6, Tags: new[] { "War" }));
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
        // A host in the field wears out arms and horses (sub-project 12).
        Stocks.Use(ctx, realm, ResourceKinds.Ore, army.Soldiers * Stocks.OrePerSoldier, war.DeclaredEventId);
        Stocks.Use(ctx, realm, ResourceKinds.Horses, army.Knights.Count * Stocks.HorsesPerKnight + army.Soldiers * Stocks.HorsesPerSoldier, war.DeclaredEventId);
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

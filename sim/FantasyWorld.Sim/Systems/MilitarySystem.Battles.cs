using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Battles (spec §6 step 4): where hostile armies meet, the largest of each side fight. Strength comes
/// from troops, the commander's martial skill, morale and supply, with defenders favoured on their own
/// ground and in rough terrain; a seeded roll decides. Losers suffer heavier casualties and retreat (or
/// break and disband); every commander and knight present faces a fate chosen by the arbiter. With Claude (sub-project 8
/// §5) the arbiter decides the battle whole; one longer than a day holds both hosts on the field (BattleJoined), is told
/// on its chapters' days (BattleTurned) and ends on its last (BattleFought), or is broken off if a host or the war is gone.
/// </summary>
public sealed partial class MilitarySystem
{
    /// <summary>The share of its troops a side loses: within these, the rules roll and Claude chooses.</summary>
    public readonly record struct LossRange(double Min, double Max)
    {
        public bool Holds(double share) => share >= Min - 1e-9 && share <= Max + 1e-9;
    }

    public static readonly LossRange WinnerLosses = new(0.05, 0.15);
    public static readonly LossRange LoserLosses = new(0.25, 0.40);

    public const string BattleTurnKind = "battle-turn";
    public const string BattleEndKind = "battle-end";

    public IEnumerable<string> HandledKinds => new[] { BattleTurnKind, BattleEndKind };

    partial void FightBattles(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var regionId in Active(world).Where(a => a.WarId == war.Id).Select(a => a.RegionId).Distinct().OrderBy(id => id).ToList())
            {
                // A host already locked in a long battle fights only that one.
                var here = Active(world).Where(a => a.WarId == war.Id && a.RegionId == regionId && !a.Engaged(ctx.Date)).ToList();
                var attacker = here.Where(a => war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                var defender = here.Where(a => !war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                if (attacker is not null && defender is not null) Battle(ctx, war, attacker, defender, world.Regions[regionId]);
            }
    }

    /// <summary>The most a host's heroes add to its strength, and what a champion's victory before the hosts is worth.</summary>
    public const double MostHeroism = 0.6, ChampionsWin = 1.3, ChampionsMeet = 0.5, ChampionSlain = 0.5;

    /// <summary>
    /// Fighting strength: troops weighted by quality, scaled by the commander's generalship and the heroes riding with
    /// the host (the owner, 2026-10-28: characters weigh in a battle's outcome), morale and supply.
    /// </summary>
    public static double Strength(World world, Army army, Region region)
    {
        var magic = region.LeyPower / 50.0;
        var troops = army.Levies + army.MenAtArms * 3.0 + (army.Knights.Count + army.Mercenaries.Count) * 25 + army.Mages * 30 * magic;
        return troops * Generalship(world, army) * Heroism(world, army) * Readiness(world, army);
    }

    /// <summary>
    /// The commander's hand: from half strength under a fool to half again under a great captain (martial with its
    /// artifacts), bolder if brave, shakier if craven; a demon lord drives its horde harder still.
    /// </summary>
    public static double Generalship(World world, Army army)
    {
        if (!world.Characters.TryGetValue(army.CommanderId, out var c) || !c.IsActive) return 0.75;
        var martial = c.Attributes.Martial + world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).Sum(a => a.Bonus("martial"));
        var hand = 0.5 + Math.Clamp(martial, 0, 25) / 20.0 + (c.Has(Traits.Brave) ? 0.05 : 0) - (c.Has(Traits.Craven) ? 0.1 : 0);
        return Math.Max(0.4, hand) * (SagaSystem.IsDemonLord(world, c) ? 1.25 : 1);
    }

    /// <summary>The heroes riding with a host (its commander and knights) lift it by their own fighting strength, up to 60%.</summary>
    public static double Heroism(World world, Army army) =>
        1 + Math.Min(MostHeroism, Notables(world, army).Sum(id => Encounters.Strength(world, world.Characters[id])) / 150);

    /// <summary>The class of a host's commander, if it has one (a commander whose class counters the other's leads 15% better).</summary>
    public static string? CommanderClass(World world, Army army) =>
        world.Characters.TryGetValue(army.CommanderId, out var c) && c.IsActive ? Classes.Of(world, c) : null;

    private static double Readiness(World world, Army army)
    {
        // Arms by the realm's stocks: short of ore it fights weaker, with horses stronger (sub-project 12).
        var arms = world.Realms.TryGetValue(army.RealmId, out var realm) ? Stocks.Arms(world, realm) : 1;
        // The gods' favour, by prayer or a players' blessing (sub-project 13 N5).
        var favour = realm is not null && PrayerSystem.Favoured(world, realm.Ref) ? PrayerSystem.FavourStrength : 1;
        return (army.Morale / 100 + 0.2) * (0.5 + army.Supply / 200) * arms * favour;
    }

    /// <summary>
    /// Before the hosts close, each side's best champion may step out and fight before them: the victor's side takes
    /// heart (its strength rises by 30%), the vanquished may die (a demon lord is only driven back). Returns the side
    /// whose champion won, if they met.
    /// </summary>
    private static bool? Champions(SimContext ctx, War war, Army attacker, Army defender, Region region)
    {
        var world = ctx.World;
        Character? Best(Army army) => Notables(world, army).Select(id => world.Characters[id])
            .OrderByDescending(c => Encounters.Strength(world, c)).ThenBy(c => c.Id).FirstOrDefault();
        if (Best(attacker) is not { } a || Best(defender) is not { } d || !ctx.Rng.Chance(ChampionsMeet)) return null;
        var (ka, kd) = (Classes.Of(world, a), Classes.Of(world, d));
        var (sa, sd) = (Encounters.Strength(world, a) * Classes.Edge(ka, kd), Encounters.Strength(world, d) * Classes.Edge(kd, ka));
        var attackerWins = ctx.Rng.Chance(sa / (sa + sd));
        var slain = ctx.Rng.Chance(ChampionSlain);
        var (victor, vanquished) = attackerWins ? (a, d) : (d, a);
        if (SagaSystem.IsDemonLord(world, vanquished)) slain = false;
        var clash = ctx.Emit(new EventDraft("ChampionsClashed",
            Actors: new Dictionary<string, EntityRef>
            {
                ["victor"] = victor.Ref,
                ["vanquished"] = vanquished.Ref,
                ["war"] = war.Ref,
                ["victor realm"] = world.Realms[(attackerWins ? attacker : defender).RealmId].Ref,
                ["vanquished realm"] = world.Realms[(attackerWins ? defender : attacker).RealmId].Ref,
            },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = slain ? "slew" : "struck down" },
            Causes: Background(ctx, war), Importance: Fates.Importance(vanquished, slain ? 40 : 32), Tags: new[] { "War", "Characters" }));
        victor.Renown = Math.Min(100, victor.Renown + 8);
        if (slain) Fates.Die(ctx, vanquished, "battle", new[] { new Cause(clash.Id, CauseKind.Trigger, 1.0) }, victor);
        else
        {
            Fates.Injure(vanquished, Fate.Wounded, ctx.Rng);
            vanquished.Renown = Math.Max(0, vanquished.Renown - 4);
        }
        return attackerWins;
    }

    private void Battle(SimContext ctx, War war, Army attacker, Army defender, Region region)
    {
        var world = ctx.World;
        var champions = Champions(ctx, war, attacker, defender, region);
        var (ca, cd) = (CommanderClass(world, attacker), CommanderClass(world, defender));
        var a = Strength(world, attacker, region) * (champions == true ? ChampionsWin : 1) * Classes.Edge(ca, cd);
        var d = Strength(world, defender, region) * (champions == false ? ChampionsWin : 1) * Classes.Edge(cd, ca);
        if (Friendly(world, war, world.Realms[defender.RealmId], region)) d *= 1.2;
        if (region.Biome is Biome.Hills or Biome.Mountain or Biome.Forest or Biome.Swamp or Biome.Jungle) d *= 1.15;
        // The rules' roll is drawn first, as always, so the stream is the same whether or not Claude answers.
        var attackerWins = ctx.Rng.Chance(a / Math.Max(1, a + d));
        var winnerShare = ctx.Rng.NextInt(5, 16) / 100.0;
        var loserShare = ctx.Rng.NextInt(25, 41) / 100.0;
        var plan = _arbiter.PlanBattle(new BattleStage(war.Id, attacker.Id, defender.Id, region.Id, a, d, Notables(world, attacker), Notables(world, defender)))
                   ?? new BattlePlan(attackerWins, 1, [], winnerShare, loserShare, new Dictionary<long, Fate>());
        if (plan.Days <= 1)
        {
            Conclude(ctx, war, attacker, defender, region, plan, 0, plan.Chapters.Count > 0 ? string.Join(" ", plan.Chapters.Select(c => c.Text)) : null);
            return;
        }

        var days = plan.Days;
        var chapters = plan.Chapters.Select(c => c with { Day = Math.Clamp(c.Day, 1, days) }).ToList();
        WorldEvent joined;
        using (ctx.Annotate("account", FightChapter.On(chapters, 1)))
            joined = ctx.Emit(new EventDraft("BattleJoined", Actors: Sides(world, war, attacker, defender), Place: region.Ref,
                Payload: new Dictionary<string, string> { ["days"] = days.ToString(CultureInfo.InvariantCulture) },
                Causes: Background(ctx, war), Importance: 30, Tags: new[] { "War" }));
        attacker.EngagedUntil = defender.EngagedUntil = ctx.Date.AddDays(days - 1).Day;
        Dictionary<string, string> Data(int day) => new()
        {
            ["war"] = war.Id.ToString(CultureInfo.InvariantCulture),
            ["attacker"] = attacker.Id.ToString(CultureInfo.InvariantCulture),
            ["defender"] = defender.Id.ToString(CultureInfo.InvariantCulture),
            ["region"] = region.Id.ToString(CultureInfo.InvariantCulture),
            ["joined"] = joined.Id.ToString(CultureInfo.InvariantCulture),
            ["day"] = day.ToString(CultureInfo.InvariantCulture),
            ["days"] = days.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var day in chapters.Select(c => c.Day).Where(n => n > 1 && n < days).Distinct())
        {
            var data = Data(day);
            data["account"] = FightChapter.On(chapters, day)!;
            ctx.Queue.Schedule(ctx.Date.AddDays(day - 1), BattleTurnKind, data);
        }
        var end = Data(days);
        end["winner"] = plan.AttackerWins ? "attacker" : "defender";
        end["winner losses"] = plan.WinnerLosses.ToString(CultureInfo.InvariantCulture);
        end["loser losses"] = plan.LoserLosses.ToString(CultureInfo.InvariantCulture);
        end["fates"] = string.Join(";", plan.Fates.OrderBy(f => f.Key).Select(f => $"{f.Key.ToString(CultureInfo.InvariantCulture)}:{f.Value}"));
        if (FightChapter.On(chapters, days) is { } last) end["account"] = last;
        ctx.Queue.Schedule(ctx.Date.AddDays(days - 1), BattleEndKind, end);
    }

    /// <summary>A beat of a long battle, or its last day: fought out if both hosts still hold the field and the war goes on.</summary>
    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var world = ctx.World;
        long Id(string key) => long.Parse(action.Data[key], CultureInfo.InvariantCulture);
        var war = world.Wars[Id("war")];
        var region = world.Regions[Id("region")];
        var joined = Id("joined");
        world.Armies.TryGetValue(Id("attacker"), out var attacker);
        world.Armies.TryGetValue(Id("defender"), out var defender);
        bool Holds(Army? army) => army is { Disbanded: false } && army.RegionId == region.Id;
        var fought = !war.Ended && Holds(attacker) && Holds(defender);
        if (action.Kind == BattleTurnKind)
        {
            if (!fought) return;
            using var account = ctx.Annotate("account", action.Data.GetValueOrDefault("account"));
            ctx.Emit(new EventDraft("BattleTurned", Actors: Sides(world, war, attacker!, defender!), Place: region.Ref,
                Payload: new Dictionary<string, string> { ["day"] = action.Data["day"], ["days"] = action.Data["days"] },
                Causes: new[] { new Cause(joined, CauseKind.Trigger, 1.0) }, Importance: 22, Tags: new[] { "War" }));
            return;
        }
        foreach (var army in new[] { attacker, defender }.OfType<Army>()) army.EngagedUntil = 0;
        if (!fought)
        {
            // A host broke up or marched off, or peace came, before the field was won.
            var sides = new Dictionary<string, EntityRef> { ["war"] = war.Ref };
            foreach (var (army, role) in new[] { (attacker, "attacker"), (defender, "defender") })
                if (army is not null) sides[role] = world.Realms[army.RealmId].Ref;
            ctx.Emit(new EventDraft("BattleBrokenOff", Actors: sides, Place: region.Ref,
                Causes: new[] { new Cause(joined, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "War" }));
            return;
        }
        var fates = action.Data["fates"].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Split(':'))
            .ToDictionary(f => long.Parse(f[0], CultureInfo.InvariantCulture), f => Enum.Parse<Fate>(f[1]));
        var plan = new BattlePlan(action.Data["winner"] == "attacker", (int)Id("days"), [],
            double.Parse(action.Data["winner losses"], CultureInfo.InvariantCulture), double.Parse(action.Data["loser losses"], CultureInfo.InvariantCulture), fates);
        Conclude(ctx, war, attacker!, defender!, region, plan, joined, action.Data.GetValueOrDefault("account"));
    }

    /// <summary>The commanders and knights riding with a host, as fates are dealt.</summary>
    private static IReadOnlyList<long> Notables(World world, Army army) =>
        army.Knights.Concat(army.Mercenaries).Append(army.CommanderId).Where(id => id != 0 && world.Characters.TryGetValue(id, out var c) && c.IsActive).ToList();

    private static Dictionary<string, EntityRef> Sides(World world, War war, Army attacker, Army defender)
    {
        var sides = new Dictionary<string, EntityRef>
        {
            ["attacker"] = world.Realms[attacker.RealmId].Ref,
            ["defender"] = world.Realms[defender.RealmId].Ref,
            ["war"] = war.Ref,
        };
        if (world.Characters.TryGetValue(attacker.CommanderId, out var a)) sides["attacker commander"] = a.Ref;
        if (world.Characters.TryGetValue(defender.CommanderId, out var d)) sides["defender commander"] = d.Ref;
        return sides;
    }

    /// <summary>What a battle grows out of: the war's declaration and the war's last fight.</summary>
    private static List<Cause> Background(SimContext ctx, War war)
    {
        var previous = ctx.Log.Involving(war.Ref).LastOrDefault(e => e.Type is "BattleFought" or "SiegeEnded");
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        if (previous is not null) causes.Add(new Cause(previous.Id, CauseKind.Enabler, 0.4));
        return causes;
    }

    /// <summary>The battle's end: losses, morale, fates, the day's hero and the loser's retreat or rout.</summary>
    private void Conclude(SimContext ctx, War war, Army attacker, Army defender, Region region, BattlePlan plan, long joined, string? account)
    {
        var world = ctx.World;
        var attackerWins = plan.AttackerWins;
        var (winner, loser) = attackerWins ? (attacker, defender) : (defender, attacker);
        var winnerLosses = Casualties(winner, plan.WinnerLosses);
        var loserLosses = Casualties(loser, plan.LoserLosses);

        var actors = new Dictionary<string, EntityRef>
        {
            ["victor"] = world.Realms[winner.RealmId].Ref,
            ["vanquished"] = world.Realms[loser.RealmId].Ref,
            ["war"] = war.Ref,
        };
        if (world.Characters.TryGetValue(winner.CommanderId, out var victorious)) actors["victor commander"] = victorious.Ref;
        if (world.Characters.TryGetValue(loser.CommanderId, out var defeated)) actors["vanquished commander"] = defeated.Ref;
        // The knights who rode with each host are named on the battle, so it is part of their lives too.
        foreach (var (army, side) in new[] { (winner, "victor"), (loser, "vanquished") })
        {
            var n = 0;
            foreach (var knight in army.Knights.Where(id => world.Characters.TryGetValue(id, out var k) && k.IsActive).Take(3))
                actors[$"{side} knight {++n}"] = world.Characters[knight].Ref;
        }
        var causes = joined != 0 ? new List<Cause> { new(joined, CauseKind.Trigger, 1.0) } : Background(ctx, war);
        var payload = new Dictionary<string, string>
        {
            ["victor losses"] = winnerLosses.ToString(CultureInfo.InvariantCulture),
            ["vanquished losses"] = loserLosses.ToString(CultureInfo.InvariantCulture),
        };
        if (plan.Days > 1) payload["days"] = plan.Days.ToString(CultureInfo.InvariantCulture);
        WorldEvent battle;
        using (ctx.Annotate("account", account))
            battle = ctx.Emit(new EventDraft("BattleFought", Actors: actors, Place: region.Ref, Payload: payload,
                Causes: causes, Importance: Math.Clamp(30 + (winnerLosses + loserLosses) / 100, 30, 75), Tags: new[] { "War" }));

        war.Battles++;
        var share = (winnerLosses + loserLosses) / (double)Math.Max(1, attacker.Soldiers + defender.Soldiers + winnerLosses + loserLosses);
        war.BattleScore = Math.Clamp(war.BattleScore + (attackerWins ? 1 : -1) * (5 + 20 * share), -60, 60);
        winner.Morale = Math.Min(100, winner.Morale + 10);
        loser.Morale = Math.Max(0, loser.Morale - 25);
        Statecraft.Remember(world.Realms[loser.RealmId], battle.Id, winner.RealmId, -40, 60);

        foreach (var (army, won, foe) in new[] { (winner, true, loser), (loser, false, winner) })
            foreach (var id in army.Knights.Append(army.CommanderId).Where(id => id != 0).ToList())
                if (world.Characters.TryGetValue(id, out var c) && c.IsActive)
                    FaceBattle(ctx, c, won, battle, world.Characters.GetValueOrDefault(foe.CommanderId), plan.Fates.TryGetValue(c.Id, out var f) ? f : null);

        // The day's hero: the most martial of the victors still standing.
        if (winner.Knights.Append(winner.CommanderId).Where(id => world.Characters.TryGetValue(id, out var h) && h.IsActive)
                .Select(id => world.Characters[id]).OrderByDescending(h => h.Attributes.Martial).ThenBy(h => h.Id).FirstOrDefault() is { } hero)
        {
            hero.Renown = Math.Min(100, hero.Renown + 4);
            ctx.Emit(new EventDraft("DistinguishedInBattle", Actors: new Dictionary<string, EntityRef> { ["character"] = hero.Ref, ["war"] = war.Ref },
                Place: region.Ref, Causes: new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) },
                Importance: Fates.Importance(hero, 28), Tags: new[] { "War", "Characters" }));
        }

        if (loser.Morale < 20 || loser.Soldiers < 50)
        {
            ctx.Emit(new EventDraft("ArmyRouted", Actors: new Dictionary<string, EntityRef> { ["army"] = loser.Ref, ["realm"] = world.Realms[loser.RealmId].Ref },
                Place: region.Ref, Causes: new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) }, Importance: 35, Tags: new[] { "War" }));
            Statecraft.Disband(world, loser);
        }
        else
        {
            loser.Objective = ArmyObjective.Return;
            loser.TargetRegionId = 0;
        }
    }

    private static int Casualties(Army army, double share)
    {
        var levies = (int)(army.Levies * share);
        var menAtArms = (int)(army.MenAtArms * share);
        var mages = (int)(army.Mages * share);
        army.Levies -= levies;
        army.MenAtArms -= menAtArms;
        army.Mages -= mages;
        return levies + menAtArms + mages;
    }

    /// <summary>The legal fates of a notable in battle; losers are far likelier to fall.</summary>
    public static Encounter BattleFate(Character c, bool won)
    {
        var (dead, maimed, wounded) = won ? (0.03, 0.03, 0.12) : (0.12, 0.08, 0.2);
        return new Encounter("battle", c.Id, 0, 1, 1, true, new[]
        {
            new EncounterOutcome("unharmed", 1 - dead - maimed - wounded, won, Fate.Unharmed, Fate.Unharmed),
            new EncounterOutcome("wounded", wounded, won, Fate.Wounded, Fate.Unharmed),
            new EncounterOutcome("maimed", maimed, won, Fate.Maimed, Fate.Unharmed),
            new EncounterOutcome("slain", dead, won, Fate.Dead, Fate.Unharmed),
        });
    }

    private void FaceBattle(SimContext ctx, Character c, bool won, WorldEvent battle, Character? enemyCommander, Fate? chosen = null)
    {
        // The rules' roll is drawn even when Claude chose the fate, keeping the stream alike.
        var rolled = _arbiter.Resolve(BattleFate(c, won), ctx.Rng).AttackerFate;
        var fate = chosen ?? rolled;
        // Only the hero foretold, or a sealing, ends a demon lord (sub-project 13): in battle it is at most wounded.
        if (fate == Fate.Dead && SagaSystem.IsDemonLord(ctx.World, c)) fate = Fate.Wounded;
        if (fate == Fate.Dead)
        {
            Fates.Die(ctx, c, "battle", new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) }, enemyCommander is { IsActive: true } ? enemyCommander : null);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
        if (fate is Fate.Wounded or Fate.Maimed)
            ctx.Emit(new EventDraft("WoundedInBattle", Actors: new Dictionary<string, EntityRef> { ["character"] = c.Ref },
                Place: battle.Place, Causes: new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) },
                Payload: new Dictionary<string, string> { ["wound"] = fate == Fate.Maimed ? "maimed" : "wounded" },
                Importance: Fates.Importance(c, fate == Fate.Maimed ? 26 : 20), Tags: new[] { "War", "Characters" }));
        if (!won) return;
        c.Renown = Math.Min(100, c.Renown + 6);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
    }
}

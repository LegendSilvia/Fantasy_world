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
/// break and disband); every commander and knight present faces a fate chosen by the arbiter.
/// </summary>
public sealed partial class MilitarySystem
{
    partial void FightBattles(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended).ToList())
            foreach (var regionId in Active(world).Where(a => a.WarId == war.Id).Select(a => a.RegionId).Distinct().OrderBy(id => id).ToList())
            {
                var here = Active(world).Where(a => a.WarId == war.Id && a.RegionId == regionId).ToList();
                var attacker = here.Where(a => war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                var defender = here.Where(a => !war.IsAttacker(a.RealmId)).OrderByDescending(a => a.Soldiers).ThenBy(a => a.Id).FirstOrDefault();
                if (attacker is not null && defender is not null) Battle(ctx, war, attacker, defender, world.Regions[regionId]);
            }
    }

    /// <summary>Fighting strength: troops weighted by quality, scaled by commander, morale and supply.</summary>
    public static double Strength(World world, Army army, Region region)
    {
        var magic = region.LeyPower / 50.0;
        var troops = army.Levies + army.MenAtArms * 3.0 + army.Knights.Count * 25 + army.Mages * 30 * magic;
        var martial = world.Characters.TryGetValue(army.CommanderId, out var c) ? c.Attributes.Martial : 5;
        return troops * (0.6 + martial / 25.0) * (army.Morale / 100 + 0.2) * (0.5 + army.Supply / 200);
    }

    private void Battle(SimContext ctx, War war, Army attacker, Army defender, Region region)
    {
        var world = ctx.World;
        var a = Strength(world, attacker, region);
        var d = Strength(world, defender, region);
        if (Friendly(world, war, world.Realms[defender.RealmId], region)) d *= 1.2;
        if (region.Biome is Biome.Hills or Biome.Mountain or Biome.Forest or Biome.Swamp or Biome.Jungle) d *= 1.15;
        var attackerWins = ctx.Rng.Chance(a / Math.Max(1, a + d));
        var (winner, loser) = attackerWins ? (attacker, defender) : (defender, attacker);
        var winnerLosses = Casualties(winner, ctx.Rng.NextInt(5, 16) / 100.0);
        var loserLosses = Casualties(loser, ctx.Rng.NextInt(25, 41) / 100.0);

        var actors = new Dictionary<string, EntityRef>
        {
            ["victor"] = world.Realms[winner.RealmId].Ref,
            ["vanquished"] = world.Realms[loser.RealmId].Ref,
            ["war"] = war.Ref,
        };
        if (world.Characters.TryGetValue(winner.CommanderId, out var victorious)) actors["victor commander"] = victorious.Ref;
        if (world.Characters.TryGetValue(loser.CommanderId, out var defeated)) actors["vanquished commander"] = defeated.Ref;
        var previous = ctx.Log.Involving(war.Ref).LastOrDefault(e => e.Type is "BattleFought" or "SiegeEnded");
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        if (previous is not null) causes.Add(new Cause(previous.Id, CauseKind.Enabler, 0.4));
        var battle = ctx.Emit(new EventDraft("BattleFought", Actors: actors, Place: region.Ref,
            Payload: new Dictionary<string, string>
            {
                ["victor losses"] = winnerLosses.ToString(CultureInfo.InvariantCulture),
                ["vanquished losses"] = loserLosses.ToString(CultureInfo.InvariantCulture),
            },
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
                    FaceBattle(ctx, c, won, battle, world.Characters.GetValueOrDefault(foe.CommanderId));

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

    private void FaceBattle(SimContext ctx, Character c, bool won, WorldEvent battle, Character? enemyCommander)
    {
        var fate = _arbiter.Resolve(BattleFate(c, won), ctx.Rng).AttackerFate;
        if (fate == Fate.Dead)
        {
            Fates.Die(ctx, c, "battle", new[] { new Cause(battle.Id, CauseKind.Trigger, 1.0) }, enemyCommander is { IsActive: true } ? enemyCommander : null);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
        if (!won) return;
        c.Renown = Math.Min(100, c.Renown + 6);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
    }
}

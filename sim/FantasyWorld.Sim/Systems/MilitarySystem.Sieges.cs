using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Sieges and occupation (spec §6 steps 5–6). An army that reaches its target besieges the region's chief
/// settlement: undefended places fall at once, walled ones after months of pressure against walls and
/// garrison, and a cruel commander sacks what falls. Defenders who return to occupied land unopposed
/// liberate it. War score is occupation (enemy capitals count more) plus the tally of battles.
/// </summary>
public sealed partial class MilitarySystem
{
    partial void Besiege(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var region in world.Regions.Values.Where(r => r.BesiegerArmyId != 0).ToList())
        {
            var besieger = world.Armies[region.BesiegerArmyId];
            if (besieger.Disbanded || besieger.RegionId != region.Id || besieger.Objective != ArmyObjective.Siege)
            {
                region.BesiegerArmyId = 0;
                region.SiegeProgress = 0;
            }
        }

        foreach (var army in Active(world).Where(a => a.Objective == ArmyObjective.Siege && a.RegionId == a.TargetRegionId).ToList())
        {
            if (army.Disbanded) continue;
            var war = world.Wars[army.WarId];
            var realm = world.Realms[army.RealmId];
            var region = world.Regions[army.RegionId];
            if (!Hostile(world, war, realm, region) || Contested(world, war, army)) continue;
            var chief = world.LivingSettlements(region).OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
            // A fortified town (its level, the owner, 2026-10-30) holds out a tenth longer for each level.
            var defense = chief is null ? 0 : (chief.Walls * 400 + chief.Garrison * 3 + 50 + (StructureSystem.Fortified(world, chief) ? StructureSystem.FortressDefence : 0))
                                              * Levels.Power(Levels.Of(world, chief));
            // A fortified town takes more to break (the owner, 2026-10-30): the host hires mercenaries for it, up to three.
            if (chief is not null) Hire(ctx, war, army, MercenariesFor(Levels.Of(world, chief)), chief.Ref);
            if (defense <= 150)
            {
                Occupy(ctx, war, army, region, chief, "RegionOccupied", null);
                continue;
            }
            if (region.BesiegerArmyId != army.Id)
            {
                region.BesiegerArmyId = army.Id;
                region.SiegeProgress = 0;
                ctx.Emit(new EventDraft("SiegeBegan", Actors: Actors(world, army, war), Place: chief!.Ref,
                    Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.5) }, Importance: 35, Tags: new[] { "War" }));
            }
            region.SiegeProgress = Math.Min(100, region.SiegeProgress + Strength(world, army, region) / defense * 25);
            if (region.SiegeProgress >= 100) Occupy(ctx, war, army, region, chief, "SiegeEnded", chief);
        }

        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0).ToList())
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var liberator = Active(world).Where(a => a.RegionId == region.Id).OrderBy(a => a.Id).FirstOrDefault(a =>
            {
                var w = world.Wars[a.WarId];
                var ours = w.IsAttacker(a.RealmId) ? w.Attackers : w.Defenders;
                var theirs = w.IsAttacker(a.RealmId) ? w.Defenders : w.Attackers;
                return ours.Contains(owner.Id) && theirs.Contains(region.OccupierRealmId);
            });
            if (liberator is null || Contested(world, world.Wars[liberator.WarId], liberator)) continue;
            region.OccupierRealmId = 0;
            ctx.Emit(new EventDraft("RegionLiberated", Actors: Actors(world, liberator, world.Wars[liberator.WarId]), Place: region.Ref,
                Importance: 35, Tags: new[] { "War" }));
        }
    }

    partial void Score(SimContext ctx, War war)
    {
        var world = ctx.World;
        var occupation = 0.0;
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId != 0))
        {
            var owner = Realms.SovereignOf(world, region);
            if (owner is null) continue;
            var weight = world.Realms.Values.Any(r => !r.Destroyed && world.Settlements[r.CapitalId].RegionId == region.Id && r.LiegeId == 0) ? 25 : 10;
            if (war.Attackers.Contains(region.OccupierRealmId) && war.Defenders.Contains(owner.Id)) occupation += weight;
            if (war.Defenders.Contains(region.OccupierRealmId) && war.Attackers.Contains(owner.Id)) occupation -= weight;
        }
        war.Score = Math.Clamp(occupation + war.BattleScore, -100, 100);
    }

    private static void Occupy(SimContext ctx, War war, Army army, Region region, Settlement? chief, string type, Settlement? sacked)
    {
        var world = ctx.World;
        region.OccupierRealmId = army.RealmId;
        region.BesiegerArmyId = 0;
        region.SiegeProgress = 0;
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.5) };
        var began = ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
        if (type == "SiegeEnded" && chief is not null)
        {
            var siege = ctx.Log.Involving(chief.Ref).LastOrDefault(e => e.Type == "SiegeBegan");
            if (siege is not null) causes.Insert(0, new Cause(siege.Id, CauseKind.Trigger, 1.0));
        }
        else if (began is not null) causes.Add(new Cause(began.Id, CauseKind.Enabler, 0.3));
        var fell = ctx.Emit(new EventDraft(type, Actors: Actors(world, army, war), Place: chief?.Ref ?? region.Ref,
            Payload: type == "SiegeEnded" ? new Dictionary<string, string> { ["outcome"] = "fell" } : null,
            Causes: causes, Importance: type == "SiegeEnded" ? 50 : 30, Tags: new[] { "War" }));
        if (chief is not null)
        {
            chief.Garrison = 0;
            chief.Walls = Math.Max(0, chief.Walls - 1);
        }
        Statecraft.Remember(Realms.SovereignOf(world, region)!, fell.Id, army.RealmId, -50, 70);

        if (sacked is not null && world.Characters.TryGetValue(army.CommanderId, out var commander) && commander.Has(Traits.Cruel))
        {
            var dead = Demography.Kill(sacked, 0.1, ctx.Rng);
            sacked.Wealth *= 0.5;
            var sack = ctx.Emit(new EventDraft("SettlementSacked",
                Actors: new Dictionary<string, EntityRef> { ["commander"] = commander.Ref, ["war"] = war.Ref },
                Place: sacked.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                Causes: new[] { new Cause(fell.Id, CauseKind.Trigger, 1.0) }, Importance: 60, Tags: new[] { "War" }));
            Statecraft.Remember(Realms.SovereignOf(world, region)!, sack.Id, army.RealmId, -90, 100);
        }
    }

    /// <summary>An enemy army stands in the same region.</summary>
    private static bool Contested(World world, War war, Army army)
    {
        var enemies = war.EnemiesOf(Side(war, world.Realms[army.RealmId]));
        return Active(world).Any(a => a.WarId == war.Id && a.RegionId == army.RegionId && enemies.Contains(a.RealmId));
    }

    private static Dictionary<string, EntityRef> Actors(World world, Army army, War war)
    {
        var actors = new Dictionary<string, EntityRef> { ["realm"] = world.Realms[army.RealmId].Ref, ["army"] = army.Ref, ["war"] = war.Ref };
        if (world.Characters.TryGetValue(army.CommanderId, out var commander)) actors["commander"] = commander.Ref;
        return actors;
    }
}

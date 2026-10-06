using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Peace (spec §6 step 6). At a war score of ±50 the losing leader weighs terms against fighting on
/// (a decision point); stalemates end in a white peace after five years, and any war after ten. Terms
/// follow the casus belli: independence confirmed or vassalage restored, a stolen artifact returned,
/// or the winner keeps the land it occupies (at least the war's target), and a crushed, much smaller loser becomes its vassal.
/// A defender who wins takes reparations. Then armies go home and occupations end.
/// </summary>
public sealed partial class MilitarySystem
{
    public const double DecisiveScore = 50;

    partial void ConsiderPeace(SimContext ctx, War war)
    {
        var world = ctx.World;
        bool Standing(long id) => world.Realms.TryGetValue(id, out var r) && !r.Destroyed;
        if (!Standing(war.AttackerId) || !war.Attackers.Any(Standing)) { MakePeace(ctx, war, attackerWon: false, white: !Standing(war.DefenderId)); return; }
        if (!Standing(war.DefenderId) || !war.Defenders.Any(Standing)) { MakePeace(ctx, war, attackerWon: true, white: false); return; }

        var years = (ctx.Date.Day - war.StartDay) / (double)Core.WorldDate.DaysPerYear;
        if (Math.Abs(war.Score) >= DecisiveScore)
        {
            var attackerWon = war.Score > 0;
            var loser = world.Realms[attackerWon ? war.DefenderId : war.AttackerId];
            var ruler = Realms.RulerOf(world, loser);
            var accept = ruler is null || _decider.Decide(new DecisionBrief(ruler.Id, "sue for peace", new[]
            {
                new DecisionOption("accept", 30 + Math.Abs(war.Score), "accept the victor's terms"),
                new DecisionOption("fight", Math.Max(5, 100 - Math.Abs(war.Score) - loser.WarWeariness), "fight on"),
            }), ctx.Rng).Key == "accept";
            if (accept) MakePeace(ctx, war, attackerWon, white: false);
        }
        else if (years >= 5 && Math.Abs(war.Score) < 25) MakePeace(ctx, war, attackerWon: false, white: true);
        else if (years >= 10) MakePeace(ctx, war, attackerWon: war.Score > 0, white: Math.Abs(war.Score) < 10);
    }

    public static WorldEvent MakePeace(SimContext ctx, War war, bool attackerWon, bool white)
    {
        var world = ctx.World;
        var winner = world.Realms[attackerWon ? war.AttackerId : war.DefenderId];
        var loser = world.Realms[attackerWon ? war.DefenderId : war.AttackerId];
        var winners = attackerWon ? war.Attackers : war.Defenders;
        var terms = new List<string>();
        var cessions = new List<Region>();
        var vassalise = false;

        if (white) terms.Add("status quo");
        else if (war.CasusBelli == CasusBelli.Independence)
        {
            // The former liege declares this war: winning restores its vassal, losing confirms independence.
            if (attackerWon)
            {
                terms.Add("vassalage restored");
                vassalise = true;
            }
            else terms.Add("independence");
        }
        else
        {
            if (attackerWon && war.CasusBelli == CasusBelli.ArtifactRecovery && world.Artifacts.TryGetValue(war.TargetArtifactId, out var relic)
                && relic.Place != ArtifactPlace.Destroyed)
                terms.Add($"return of {relic.Name}");
            cessions = world.Regions.Values
                .Where(r => winners.Contains(r.OccupierRealmId) && Realms.SovereignOf(world, r) is { } owner
                            && (attackerWon ? war.Defenders : war.Attackers).Contains(owner.Id))
                .OrderBy(r => r.Id).ToList();
            if (attackerWon && cessions.Count == 0 && world.Regions.TryGetValue(war.TargetRegionId, out var target)
                && Realms.SovereignOf(world, target) is { } held && war.Defenders.Contains(held.Id))
                cessions.Add(target);
            if (cessions.Count > 0) terms.Add($"{cessions.Count} region(s) ceded");
            var loserSize = Realms.Territory(world, loser).Count() - cessions.Count;
            if (attackerWon && Math.Abs(war.Score) >= 90 && loserSize > 0 && loserSize * 2 <= Realms.Territory(world, winner).Count())
            {
                terms.Add("vassalage");
                vassalise = true;
            }
            if (!attackerWon || terms.Count == 0)
            {
                // Defenders take reparations; an attacker with nothing else to gain takes tribute.
                var reparations = Math.Max(0, loser.Treasury) * 0.3;
                loser.Treasury -= reparations;
                winner.Treasury += reparations;
                terms.Add(attackerWon ? "tribute" : "reparations");
            }
        }

        var decisive = ctx.Log.Involving(war.Ref).LastOrDefault(e => e.Type is "BattleFought" or "SiegeEnded" or "RegionOccupied");
        var causes = new List<Cause> { new(war.DeclaredEventId, CauseKind.Enabler, 0.6) };
        if (decisive is not null) causes.Insert(0, new Cause(decisive.Id, CauseKind.Trigger, 0.9));
        var peace = ctx.Emit(new EventDraft("PeaceSigned",
            Actors: new Dictionary<string, EntityRef> { ["victor"] = winner.Ref, ["vanquished"] = loser.Ref, ["war"] = war.Ref },
            Place: world.Settlements[loser.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["terms"] = string.Join("; ", terms), ["outcome"] = white ? "white peace" : attackerWon ? "attackers won" : "defenders won" },
            Causes: causes, Importance: white ? 45 : 65, Tags: new[] { "War", "Politics" }));
        war.Ended = true;
        war.EndedEventId = peace.Id;

        foreach (var army in Active(world).Where(a => a.WarId == war.Id).ToList()) Statecraft.Disband(world, army);
        // End this war's occupations (not those of other wars), including land whose sovereign changed mid-war.
        foreach (var region in world.Regions.Values.Where(r => war.Involves(r.OccupierRealmId)
                     && (Realms.SovereignOf(world, r) is not { } owner || war.Involves(owner.Id) || !AtWarWith(world, r.OccupierRealmId, owner.Id))))
            region.OccupierRealmId = 0;

        if (!white && terms.Any(t => t.StartsWith("return of", StringComparison.Ordinal)) && Realms.RulerOf(world, winner) is { } claimant)
            Fates.MoveToHolder(world.Artifacts[war.TargetArtifactId], claimant, peace.Id, "returned");
        foreach (var region in cessions.Where(r => !winner.Destroyed && r.OwnerRealmId != 0))
        {
            var from = world.Realms[region.OwnerRealmId];
            if (from.Destroyed) continue;
            Statecraft.Cede(ctx, region, from, winner, new[] { new Cause(peace.Id, CauseKind.Trigger, 1.0) });
        }
        if (vassalise && !loser.Destroyed && !Realms.IsBelow(world, winner, loser))
        {
            // A vassal has no foreign policy of its own: its treaties lapse.
            loser.LiegeId = winner.Id;
            loser.Treaties.Clear();
            foreach (var other in world.Realms.Values) other.Treaties.RemoveAll(t => t.With == loser.Id);
        }

        if (!white)
        {
            Statecraft.Remember(loser, peace.Id, winner.Id, -60, 90);
            if (Realms.RulerOf(world, winner) is { } victor) victor.Renown = Math.Min(100, victor.Renown + 10);
        }
        return peace;
    }
}

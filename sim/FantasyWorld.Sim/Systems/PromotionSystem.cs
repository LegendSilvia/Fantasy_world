using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Populace promotion (spec §3): when a story needs a person, one steps out of the aggregate
/// population. Empty seats get a new lord (citing the death that emptied them), revolts get a
/// leader, monster-ridden regions get a hero, leaderless villages get an elder.
/// </summary>
public sealed class PromotionSystem : ISimSystem
{
    public const double HeroPerMonth = 0.2;
    public const double ElderPerMonth = 0.05;

    public string Name => "promotion";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var ruin in world.Settlements.Values.Where(s => s.Abandoned))
            Evacuate(ctx, ruin);
        var residents = world.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned).ToList())
        {
            if (s.Tier >= SettlementTier.Town && s.LordId == 0) RaiseLord(ctx, s, residents[s.Id]);
            if (s.Conditions.TryGetValue(Conditions.Revolt, out var revolt) && !residents[s.Id].Any(c => c.Role == Role.Rebel))
                RaiseRebel(ctx, s, revolt);
            if (s.Tier == SettlementTier.Village && !residents[s.Id].Any() && ctx.Rng.Chance(ElderPerMonth))
                Promote(ctx, s, Role.Elder, "ElderChosen", 5, Array.Empty<Cause>());
        }

        foreach (var region in world.Regions.Values)
        {
            if (!region.Conditions.TryGetValue(Conditions.Dangerous, out var danger)) continue;
            var settlements = world.LivingSettlements(region).ToList();
            if (settlements.Count == 0 || settlements.Any(s => residents[s.Id].Any(c => c.Role is Role.Adventurer or Role.Knight)))
                continue;
            if (!ctx.Rng.Chance(HeroPerMonth)) continue;
            var home = settlements.OrderByDescending(s => s.Population).ThenBy(s => s.Id).First();
            var (hero, _) = Promote(ctx, home, Role.Adventurer, "HeroRose", 25, new[] { new Cause(danger, CauseKind.Trigger, 0.9) });
            hero.Ambition = Ambitions.Renown;
        }
    }

    /// <summary>Settlements are abandoned in the settlements' yearly tick; promotion runs after it and evacuates the same day.</summary>
    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var ruin in ctx.World.Settlements.Values.Where(s => s.Abandoned))
            Evacuate(ctx, ruin);
    }

    /// <summary>The ablest adult already living there takes the seat; failing that, someone from the populace.</summary>
    private static void RaiseLord(SimContext ctx, Settlement seat, IEnumerable<Character> residents)
    {
        var world = ctx.World;
        var causes = seat.Conditions.TryGetValue(Fates.VacantSeat, out var death)
            ? new[] { new Cause(death, CauseKind.Trigger, 1.0) }
            : Array.Empty<Cause>();
        var candidate = residents
            .Where(c => c.Stage(ctx.Date) != LifeStage.Child && c.Role is not (Role.Lord or Role.Rebel))
            .OrderByDescending(c => c.Attributes.Stewardship + c.Attributes.Diplomacy + c.Renown / 5).ThenBy(c => c.Id)
            .FirstOrDefault();
        if (candidate is null)
        {
            (candidate, _) = Promote(ctx, seat, Role.Lord, "NewLordRose", Importance(seat), causes);
        }
        else
        {
            candidate.Role = Role.Lord;
            ctx.Emit(new EventDraft("NewLordRose",
                Actors: new Dictionary<string, EntityRef> { ["lord"] = candidate.Ref },
                Place: seat.Ref, Causes: causes, Importance: Importance(seat), Tags: new[] { "Characters", "Politics" }));
        }
        seat.LordId = candidate.Id;
        seat.Conditions.Remove(Fates.VacantSeat);
        world.Houses[candidate.HouseId].SeatSettlementId = seat.Id;
    }

    private static void RaiseRebel(SimContext ctx, Settlement s, long revolt)
    {
        var (rebel, rose) = Promote(ctx, s, Role.Rebel, "RebelLeaderRose", 30, new[] { new Cause(revolt, CauseKind.Trigger, 1.0) });
        rebel.Renown = 10;
        rebel.Ambition = Ambitions.Renown;
        if (s.LordId == 0) return;
        var lord = ctx.World.Characters[s.LordId];
        Fates.Remember(rebel, rose.Id, lord.Id, -50, 60);
        Fates.Remember(lord, rose.Id, rebel.Id, -50, 60);
    }

    /// <summary>A new notable from the settlement's populace (one adult fewer in the aggregate), with a new house.</summary>
    private static (Character Character, WorldEvent Rose) Promote(SimContext ctx, Settlement s, Role role, string eventType, int importance, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        var region = world.Regions[s.RegionId];
        var house = CharacterFactory.NewHouse(world, ctx.Rng, region.CultureId, s.Id);
        var born = ctx.Date.Day - (long)ctx.Rng.NextInt(18, 46) * WorldDate.DaysPerYear - ctx.Rng.NextInt(0, 360);
        var c = CharacterFactory.Create(world, ctx.Rng, s.Id, ctx.Rng.Chance(0.5) ? Sex.Female : Sex.Male, born, house.Id, role);
        if (s.Adults > 0) s.Adults--;
        var rose = ctx.Emit(new EventDraft(eventType,
            Actors: new Dictionary<string, EntityRef> { [role == Role.Lord ? "lord" : "character"] = c.Ref },
            Place: s.Ref, Causes: causes, Importance: importance, Tags: new[] { "Characters" }));
        return (c, rose);
    }

    /// <summary>
    /// Notables leave a settlement that has been abandoned for the largest living settlement in the
    /// region (else a neighbouring region, else anywhere); its lord loses the dead seat.
    /// </summary>
    private static void Evacuate(SimContext ctx, Settlement ruin)
    {
        var world = ctx.World;
        var people = world.ActiveCharacters.Where(c => c.SettlementId == ruin.Id).ToList();
        if (people.Count == 0 && ruin.LordId == 0) return;
        var region = world.Regions[ruin.RegionId];
        var regions = new[] { region.Id }.Concat(world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]).Concat(world.Regions.Values);
        var refuge = regions.Select(r => world.LivingSettlements(r).OrderByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault())
            .FirstOrDefault(s => s is not null);
        if (refuge is null) return;

        var abandonment = ctx.Log.Involving(ruin.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
        if (ruin.LordId != 0)
        {
            world.Characters[ruin.LordId].Role = Role.None;
            ruin.LordId = 0;
        }
        foreach (var c in people) c.SettlementId = refuge.Id;
        ctx.Emit(new EventDraft("NotablesFled",
            Actors: new Dictionary<string, EntityRef> { ["from"] = ruin.Ref },
            Place: refuge.Ref,
            Payload: new Dictionary<string, string> { ["people"] = people.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: abandonment is not null ? new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) } : null,
            Importance: 15, Tags: new[] { "Characters" }));
    }

    private static int Importance(Settlement seat) => seat.Tier == SettlementTier.City ? 45 : 30;
}

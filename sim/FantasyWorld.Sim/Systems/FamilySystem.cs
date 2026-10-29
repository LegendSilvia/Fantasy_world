using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Marriages and births. Unmarried adults (18–50) court partners nearby, a decision point between the
/// best few matches and staying unwed. The couple lives where the culture's lineage says (a lord always
/// stays at their seat). Fertile couples have children, who join the lineage parent's house.
/// </summary>
public sealed class FamilySystem : ISimSystem
{
    public const double CourtshipPerMonth = 0.015;
    public const double BirthPerMonth = 0.025;

    private readonly IDecider _decider;

    public FamilySystem(IDecider? decider = null) => _decider = decider ?? new RulesDecider();

    public string Name => "family";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var singles = world.ActiveCharacters.Where(c => Marriageable(c, ctx.Date) && !SagaSystem.IsDemonLord(world, c)).ToList();
        foreach (var c in singles)
            if (Marriageable(c, ctx.Date) && ctx.Rng.Chance(CourtshipPerMonth * (c.Ambition == Ambitions.Family ? 2 : 1)))
                Court(ctx, c, singles);

        foreach (var mother in world.ActiveCharacters.Where(c => c.Sex == Sex.Female && c.SpouseId != 0).ToList())
        {
            var age = mother.Age(ctx.Date);
            if (age is < 16 or > 45) continue;
            var father = world.Characters[mother.SpouseId];
            if (!father.IsActive) continue;
            var odds = BirthPerMonth * Math.Max(0.2, 1 - mother.Children.Count / 6.0);
            if (ctx.Rng.Chance(odds)) Birth(ctx, mother, father);
        }
    }

    public static bool Marriageable(Character c, WorldDate today) =>
        c.IsActive && c.SpouseId == 0 && c.Age(today) is >= 18 and <= 50;

    private void Court(SimContext ctx, Character suitor, IReadOnlyList<Character> singles)
    {
        var world = ctx.World;
        var home = world.Settlements[suitor.SettlementId];
        var nearby = new HashSet<long> { home.RegionId };
        foreach (var n in world.Geography?.Graph.Neighbours(home.RegionId) ?? Array.Empty<long>()) nearby.Add(n);
        var isLord = suitor.Role == Role.Lord;

        var matches = singles
            .Where(o => o.Sex != suitor.Sex && Marriageable(o, ctx.Date) && !Relations.AreKin(o, suitor))
            .Where(o => !(isLord && o.Role == Role.Lord))
            .Where(o => nearby.Contains(world.Settlements[o.SettlementId].RegionId))
            .Select(o => (Partner: o, Score: Relations.Opinion(suitor, o) + (o.SettlementId == suitor.SettlementId ? 15 : 0)
                                              + (o.Role == Role.Lord ? 10 : 0)))
            .Where(m => m.Score > -20)
            .OrderByDescending(m => m.Score).ThenBy(m => m.Partner.Id)
            .Take(3)
            .ToList();
        if (matches.Count == 0) return;

        var options = matches.Select(m => new DecisionOption(m.Partner.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Max(1, 40 + m.Score), $"marry {m.Partner.Name}"))
            .Append(new DecisionOption("unwed", 15, "stay unwed"))
            .ToList();
        var choice = _decider.Decide(new DecisionBrief(suitor.Id, "courtship", options), ctx.Rng);
        if (choice.Key == "unwed") return;
        using (ctx.Annotate("motive", choice.Reasoning))
            Wed(ctx, suitor, world.Characters[long.Parse(choice.Key, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    public static WorldEvent Wed(SimContext ctx, Character a, Character b)
    {
        var world = ctx.World;
        var (husband, wife) = a.Sex == Sex.Male ? (a, b) : (b, a);
        var (stays, moves) = a.Role == Role.Lord ? (a, b)
            : b.Role == Role.Lord ? (b, a)
            : CharacterFactory.LineageParent(world, husband, wife) == wife ? (wife, husband) : (husband, wife);
        moves.SettlementId = stays.SettlementId;

        var wedding = ctx.Emit(new EventDraft("MarriageWed",
            Actors: new Dictionary<string, EntityRef> { ["spouse"] = stays.Ref, ["partner"] = moves.Ref },
            Place: world.Settlements[stays.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(a, 10), Fates.Importance(b, 10)),
            Tags: new[] { "Characters", "Family" }));
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
        a.MarriageEventId = b.MarriageEventId = wedding.Id;
        Fates.Remember(a, wedding.Id, b.Id, 40, 40);
        Fates.Remember(b, wedding.Id, a.Id, 40, 40);
        return wedding;
    }

    internal static void Birth(SimContext ctx, Character mother, Character father)
    {
        var world = ctx.World;
        var houseId = CharacterFactory.LineageParent(world, father, mother)!.HouseId;
        var child = CharacterFactory.Create(world, ctx.Rng, mother.SettlementId, ctx.Rng.Chance(0.5) ? Sex.Female : Sex.Male,
            ctx.Date.Day, houseId, father: father, mother: mother);
        // Only a birth that matters is news (the owner, 2026-10-29): a ruler's child (a prince or princess; the heir if
        // first-born) is told, a lord's is minor, any other is the chronicle's small print.
        var realm = world.Realms.Values.FirstOrDefault(r => !r.Destroyed && Realms.RulerOf(world, r) is { } ruler && (ruler.Id == father.Id || ruler.Id == mother.Id));
        var lord = world.Settlements.Values.Any(s => s.LordId == father.Id || s.LordId == mother.Id);
        var firstBorn = father.Children.Count(id => id != child.Id) == 0 && mother.Children.Count(id => id != child.Id) == 0;
        var payload = new Dictionary<string, string>();
        if (realm is not null)
        {
            payload["title"] = (realm.Rank is RealmRank.Kingdom or RealmRank.Empire ? (child.Sex == Sex.Female ? "princess" : "prince") : (child.Sex == Sex.Female ? "lady" : "lord"))
                               + (firstBorn ? $", heir to the {Statecraft.Title(realm)}" : $" of the {Statecraft.Title(realm)}");
        }
        var born = ctx.Emit(new EventDraft("CharacterBorn",
            Actors: new Dictionary<string, EntityRef> { ["child"] = child.Ref, ["mother"] = mother.Ref, ["father"] = father.Ref },
            Place: world.Settlements[mother.SettlementId].Ref,
            Payload: payload,
            Causes: mother.MarriageEventId != 0 ? new[] { new Cause(mother.MarriageEventId, CauseKind.Enabler, 0.5) } : null,
            Importance: realm is not null ? (firstBorn ? 45 : 32) : lord ? 12 : 2,
            Tags: new[] { "Characters", "Family" }));
        if (ctx.Rng.Chance(0.01))
            Fates.Die(ctx, mother, "childbirth", new[] { new Cause(born.Id, CauseKind.Trigger, 1.0) });
    }
}

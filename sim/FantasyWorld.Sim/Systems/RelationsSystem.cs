using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Friendships and rivalries. Each month, two notables who share a settlement may meet; how it goes
/// depends on trait compatibility plus chance. A warm meeting makes them friends, a bitter one rivals,
/// each recorded as an event both remember (once: not again while that memory lasts). Memories fade by 20% a year and are forgotten below weight 2.
/// </summary>
public sealed class RelationsSystem : ISimSystem
{
    public const double MeetingPerMonth = 0.15;
    public const double YearlyFade = 0.8;
    public const double ForgetBelow = 2;

    public string Name => "relations";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var adults = world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child).ToList();
        foreach (var group in adults.GroupBy(c => c.SettlementId).OrderBy(g => g.Key))
        {
            var people = group.ToList();
            if (people.Count < 2 || !ctx.Rng.Chance(MeetingPerMonth)) continue;
            var i = ctx.Rng.NextInt(0, people.Count);
            var j = ctx.Rng.NextInt(0, people.Count - 1);
            Meet(ctx, people[i], people[j >= i ? j + 1 : j]);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var c in ctx.World.ActiveCharacters)
        {
            for (var i = c.Memories.Count - 1; i >= 0; i--)
            {
                var faded = c.Memories[i] with { Weight = c.Memories[i].Weight * YearlyFade };
                if (faded.Weight < ForgetBelow) c.Memories.RemoveAt(i);
                else c.Memories[i] = faded;
            }
        }
    }

    private static void Meet(SimContext ctx, Character a, Character b)
    {
        var quality = Traits.Compatibility(a.Traits, b.Traits) + ctx.Rng.NextInt(-30, 31);
        if (a.Has(Traits.Wrathful) || b.Has(Traits.Wrathful)) quality -= 10;
        if (a.Has(Traits.Kind) || b.Has(Traits.Kind)) quality += 5;
        var place = ctx.World.Settlements[a.SettlementId].Ref;
        var actors = new Dictionary<string, EntityRef> { ["a"] = a.Ref, ["b"] = b.Ref };

        if (quality >= 30 && !Shares(a, b, positive: true))
        {
            var ev = ctx.Emit(new EventDraft("FriendshipFormed", Actors: actors, Place: place,
                Importance: Math.Max(Fates.Importance(a, 5), Fates.Importance(b, 5)), Tags: new[] { "Characters" }));
            Fates.Remember(a, ev.Id, b.Id, 50, 60);
            Fates.Remember(b, ev.Id, a.Id, 50, 60);
        }
        else if (quality <= -35 && !Shares(a, b, positive: false))
        {
            var ev = ctx.Emit(new EventDraft("RivalryBegan", Actors: actors, Place: place,
                Payload: new Dictionary<string, string> { ["over"] = Grievance(a, b) },
                Importance: Math.Max(Fates.Importance(a, 10), Fates.Importance(b, 10)), Tags: new[] { "Characters" }));
            Fates.Remember(a, ev.Id, b.Id, -60, 60);
            Fates.Remember(b, ev.Id, a.Id, -60, 60);
        }
    }

    /// <summary>Whether a already remembers a warm (or bitter) moment with b.</summary>
    private static bool Shares(Character a, Character b, bool positive) =>
        a.Memories.Any(m => m.About == b.Id && (positive ? m.Feeling > 0 : m.Feeling < 0));

    /// <summary>A short reason for a rivalry, from the first clashing trait pair.</summary>
    private static string Grievance(Character a, Character b)
    {
        foreach (var trait in a.Traits)
            if (Traits.OppositeOf(trait) is { } opposite && b.Has(opposite))
                return $"{trait} vs {opposite}";
        return "an insult";
    }
}

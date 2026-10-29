using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// A flaw may grow into a virtue over a life (the owner, 2026-10-29: bad traits turn good later, in a condition): each year,
/// one whose life has given them reason (a goal won softens envy, a defeat humbles the arrogant, old age calms the wrathful,
/// a mortal illness opens the greedy hand, a lost child gentles the cruel, a raid on their children's home makes the coward
/// brave) may change, told as news. Rolled from the character itself, never the world's dice.
/// </summary>
public sealed class GrowthSystem : ISimSystem
{
    /// <summary>Once their life gives them reason, the chance each year that they change, in a hundred.</summary>
    public const int ChancePerYear = 30;

    public string Name => "growth";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - Core.WorldDate.DaysPerYear;
        var year = ctx.Log.All.Where(e => e.Date.Day >= since).ToList();
        bool Did(Character c, string type) => year.Any(e => e.Type == type && e.Actors.Values.Contains(c.Ref));
        WorldEvent? Last(Character c, string type) => year.LastOrDefault(e => e.Type == type && e.Actors.Values.Contains(c.Ref));
        foreach (var c in world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child).OrderBy(c => c.Id).ToList())
        {
            var old = c.Stage(ctx.Date) == LifeStage.Elder;
            var grandchildren = c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>().Any(k => k.Children.Count > 0);
            var lostChild = c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()
                .Any(k => k.Died is { } d && d.Day >= since);
            var raided = world.Settlements.TryGetValue(c.SettlementId, out var home)
                         && year.Any(e => e.Type == "MonsterRaid" && e.Place == home.Ref) && c.Children.Count > 0;
            // Flaw, virtue, the reason (the event behind it, if any), and how it is told.
            var turns = new (string Flaw, string Virtue, bool Reason, WorldEvent? Cause, string How)[]
            {
                (Traits.Envious, Traits.Gracious, Did(c, "AmbitionAchieved"), Last(c, "AmbitionAchieved"), "won what they had longed for, and stopped envying others theirs"),
                (Traits.Arrogant, Traits.Humble, c.Has(Traits.Humiliated) || year.Any(e => e.Type == "DuelFought" && e.Actors.Values.Contains(c.Ref) && Lost(e, c)),
                    Last(c, "DuelFought"), "was brought low, and learned humility from it"),
                (Traits.Spiteful, Traits.Forgiving, c.Memories.Any(m => m.Feeling <= -50 && world.Characters.TryGetValue(m.About, out var o) && !o.IsAlive),
                    null, "outlived the one they hated, and let the hatred go"),
                (Traits.Reckless, Traits.Prudent, c.Has(Traits.Maimed) || old, null, c.Has(Traits.Maimed) ? "paid for their rashness in flesh, and grew careful" : "grew careful with the years"),
                (Traits.Wrathful, Traits.Calm, old, null, "found that the years had cooled their anger"),
                (Traits.Greedy, Traits.Generous, Did(c, "FellIll") || grandchildren, Last(c, "FellIll"), "saw how little gold is worth at the end, and opened their hand"),
                (Traits.Cruel, Traits.Kind, lostChild, null, "lost a child, and was gentler ever after"),
                (Traits.Craven, Traits.Brave, raided, year.LastOrDefault(e => e.Type == "MonsterRaid" && home is not null && e.Place == home.Ref),
                    "saw a beast at the gate of their children's home, and found courage"),
                (Traits.Paranoid, Traits.Trusting, c.Companion is { Kind: Companion.Sworn }, null, "found a friend sworn to them, and learned to trust"),
                (Traits.Deceitful, Traits.Honest, Did(c, "TheftFoiled"), Last(c, "TheftFoiled"), "was caught in their deceit, and was honest after"),
            };
            foreach (var (flaw, virtue, reason, cause, how) in turns)
            {
                if (!reason || !c.Has(flaw) || Hash.Percent(c.Id, 60 + flaw.Length, ctx.Date.Year) >= ChancePerYear) continue;
                c.Traits.Remove(flaw);
                if (!c.Traits.Contains(virtue)) c.Traits.Add(virtue);
                c.Traits.Sort(StringComparer.Ordinal);
                ctx.Emit(new EventDraft("TraitGrown", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref },
                    Place: home?.Ref,
                    Payload: new Dictionary<string, string> { ["from"] = flaw, ["to"] = virtue, ["how"] = how },
                    Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Trigger, 0.8) },
                    Importance: Fates.Importance(c, 12), Tags: new[] { "Characters" }));
                break; // one change a year
            }
        }
    }

    private static bool Lost(WorldEvent duel, Character c)
    {
        var challengerWon = duel.Payload.GetValueOrDefault("outcome")?.StartsWith("attacker", StringComparison.Ordinal) ?? true;
        return duel.Actors.TryGetValue(challengerWon ? "defender" : "challenger", out var loser) && loser == c.Ref;
    }
}

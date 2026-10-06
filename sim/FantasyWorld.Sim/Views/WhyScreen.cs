using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Views;

/// <summary>An event as a card: what happened, when, its system, the engine's note on why or how, and whether it is a root.</summary>
public sealed record EventCard(long EventId, int Year, Category Category, LinkedText Title, string? Note, bool IsRoot, bool HasMore);

public sealed record CauseEdge(long From, long To, CauseKind Kind, string Label);

public sealed record Mention(EntityRef Ref, string Name);

/// <summary>
/// The *Why chain* screen (sub-project 2 §3.4): the outcome and its causes as a layered graph (each event once, at its
/// shortest distance from the outcome), edges labelled "because" or "because, and" when an event had several causes,
/// dashed roots, and the people, places and systems involved. Consequences are the "trace further" direction.
/// </summary>
public sealed record WhyScreen(
    EventCard Outcome, IReadOnlyList<IReadOnlyList<EventCard>> Layers, IReadOnlyList<CauseEdge> Edges, string Summary,
    IReadOnlyList<Mention> People, IReadOnlyList<Mention> Places, IReadOnlyList<Category> Systems, IReadOnlyList<EventCard> Consequences,
    LinkedText? Story = null)
{
    public static WhyScreen Build(World world, EventLog log, long eventId, int depth = 4)
    {
        var outcome = log.TryGet(eventId, out var found) ? found : throw new KeyNotFoundException($"No event #{eventId}.");
        var nameOf = Names.Of(world);
        var narrator = new TemplateNarrator(world);
        EventCard Card(WorldEvent ev, bool hasMore) => new(ev.Id, ev.Date.Year, Categories.Of(ev),
            LinkedText.Link(narrator.Sentence(ev), LinkedText.Mentions(ev), nameOf),
            ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("lore"),
            ev.Causes.Count == 0, hasMore);

        var distance = new Dictionary<long, int> { [outcome.Id] = 0 };
        var frontier = new List<WorldEvent> { outcome };
        var edges = new List<CauseEdge>();
        for (var level = 1; level <= depth && frontier.Count > 0; level++)
        {
            var next = new List<WorldEvent>();
            foreach (var ev in frontier)
                foreach (var cause in ev.Causes)
                {
                    if (!log.TryGet(cause.EventId, out var c)) continue;
                    edges.Add(new CauseEdge(c.Id, ev.Id, cause.Kind, ev.Causes.Count > 1 ? "because, and" : "because"));
                    if (distance.TryAdd(c.Id, level)) next.Add(c);
                }
            frontier = next;
        }
        var chain = distance.Keys.Select(log.Get).ToList();
        var layers = distance.Where(kv => kv.Value > 0).GroupBy(kv => kv.Value).OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<EventCard>)g.Select(kv => log.Get(kv.Key)).OrderBy(e => e.Id)
                .Select(e => Card(e, e.Causes.Any(c => !distance.ContainsKey(c.EventId)))).ToList())
            .ToList();

        var roots = chain.Count(e => e.Causes.Count == 0 && e.Id != outcome.Id);
        var years = outcome.Date.Year - chain.Min(e => e.Date.Year);
        var summary = chain.Count == 1
            ? "Nothing recorded led to this."
            : $"Traced back {(years == 0 ? "within the year" : years == 1 ? "one year" : $"{Words(years)} years")}, "
              + (roots == 0 ? "and further than this view goes." : $"through {(roots == 1 ? "one root cause" : $"{Words(roots)} root causes")}.");
        var mentions = chain.SelectMany(LinkedText.Mentions).Distinct().Select(r => new Mention(r, nameOf(r) ?? r.ToString())).ToList();
        var consequences = log.EffectsOf(outcome.Id).Select(log.Get).Select(e => Card(e, false)).ToList();
        return new WhyScreen(Card(outcome, outcome.Causes.Any(c => !distance.ContainsKey(c.EventId))), layers, edges, summary,
            mentions.Where(m => m.Ref.Kind == Character.Kind).ToList(),
            mentions.Where(m => m.Ref.Kind is Settlement.Kind or Region.Kind).ToList(),
            chain.Select(Categories.Of).Distinct().OrderBy(c => c).ToList(), consequences,
            chain.Count > 1 ? LinkedText.Link(narrator.Story(chain), chain.SelectMany(LinkedText.Mentions), nameOf) : null);
    }

    private static readonly string[] Numbers = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    private static string Words(int n) => n < Numbers.Length ? Numbers[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

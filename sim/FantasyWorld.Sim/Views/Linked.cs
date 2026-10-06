using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Views;

/// <summary>A span of text that names an entity, so a client can make it a link.</summary>
public sealed record TextLink(int Start, int Length, EntityRef Entity);

/// <summary>Text with its entity links (every visible name can be followed).</summary>
public sealed record LinkedText(string Text, IReadOnlyList<TextLink> Links)
{
    public static LinkedText Plain(string text) => new(text, Array.Empty<TextLink>());

    /// <summary>
    /// Finds the first mention of each entity's name in the text, longest names first so "Maren Holt of Venn" wins
    /// over "Venn", and never overlapping.
    /// </summary>
    public static LinkedText Link(string text, IEnumerable<EntityRef> entities, Func<EntityRef, string?> nameOf)
    {
        var links = new List<TextLink>();
        foreach (var (entity, name) in entities.Distinct().Select(e => (e, nameOf(e))).Where(x => !string.IsNullOrEmpty(x.Item2))
                     .OrderByDescending(x => x.Item2!.Length).ThenBy(x => x.e.Kind, StringComparer.Ordinal).ThenBy(x => x.e.Id))
        {
            var from = 0;
            while (true)
            {
                var at = text.IndexOf(name!, from, StringComparison.Ordinal);
                // Inside a sentence "The War of the Ash" reads "the War of the Ash".
                if (at < 0 && name!.StartsWith("The ", StringComparison.Ordinal)) at = text.IndexOf("the " + name[4..], from, StringComparison.Ordinal);
                if (at < 0) break;
                if (!links.Any(l => at < l.Start + l.Length && l.Start < at + name!.Length))
                {
                    links.Add(new TextLink(at, name!.Length, entity));
                    break;
                }
                from = at + 1;
            }
        }
        return new LinkedText(text, links.OrderBy(l => l.Start).ToList());
    }

    /// <summary>Whether a linked span reads as the entity's name ("the War of the Ash" for "The War of the Ash").</summary>
    public static bool Reads(string span, string? name) =>
        span == name || name is not null && name.StartsWith("The ", StringComparison.Ordinal) && span == "the " + name[4..];

    /// <summary>All entities an event mentions: its actors and place.</summary>
    public static IEnumerable<EntityRef> Mentions(WorldEvent ev) =>
        ev.Place is { } place ? ev.Actors.Values.Append(place) : ev.Actors.Values;
}

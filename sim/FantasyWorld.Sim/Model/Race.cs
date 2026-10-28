using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// A people of the world (sub-project 13 N2, the owner's choice: original races, named and described at the world's
/// making). Cultures belong to a race, and so do their people. A race has its looks and nature (words for the chronicler
/// and the painter), a temper that leans its realms, gifts that lean its people's talents, a lifespan, the lands it
/// favours, and old friendships and hatreds with the other races.
/// </summary>
public sealed class Race
{
    public const string Kind = "race";

    /// <summary>The tempers a race may have: how its realms lean.</summary>
    public const string Warlike = "warlike", Pious = "pious", Arcane = "arcane", Mercantile = "mercantile", Wild = "wild", Stoic = "stoic";

    public static readonly IReadOnlyList<string> Tempers = [Warlike, Pious, Arcane, Mercantile, Wild, Stoic];

    private SortedDictionary<string, int> _gifts = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; set; } = "";

    /// <summary>How they look, as a painter would be told ("tall and ashen, with horns swept back").</summary>
    public string Looks { get; set; } = "";

    /// <summary>What they are like and believe of themselves, a sentence or two for the chronicler.</summary>
    public string Nature { get; set; } = "";

    public string Temper { get; init; } = Stoic;
    public NameStyle Style { get; init; } = NameStyle.Default;

    /// <summary>How long they live against the common span (1): 0.7 is short-lived, 3 lives three lifetimes.</summary>
    public double Span { get; init; } = 1;

    public List<Biome> Homelands { get; init; } = new();

    /// <summary>Attribute → a lean in their people's talents (+3 … −2).</summary>
    public SortedDictionary<string, int> Gifts
    {
        get => _gifts;
        init => _gifts = Sorted.From(value);
    }

    public List<long> Friends { get; init; } = new();
    public List<long> Foes { get; init; } = new();

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool Hates(long other) => Foes.Contains(other);
    public bool Likes(long other) => Friends.Contains(other);
}

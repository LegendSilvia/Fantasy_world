using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>All simulated state. Grows with characters, realms and armies in Plans 3–4.</summary>
public sealed class World
{
    public required ulong Seed { get; init; }

    /// <summary>The world's own name (shown on every screen); empty for worlds made before it was named.</summary>
    public string Name { get; set; } = "";
    public WorldDate Date { get; set; }
    public DateTimeOffset LastRealUtc { get; set; }
    public long LastSeenEventId { get; set; }

    public const string BroadcastMarker = "broadcast.lastEventId";

    /// <summary>
    /// Where the daily broadcast's audience left off (sub-project 6): separate from <see cref="LastSeenEventId"/>, so the
    /// owner reading a digest never changes what the audience hears. Kept in <see cref="Markers"/>.
    /// </summary>
    public long LastBroadcastEventId
    {
        get => Markers.GetValueOrDefault(BroadcastMarker);
        set => Markers[BroadcastMarker] = value;
    }

    public const string BroadcastDateMarker = "broadcast.date";

    /// <summary>
    /// The broadcast date (yyyy-MM-dd) of the episode the marker was last moved for, or null before the first. Saved with
    /// the world, so the stored world itself says which day's episode it already holds.
    /// </summary>
    public string? LastBroadcastDate
    {
        get => Markers.TryGetValue(BroadcastDateMarker, out var d) ? $"{d / 10000:D4}-{d / 100 % 100:D2}-{d % 100:D2}" : null;
        set
        {
            if (value is null) Markers.Remove(BroadcastDateMarker);
            else Markers[BroadcastDateMarker] = long.Parse(value.Replace("-", ""), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Named scalar state for systems without entities of their own.</summary>
    public SortedDictionary<string, double> Stats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Named references to "currently active" events, e.g. an ongoing shortage.</summary>
    public SortedDictionary<string, long> Markers { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The static map. Null only for bare worlds built in tests.</summary>
    public Geography? Geography { get; set; }

    public IdGen Ids { get; init; } = new();
    public SortedDictionary<long, Region> Regions { get; init; } = new();
    public SortedDictionary<long, Settlement> Settlements { get; init; } = new();
    public SortedDictionary<long, Culture> Cultures { get; init; } = new();
    public SortedDictionary<long, Faith> Faiths { get; init; } = new();
    public SortedDictionary<long, House> Houses { get; init; } = new();
    public SortedDictionary<long, Character> Characters { get; init; } = new();
    public SortedDictionary<long, Artifact> Artifacts { get; init; } = new();
    public SortedDictionary<long, Realm> Realms { get; init; } = new();
    public SortedDictionary<long, War> Wars { get; init; } = new();
    public SortedDictionary<long, Army> Armies { get; init; } = new();
    public SortedDictionary<long, Structure> Structures { get; init; } = new();
    public SortedDictionary<long, TradeRoute> Routes { get; init; } = new();
    public SortedDictionary<long, Monster> Monsters { get; init; } = new();
    public SortedDictionary<long, Continent> Continents { get; init; } = new();
    public SortedDictionary<long, Race> Races { get; init; } = new();
    public SortedDictionary<long, Saga> Sagas { get; init; } = new();
    public SortedDictionary<long, Prayer> Prayers { get; init; } = new();

    /// <summary>The world's legend, once written (<see cref="WorldLegend"/>); null for a world made before it.</summary>
    public WorldLegend? Legend { get; set; }

    /// <summary>The awakened callings the world has seen, as its lore records them (<see cref="Calling"/>), first awakened first.</summary>
    public List<Calling> Callings { get; init; } = new();

    /// <summary>The world's secrets uncovered so far, in the order they came to light (<see cref="Secret"/>).</summary>
    public List<Secret> Secrets { get; init; } = new();

    /// <summary>The gods' favour (sub-project 13 N5): "char#12" or "realm#3" → the last day it lasts.</summary>
    public SortedDictionary<string, long> Favour { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Region → realm at world creation: the baseline that history replays from (map history slider).</summary>
    public SortedDictionary<long, long> FoundingOwners { get; init; } = new();

    /// <summary>Realm → liege at world creation (0 = independent), the baseline for replaying vassalage.</summary>
    public SortedDictionary<long, long> FoundingLieges { get; init; } = new();

    /// <summary>Living notables (not faded), in id order.</summary>
    public IEnumerable<Character> ActiveCharacters => Characters.Values.Where(c => c.IsActive);

    /// <summary>Living notables who live in the settlement, in id order.</summary>
    public IEnumerable<Character> PeopleOf(Settlement settlement) =>
        ActiveCharacters.Where(c => c.SettlementId == settlement.Id);

    /// <summary>The region's settlements that are still inhabited, in id order.</summary>
    public IEnumerable<Settlement> LivingSettlements(Region region) =>
        region.Settlements.Select(id => Settlements[id]).Where(s => !s.Abandoned);
}

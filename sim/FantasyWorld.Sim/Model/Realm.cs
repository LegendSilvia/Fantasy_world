using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Rank follows size: counties hold a few regions, empires many plus kingdom-sized vassals.</summary>
public enum RealmRank { County, Duchy, Kingdom, Empire }

/// <summary>Dominion: a demon lord's realm (sub-project 13), ruled by fear, raising hordes.</summary>
public enum Government { FeudalMonarchy, ElectiveKingdom, Theocracy, Magocracy, MerchantRepublic, Tribal, Dominion }

/// <summary>Who inherits the ruler's seat: eldest child, the court's ablest, or the eldest sibling first.</summary>
public enum SuccessionLaw { Primogeniture, Elective, Seniority }

public enum MagicLaw { Embraced, Licensed, Outlawed }

/// <summary>Lifecycle (spec §6); each change of phase opens a named era.</summary>
public enum RealmPhase { Founded, Expanding, Stable, Declining, Collapsed }

public enum TreatyKind { Alliance, NonAggression, TradePact, Tribute, Rivalry }

/// <summary>A standing agreement with another realm. UntilDay 0 means open-ended.</summary>
public sealed record Treaty(TreatyKind Kind, long With, long SinceEventId, long UntilDay);

/// <summary>A realm-level memory: grudges outlive rulers. About is the other realm.</summary>
public sealed record RealmMemory(long EventId, long About, double Feeling, double Weight);

public static class CouncilSeats
{
    public const string Marshal = "marshal";
    public const string Steward = "steward";
    public const string Spymaster = "spymaster";
    public const string CourtMage = "court mage";
    public const string HighPriest = "high priest";

    public static readonly IReadOnlyList<string> All = new[] { Marshal, Steward, Spymaster, CourtMage, HighPriest };
}

/// <summary>
/// A political realm (spec §6). Its ruler is the lord of its capital settlement (derived, never stored).
/// It holds regions directly (Region.OwnerRealmId) and indirectly through vassal realms.
/// </summary>
public sealed class Realm
{
    public const string Kind = "realm";

    private SortedDictionary<string, long> _council = new(StringComparer.Ordinal);
    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; set; } = "";
    public RealmRank Rank { get; set; }
    public long CapitalId { get; set; }
    public long LiegeId { get; set; }
    public Government Government { get; set; }
    public SuccessionLaw SuccessionLaw { get; set; }
    public MagicLaw MagicLaw { get; set; }
    public double Legitimacy { get; set; }
    public double Stability { get; set; }
    public double Treasury { get; set; }
    public double WarWeariness { get; set; }
    public long CultureId { get; set; }
    public long FaithId { get; set; }
    public string[] Colors { get; init; } = { "grey", "white" };
    public long FoundedEventId { get; init; }
    public RealmPhase Phase { get; set; }

    /// <summary>Directly held regions at the last yearly review (for expanding/declining phases).</summary>
    public int RegionsAtLastReview { get; set; }

    public bool Destroyed { get; set; }

    /// <summary>Seat name → character id.</summary>
    public SortedDictionary<string, long> Council
    {
        get => _council;
        init => _council = Sorted.From(value);
    }

    public List<Treaty> Treaties { get; init; } = new();
    public List<RealmMemory> Memories { get; init; } = new();

    private SortedDictionary<string, double> _stocks = new(StringComparer.Ordinal);

    /// <summary>Active conditions ("legitimacyCollapsed", "short:ore", "revolution", …) mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    /// <summary>
    /// What the realm's domain has yielded and not yet spent (sub-project 12): ore, horses, timber, stone and the rare
    /// kinds, by name; see <see cref="Systems.Stocks"/>.
    /// </summary>
    public SortedDictionary<string, double> Stocks
    {
        get => _stocks;
        init => _stocks = Sorted.From(value);
    }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool Has(TreatyKind kind, long with) => Treaties.Any(t => t.Kind == kind && t.With == with);
}

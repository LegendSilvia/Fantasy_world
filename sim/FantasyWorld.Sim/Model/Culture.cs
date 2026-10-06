using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Which parent's house children join, and where a married couple lives.</summary>
public enum Lineage { Patrilineal, Matrilineal }

public sealed class Culture
{
    public const string Kind = "culture";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public NameStyle Style { get; init; } = NameStyle.Default;
    public Lineage Lineage { get; init; }
    public long HomeRegionId { get; init; }
    public long FaithId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

public sealed class Faith
{
    public const string Kind = "faith";

    /// <summary>Tenet vocabulary. Plans 4–5 give them teeth (holy wars, magic law, decision weights).</summary>
    public static readonly IReadOnlyList<string> TenetPool =
        new[] { "warlike", "pacifist", "arcane", "ascetic", "mercantile", "ancestral", "zealous", "tolerant" };

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Deity { get; init; } = "";
    public List<string> Tenets { get; init; } = new();
    public long HolyRegionId { get; init; }

    /// <summary>0–100: how fiercely the faithful defend and spread it. Rises when the holy land is lost.</summary>
    public double Zeal { get; set; }

    /// <summary>The event that cost the faith its holy land (0 while a realm of the faith, or none, holds it).</summary>
    public long HolySiteLostEventId { get; set; }

    public bool Has(string tenet) => Tenets.Contains(tenet);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

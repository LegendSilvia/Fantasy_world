using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum ArtifactType { Weapon, Armor, Crown, Tome, Relic, Trinket }

/// <summary>Held by a character, kept in a settlement's treasury, lost in a region, or destroyed.</summary>
public enum ArtifactPlace { Held, Treasury, Lost, Destroyed }

/// <summary>
/// A mechanical effect. Kind "attribute" adds Amount to Attribute while held; other kinds are tags
/// later systems read: "ward" (monsters), "truthsight" (intrigue), "harvest" (region food), "legitimacy" (Plan 4), "devotion" (a saint's relics: holds their town to its faith).
/// </summary>
public sealed record ArtifactPower(string Kind, string? Attribute, int Amount);

/// <summary>One change of hands. From/To are character ids (0 = none); How: forged, found, inherited, gifted, stolen, looted, lost.</summary>
public sealed record ProvenanceEntry(long EventId, long From, long To, string How);

/// <summary>A named object with history (spec §4).</summary>
public sealed class Artifact
{
    public const string Kind = "artifact";

    public long Id { get; init; }
    public string Name { get; set; } = "";

    /// <summary>Legend invented for it (Plan 5); empty when none was.</summary>
    public string Lore { get; set; } = "";

    public ArtifactType Type { get; init; }
    public string Material { get; init; } = "";

    /// <summary>The creating event, or 0 if it predates recorded history (prehistory arrives in Plan 6).</summary>
    public long OriginEventId { get; init; }

    public List<ArtifactPower> Powers { get; init; } = new();

    /// <summary>A hidden downside, e.g. "paranoia"; revealed over time.</summary>
    public string? Curse { get; init; }
    public bool CurseRevealed { get; set; }

    /// <summary>0–100. Grows with deeds done while held.</summary>
    public double Fame { get; set; }

    public List<ProvenanceEntry> Provenance { get; init; } = new();

    public ArtifactPlace Place { get; set; }
    public long HolderId { get; set; }
    public long SettlementId { get; set; }
    public long RegionId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>Total bonus this artifact gives to an attribute while held.</summary>
    public int Bonus(string attribute) =>
        Powers.Where(p => p.Kind == "attribute" && p.Attribute == attribute).Sum(p => p.Amount);
}

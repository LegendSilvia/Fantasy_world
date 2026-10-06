using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum StructureKind { Landmark, Fortification, Labyrinth, Ruin, Shrine }

public enum StructureState { Building, Standing, Ruined }

/// <summary>
/// A place-defining work (spec §5 "Structures", minimal hooks expanded in sub-project 3): a landmark raised by a
/// lord over years, a labyrinth that opens in a dangerous, ley-rich region and hides artifacts, or the ruin an
/// abandoned settlement leaves behind.
/// </summary>
public sealed class Structure
{
    public const string Kind = "structure";

    public long Id { get; init; }
    public string Name { get; set; } = "";
    /// <summary>A cleared labyrinth becomes a ruin or a shrine.</summary>
    public StructureKind Type { get; set; }
    public long RegionId { get; init; }

    /// <summary>The settlement it stands in, 0 for the wilds (labyrinths).</summary>
    public long SettlementId { get; init; }

    /// <summary>The character who commissioned it, 0 if none.</summary>
    public long BuilderId { get; init; }

    /// <summary>The character who designs and oversees a great work (0 if none).</summary>
    public long ArchitectId { get; set; }

    /// <summary>Pushed on by an impatient patron: faster, but it may collapse.</summary>
    public bool Rushed { get; init; }

    /// <summary>A labyrinth's levels (3–7, 0 for one opened before levels), the deepest reached, its guardian and its clearing.</summary>
    public int Levels { get; set; }

    public int Depth { get; set; }
    public long GuardianId { get; set; }
    public long ClearedEventId { get; set; }

    /// <summary>The event (war, plague, famine) holding the work up, 0 when it goes on.</summary>
    public long DelayedByEventId { get; set; }

    public long StartedEventId { get; init; }
    public long CompletedEventId { get; set; }

    /// <summary>0–100 while building.</summary>
    public double Progress { get; set; }

    public StructureState State { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

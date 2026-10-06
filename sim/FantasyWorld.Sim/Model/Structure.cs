using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum StructureKind { Landmark, Fortification, Labyrinth, Ruin }

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
    public StructureKind Type { get; init; }
    public long RegionId { get; init; }

    /// <summary>The settlement it stands in, 0 for the wilds (labyrinths).</summary>
    public long SettlementId { get; init; }

    /// <summary>The character who commissioned it, 0 if none.</summary>
    public long BuilderId { get; init; }

    public long StartedEventId { get; init; }
    public long CompletedEventId { get; set; }

    /// <summary>0–100 while building.</summary>
    public double Progress { get; set; }

    public StructureState State { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

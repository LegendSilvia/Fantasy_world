using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

public enum SettlementTier { Hamlet, Village, Town, City }

/// <summary>A place where the populace lives, simulated as aggregate numbers.</summary>
public sealed class Settlement
{
    public const string Kind = "settlement";

    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long RegionId { get; init; }
    public TileCoord Tile { get; init; }
    public SettlementTier Tier { get; set; }

    public int Children { get; set; }
    public int Adults { get; set; }
    public int Elders { get; set; }

    public double Wealth { get; set; }

    /// <summary>Wall level 0–3.</summary>
    public int Walls { get; set; }

    public int Garrison { get; set; }

    /// <summary>0–100. High unrest leads to revolt and emigration.</summary>
    public double Unrest { get; set; }

    public bool Abandoned { get; set; }

    /// <summary>The ruling notable (0 for none). Character.Role is Lord exactly for these.</summary>
    public long LordId { get; set; }

    /// <summary>Building names, kept sorted.</summary>
    public List<string> Buildings { get; init; } = new();

    /// <summary>Active conditions ("revolt") mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    [JsonIgnore] public int Population => Children + Adults + Elders;

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

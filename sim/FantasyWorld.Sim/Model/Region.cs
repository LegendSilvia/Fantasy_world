using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

/// <summary>A resource a region can yield. Yield drifts toward Capacity; extraction pulls it down.</summary>
public sealed class ResourceDeposit
{
    public double Capacity { get; set; }
    public double Yield { get; set; }
}

/// <summary>A province: the unit of movement, food, danger and (from Plan 4) ownership.</summary>
public sealed class Region
{
    public const string Kind = "region";

    private SortedDictionary<string, ResourceDeposit> _resources = new(StringComparer.Ordinal);
    private SortedDictionary<string, long> _conditions = new(StringComparer.Ordinal);

    public long Id { get; init; }
    public string Name { get; init; } = "";

    // Geography — fixed at world generation.
    public int TileCount { get; init; }
    public TileCoord Center { get; init; }
    public Biome Biome { get; init; }
    public ClimateBand Climate { get; init; }
    public bool Coastal { get; init; }
    public bool HasRiver { get; init; }

    /// <summary>Farmable land in tile-equivalents; caps how many farmers can work.</summary>
    public double Farmland { get; init; }

    public double MountainShare { get; init; }

    /// <summary>The culture and faith most of the region's people share (0 for worlds generated before Plan 3).</summary>
    public long CultureId { get; set; }

    /// <summary>The continent the region lies on (its island's nearest continent); 0 in worlds made before continents.</summary>
    public long ContinentId { get; set; }

    public long FaithId { get; set; }

    /// <summary>The realm that holds the region directly (0 = unclaimed).</summary>
    public long OwnerRealmId { get; set; }

    /// <summary>A realm occupying it in war (0 = none). Control is loyal, occupied or contested (see Realms).</summary>
    public long OccupierRealmId { get; set; }

    /// <summary>Progress (0–100) of a siege of the region's chief settlement, and the besieging army.</summary>
    public double SiegeProgress { get; set; }

    public long BesiegerArmyId { get; set; }

    // Living state, 0–100 unless noted.
    public double BaseFertility { get; init; }
    public double Fertility { get; set; }
    public double BaseDanger { get; init; }
    public double Danger { get; set; }
    public double BaseLey { get; init; }
    public double LeyPower { get; set; }

    /// <summary>Food stores shared by the region's settlements, in person-months.</summary>
    public double Food { get; set; }

    public List<long> Settlements { get; init; } = new();

    public SortedDictionary<string, ResourceDeposit> Resources
    {
        get => _resources;
        init => _resources = Sorted.From(value);
    }

    /// <summary>Active conditions ("drought", "shortage", …) mapped to the event that began them.</summary>
    public SortedDictionary<string, long> Conditions
    {
        get => _conditions;
        init => _conditions = Sorted.From(value);
    }

    /// <summary>Defining events ("the burned valley"), newest last. Used for naming and narration.</summary>
    public List<long> Memory { get; init; } = new();

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

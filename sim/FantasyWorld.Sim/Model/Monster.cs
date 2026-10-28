using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

public enum MonsterState { Roaming, Slumbering, Slain, Lairing }

/// <summary>
/// A named beast (sub-project 3 §3): what breed it is, where it lairs, how mighty and how infamous it has grown, and
/// how its story ended. Danger in its region never falls below half its might while it lives.
/// </summary>
public sealed class Monster
{
    public const string Kind = "monster";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Breed { get; init; } = "";
    public long RegionId { get; init; }

    /// <summary>1–100: how hard it is to kill, and how hard it raids.</summary>
    public double Might { get; set; }

    /// <summary>0–100: how widely it is feared; legends lead the digest.</summary>
    public double Notoriety { get; set; }

    public bool Legendary { get; set; }
    public long BornDay { get; init; }
    public MonsterState State { get; set; }
    public long RoseEventId { get; init; }
    public long SlainEventId { get; set; }
    public long SlayerId { get; set; }

    /// <summary>For a labyrinth's guardian (state Lairing): the labyrinth it guards; it never leaves to raid.</summary>
    public long LairStructureId { get; init; }

    /// <summary>What guards a labyrinth's deepest level.</summary>
    public static readonly IReadOnlyList<string> GuardianBreeds = new[] { "minotaur", "lich", "golem", "dragon" };

    [JsonIgnore] public bool Alive => State != MonsterState.Slain;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    /// <summary>What rises from the sea to prey on ships (sub-project 13): sea monsters lair in ports.</summary>
    public static readonly IReadOnlyList<string> SeaBreeds = new[] { "kraken", "leviathan", "sea serpent" };

    /// <summary>The rarer beasts each continent may keep as its own (sub-project 13).</summary>
    public static readonly IReadOnlyList<string> RareBreeds = new[]
    {
        "ash drake", "shadow stalker", "crystal spider", "thunderbird", "bone colossus", "fire salamander", "night hag", "behemoth",
        "storm wyvern", "mire horror", "glass golem", "star-eater",
    };

    [JsonIgnore] public bool AtSea => SeaBreeds.Contains(Breed);

    /// <summary>The breeds that haunt each kind of land.</summary>
    public static IReadOnlyList<string> BreedsOf(Biome biome) => biome switch
    {
        Biome.Mountain => new[] { "wyrm", "troll", "roc" },
        Biome.Hills => new[] { "troll", "ogre", "griffin" },
        Biome.Forest => new[] { "wolf-king", "basilisk", "wild hunt" },
        Biome.Jungle => new[] { "basilisk", "manticore", "serpent" },
        Biome.Swamp => new[] { "hydra", "bog wight", "serpent" },
        Biome.Desert => new[] { "sand wyrm", "manticore", "sphinx" },
        Biome.Tundra or Biome.Taiga => new[] { "frost giant", "wendigo", "white wolf" },
        Biome.Savanna or Biome.Grassland => new[] { "chimera", "griffin", "wild hunt" },
        _ => new[] { "kraken", "serpent" },
    };
}

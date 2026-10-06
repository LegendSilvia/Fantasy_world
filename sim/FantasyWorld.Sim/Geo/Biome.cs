namespace FantasyWorld.Sim.Geo;

/// <summary>Ocean covers all water, including inland seas.</summary>
public enum Biome : byte { Ocean, Tundra, Taiga, Grassland, Forest, Swamp, Desert, Savanna, Jungle, Hills, Mountain }

public enum ClimateBand { Cold, Temperate, Hot }

[Flags]
public enum TileFlags : byte { None = 0, River = 1, Coast = 2 }

/// <summary>Static per-biome numbers used by world generation and the economy.</summary>
public static class BiomeInfo
{
    /// <summary>Cost of crossing one tile, relative to open grassland.</summary>
    public static double MoveCost(Biome b) => b switch
    {
        Biome.Mountain => 5.0,
        Biome.Swamp => 2.5,
        Biome.Hills or Biome.Jungle => 2.0,
        Biome.Forest or Biome.Taiga or Biome.Desert or Biome.Tundra => 1.5,
        _ => 1.0,
    };

    /// <summary>Fraction of a tile that can be farmed.</summary>
    public static double Farmland(Biome b) => b switch
    {
        Biome.Grassland => 1.0,
        Biome.Savanna => 0.8,
        Biome.Forest or Biome.Hills => 0.5,
        Biome.Swamp or Biome.Jungle => 0.4,
        Biome.Taiga => 0.3,
        Biome.Tundra => 0.1,
        Biome.Desert => 0.05,
        _ => 0.0,
    };

    public static double BaseFertility(Biome b) => b switch
    {
        Biome.Grassland => 70,
        Biome.Forest => 55,
        Biome.Savanna => 50,
        Biome.Jungle => 45,
        Biome.Hills => 40,
        Biome.Swamp => 35,
        Biome.Taiga => 30,
        Biome.Tundra or Biome.Mountain => 15,
        Biome.Desert => 10,
        _ => 0,
    };

    public static double BaseDanger(Biome b) => b switch
    {
        Biome.Mountain => 50,
        Biome.Swamp or Biome.Jungle => 45,
        Biome.Tundra => 40,
        Biome.Taiga or Biome.Desert => 35,
        Biome.Forest or Biome.Hills => 30,
        Biome.Savanna => 25,
        _ => 15,
    };
}

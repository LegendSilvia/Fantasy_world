using System.Text.Json.Serialization;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Model;

/// <summary>The static map: terrain tiles, which region owns each tile, and the region graph.</summary>
public sealed class Geography
{
    [JsonConstructor]
    public Geography(TerrainGrid terrain, int[] regionOf, RegionGraph graph)
    {
        if (regionOf.Length != terrain.Count)
            throw new ArgumentException("RegionOf must have one entry per tile.", nameof(regionOf));
        Terrain = terrain;
        RegionOf = regionOf;
        Graph = graph;
    }

    public TerrainGrid Terrain { get; }

    /// <summary>Region id per tile; 0 is water.</summary>
    public int[] RegionOf { get; }

    public RegionGraph Graph { get; }

    public long RegionAt(TileCoord tile) => RegionOf[Terrain.Index(tile.X, tile.Y)];
}

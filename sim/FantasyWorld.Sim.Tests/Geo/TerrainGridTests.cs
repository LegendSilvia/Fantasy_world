using System.Text.Json;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Tests.Geo;

public class TerrainGridTests
{
    [Fact]
    public void IndexAndCoord_RoundTrip()
    {
        var grid = new TerrainGrid(8, 4);
        Assert.Equal(13, grid.Index(5, 1));
        Assert.Equal(new TileCoord(5, 1), grid.Coord(13));
        Assert.Equal(32, grid.Count);
    }

    [Fact]
    public void Neighbours_StayInBounds()
    {
        var grid = new TerrainGrid(4, 4);
        Assert.Equal(new[] { 1, 4 }, grid.Neighbours4(0));
        Assert.Equal(new[] { 1, 4, 5 }, grid.Neighbours8(0));
        Assert.Equal(8, grid.Neighbours8(grid.Index(1, 1)).Count());
    }

    [Fact]
    public void FlagsAndBiomes_AreStoredPerTile()
    {
        var grid = new TerrainGrid(4, 4);
        Assert.False(grid.IsLand(3));
        grid.SetBiome(3, Biome.Forest);
        grid.Set(3, TileFlags.River);
        Assert.True(grid.IsLand(3));
        Assert.True(grid.Has(3, TileFlags.River));
        Assert.False(grid.Has(3, TileFlags.Coast));
    }

    [Fact]
    public void JsonRoundTrip_PreservesChannels()
    {
        var grid = new TerrainGrid(3, 2) { SeaLevel = 100 };
        grid.Elevation[4] = 200;
        grid.SetBiome(4, Biome.Mountain);
        var copy = JsonSerializer.Deserialize<TerrainGrid>(JsonSerializer.Serialize(grid))!;
        Assert.Equal(100, copy.SeaLevel);
        Assert.Equal(grid.Elevation, copy.Elevation);
        Assert.Equal(Biome.Mountain, copy.BiomeAt(4));
    }

    [Fact]
    public void Constructor_RejectsWrongChannelLength() =>
        Assert.Throws<ArgumentException>(() =>
            new TerrainGrid(2, 2, 0, new byte[4], new byte[4], new byte[4], new byte[3], new byte[4]));
}

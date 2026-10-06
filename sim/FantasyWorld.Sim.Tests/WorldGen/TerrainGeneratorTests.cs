using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class TerrainGeneratorTests
{
    private static readonly TerrainOptions Small = new(Width: 96, Height: 96, MinIslandTiles: 40);

    [Fact]
    public void SameSeed_SameTerrain()
    {
        var a = TerrainGenerator.Generate(3, Small);
        var b = TerrainGenerator.Generate(3, Small);
        Assert.Equal(a.Elevation, b.Elevation);
        Assert.Equal(a.Biomes, b.Biomes);
        Assert.Equal(a.Moisture, b.Moisture);
    }

    [Fact]
    public void DifferentSeeds_DifferentTerrain() =>
        Assert.NotEqual(TerrainGenerator.Generate(3, Small).Biomes, TerrainGenerator.Generate(4, Small).Biomes);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void LandFraction_IsNearTarget(ulong seed)
    {
        var grid = TerrainGenerator.Generate(seed, Small);
        var land = Enumerable.Range(0, grid.Count).Count(grid.IsLand) / (double)grid.Count;
        Assert.InRange(land, Small.LandFraction - 0.08, Small.LandFraction + 0.01);
    }

    [Fact]
    public void Border_IsOcean()
    {
        var grid = TerrainGenerator.Generate(5, Small);
        for (var x = 0; x < grid.Width; x++)
        {
            Assert.False(grid.IsLand(grid.Index(x, 0)));
            Assert.False(grid.IsLand(grid.Index(x, grid.Height - 1)));
        }
    }

    [Fact]
    public void LandAndWater_AgreeWithSeaLevel()
    {
        var grid = TerrainGenerator.Generate(5, Small);
        for (var i = 0; i < grid.Count; i++)
            Assert.Equal(grid.Elevation[i] > grid.SeaLevel, grid.IsLand(i));
    }

    [Fact]
    public void Generates_MountainsHillsAndVariedLowlands()
    {
        var grid = TerrainGenerator.Generate(5, new TerrainOptions(Width: 160, Height: 160, MinIslandTiles: 60));
        var biomes = Enumerable.Range(0, grid.Count).Where(grid.IsLand).Select(grid.BiomeAt).ToHashSet();
        Assert.Contains(Biome.Mountain, biomes);
        Assert.Contains(Biome.Hills, biomes);
        Assert.True(biomes.Count >= 7, $"expected varied biomes, got {string.Join(",", biomes)}");
    }

    [Fact]
    public void NoIsland_IsSmallerThanTheMinimum()
    {
        var grid = TerrainGenerator.Generate(8, Small);
        Assert.All(TerrainGenerator.LandComponents(grid), c => Assert.True(c.Count >= Small.MinIslandTiles));
    }

    [Theory]
    [InlineData(0.8, 0.9, Biome.Jungle)]
    [InlineData(0.8, 0.4, Biome.Savanna)]
    [InlineData(0.8, 0.1, Biome.Desert)]
    [InlineData(0.5, 0.2, Biome.Grassland)]
    [InlineData(0.5, 0.5, Biome.Forest)]
    [InlineData(0.5, 0.8, Biome.Swamp)]
    [InlineData(0.1, 0.2, Biome.Tundra)]
    [InlineData(0.1, 0.6, Biome.Taiga)]
    public void Classify_LowlandsByTemperatureAndMoisture(double temperature, double moisture, Biome expected) =>
        Assert.Equal(expected, TerrainGenerator.Classify(120, mountainLevel: 240, hillLevel: 220, temperature, moisture));

    [Fact]
    public void Classify_HighGroundByElevation()
    {
        Assert.Equal(Biome.Mountain, TerrainGenerator.Classify(240, 240, 220, 0.5, 0.5));
        Assert.Equal(Biome.Hills, TerrainGenerator.Classify(225, 240, 220, 0.5, 0.5));
    }
}

using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionProfilerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, List<Region> Regions) Make(ulong seed = 2)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        var regions = RegionProfiler.Profile(grid, layout, seed, new Rng(seed + 1), new IdGen(), new NameGen(new Rng(seed + 2)));
        return (grid, layout, regions);
    }

    [Fact]
    public void OneRegionPerLayoutEntry_WithMatchingIds()
    {
        var (_, layout, regions) = Make();
        Assert.Equal(Enumerable.Range(1, layout.Count).Select(i => (long)i), regions.Select(r => r.Id));
        Assert.All(regions, r => Assert.Equal(layout.Tiles[(int)r.Id].Count, r.TileCount));
    }

    [Fact]
    public void Stats_AreInRange()
    {
        var (_, _, regions) = Make();
        Assert.All(regions, r =>
        {
            Assert.InRange(r.Fertility, 0, 100);
            Assert.InRange(r.Danger, 0, 100);
            Assert.InRange(r.LeyPower, 0, 100);
            Assert.True(r.Farmland >= 0);
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
        });
        Assert.True(regions.Select(r => r.LeyPower).Max() - regions.Select(r => r.LeyPower).Min() > 20, "ley should vary");
    }

    [Fact]
    public void Resources_FollowGeography()
    {
        var (_, _, regions) = Make();
        Assert.All(regions.Where(r => r.Coastal), r => Assert.Contains(ResourceKinds.Fish, r.Resources.Keys));
        Assert.All(regions.Where(r => r.MountainShare >= 0.2), r => Assert.Contains(ResourceKinds.Ore, r.Resources.Keys));
        Assert.All(regions.SelectMany(r => r.Resources.Values), d => Assert.Equal(d.Capacity, d.Yield));
    }

    [Fact]
    public void FlagsMatchTiles()
    {
        var (grid, layout, regions) = Make();
        foreach (var r in regions)
        {
            var tiles = layout.Tiles[(int)r.Id];
            Assert.Equal(tiles.Any(i => grid.Has(i, TileFlags.River)), r.HasRiver);
            Assert.Equal(tiles.Any(i => grid.Has(i, TileFlags.Coast)), r.Coastal);
        }
    }

    [Fact]
    public void Profile_IsDeterministic()
    {
        var a = Make(5).Regions;
        var b = Make(5).Regions;
        Assert.Equal(a.Select(r => (r.Name, r.Danger, r.Resources.Count)), b.Select(r => (r.Name, r.Danger, r.Resources.Count)));
    }
}

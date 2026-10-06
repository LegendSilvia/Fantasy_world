using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionGraphBuilderTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, RegionGraph Graph) Make(ulong seed)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        return (grid, layout, RegionGraphBuilder.Build(grid, layout));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void EveryRegion_IsReachable(ulong seed)
    {
        var (_, layout, graph) = Make(seed);
        Assert.Equal(layout.Count, graph.Reachable(1).Count);
    }

    [Fact]
    public void LandLinks_JoinRegionsThatShareABorder()
    {
        var (grid, layout, graph) = Make(2);
        foreach (var link in graph.Links.Where(l => l.Kind == LinkKind.Land))
        {
            Assert.True(link.BorderLength > 0);
            var touching = layout.Tiles[(int)link.A].Any(i =>
                grid.Neighbours4(i).Any(n => layout.RegionOf[n] == link.B));
            Assert.True(touching, $"regions {link.A} and {link.B} do not touch");
        }
    }

    [Fact]
    public void TravelDays_ArePositive()
    {
        var (_, _, graph) = Make(3);
        Assert.All(graph.Links, l => Assert.True(l.TravelDays > 0));
    }

    [Fact]
    public void Regions_HaveSeveralNeighboursOnAverage()
    {
        var (_, layout, graph) = Make(1);
        var mean = Enumerable.Range(1, layout.Count).Average(r => graph.LinksOf(r).Count);
        Assert.InRange(mean, 3.0, 8.0);
    }

    [Fact]
    public void SeparateIslands_AreJoinedBySeaLane()
    {
        var grid = new TerrainGrid(20, 10) { SeaLevel = 100 };
        var regionOf = new int[grid.Count];
        for (var y = 2; y < 8; y++)
            for (var x = 2; x < 18; x++)
            {
                if (x is >= 8 and < 12) continue;
                var i = grid.Index(x, y);
                grid.Elevation[i] = 120;
                grid.SetBiome(i, Biome.Grassland);
                regionOf[i] = x < 10 ? 1 : 2;
            }
        Hydrology.Apply(grid);

        var graph = RegionGraphBuilder.Build(grid, new RegionLayout(2, regionOf, grid));

        var lane = Assert.Single(graph.Links);
        Assert.Equal(LinkKind.Sea, lane.Kind);
        Assert.Equal((1L, 2L), (lane.A, lane.B));
    }
}

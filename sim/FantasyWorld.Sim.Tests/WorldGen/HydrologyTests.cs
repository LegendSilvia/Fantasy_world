using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class HydrologyTests
{
    private static (TerrainGrid Grid, int[] Downstream) Make(ulong seed = 4)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 96, Height: 96, MinIslandTiles: 40));
        return (grid, Hydrology.Apply(grid));
    }

    [Fact]
    public void EveryLandTile_DrainsToTheSea()
    {
        var (grid, downstream) = Make();
        for (var i = 0; i < grid.Count; i++)
        {
            if (!grid.IsLand(i)) continue;
            var steps = 0;
            var at = i;
            while (grid.IsLand(at))
            {
                at = downstream[at];
                Assert.True(at >= 0, $"tile {i} has no outlet");
                Assert.True(++steps <= grid.Count, $"tile {i} drains in a cycle");
            }
        }
    }

    [Fact]
    public void Rivers_FlowIntoRiversOrTheSea()
    {
        var (grid, downstream) = Make();
        for (var i = 0; i < grid.Count; i++)
            if (grid.Has(i, TileFlags.River))
            {
                var next = downstream[i];
                Assert.True(!grid.IsLand(next) || grid.Has(next, TileFlags.River), $"river at {grid.Coord(i)} ends inland");
            }
    }

    [Fact]
    public void RiverShare_IsNearTarget()
    {
        var (grid, _) = Make();
        var land = Enumerable.Range(0, grid.Count).Count(grid.IsLand);
        var rivers = Enumerable.Range(0, grid.Count).Count(i => grid.Has(i, TileFlags.River));
        Assert.InRange(rivers / (double)land, 0.03, 0.06);
    }

    [Fact]
    public void Coast_IsLandNextToWater()
    {
        var (grid, _) = Make();
        for (var i = 0; i < grid.Count; i++)
            Assert.Equal(grid.IsLand(i) && grid.Neighbours4(i).Any(n => !grid.IsLand(n)), grid.Has(i, TileFlags.Coast));
    }

    [Fact]
    public void Hydrology_IsDeterministic()
    {
        var (a, _) = Make(9);
        var (b, _) = Make(9);
        Assert.Equal(a.Flags, b.Flags);
    }
}

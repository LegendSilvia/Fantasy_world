using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RegionPartitionerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout) Make(ulong seed = 6, int target = 30)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        return (grid, RegionPartitioner.Partition(grid, target, new Rng(seed)));
    }

    [Fact]
    public void EveryLandTile_BelongsToExactlyOneRegion()
    {
        var (grid, layout) = Make();
        for (var i = 0; i < grid.Count; i++)
        {
            if (grid.IsLand(i)) Assert.InRange(layout.RegionOf[i], 1, layout.Count);
            else Assert.Equal(0, layout.RegionOf[i]);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void RegionCount_IsNearTarget(ulong seed)
    {
        var (_, layout) = Make(seed, target: 30);
        Assert.InRange(layout.Count, 27, 34);
    }

    [Fact]
    public void Regions_AreContiguous()
    {
        var (grid, layout) = Make();
        for (var r = 1; r <= layout.Count; r++)
        {
            var tiles = layout.Tiles[r];
            var seen = new HashSet<int> { tiles[0] };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
                foreach (var n in grid.Neighbours4(queue.Dequeue()))
                    if (layout.RegionOf[n] == r && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(tiles.Count, seen.Count);
        }
    }

    [Fact]
    public void Regions_AreReasonablySized()
    {
        var (_, layout) = Make();
        var sizes = Enumerable.Range(1, layout.Count).Select(r => layout.Tiles[r].Count).ToList();
        Assert.True(sizes.Min() * 6 >= sizes.Average(), $"smallest region {sizes.Min()} vs mean {sizes.Average():F0}");
    }

    [Fact]
    public void Centers_LieInsideTheirRegion()
    {
        var (grid, layout) = Make();
        for (var r = 1; r <= layout.Count; r++)
            Assert.Equal(r, layout.RegionOf[grid.Index(layout.Centers[r].X, layout.Centers[r].Y)]);
    }

    [Fact]
    public void Partition_IsDeterministic() =>
        Assert.Equal(Make(3).Layout.RegionOf, Make(3).Layout.RegionOf);
}

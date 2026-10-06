using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Land split into regions. RegionOf[tile] is the region id (1..Count), 0 for water.</summary>
public sealed class RegionLayout
{
    public RegionLayout(int count, int[] regionOf, TerrainGrid grid)
    {
        Count = count;
        RegionOf = regionOf;
        Tiles = new List<int>[count + 1];
        for (var r = 0; r <= count; r++) Tiles[r] = new List<int>();
        for (var i = 0; i < regionOf.Length; i++)
            if (regionOf[i] > 0) Tiles[regionOf[i]].Add(i);
        Centers = new TileCoord[count + 1];
        for (var r = 1; r <= count; r++) Centers[r] = CenterOf(grid, Tiles[r]);
    }

    public int Count { get; }
    public int[] RegionOf { get; }

    /// <summary>Tiles of each region, index 0 unused.</summary>
    public List<int>[] Tiles { get; }

    /// <summary>The region tile nearest each region's centroid (always inside the region), index 0 unused.</summary>
    public TileCoord[] Centers { get; }

    private static TileCoord CenterOf(TerrainGrid grid, List<int> tiles)
    {
        long sx = 0, sy = 0;
        foreach (var i in tiles)
        {
            sx += i % grid.Width;
            sy += i / grid.Width;
        }
        var mean = new TileCoord((int)(sx / tiles.Count), (int)(sy / tiles.Count));
        return tiles.Select(grid.Coord).OrderBy(c => c.DistanceSquared(mean)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
    }
}

/// <summary>
/// Splits land into ~targetRegions contiguous regions. Seeds are spread with minimum spacing, then
/// a multi-source Dijkstra grows them by terrain cost, so mountain ranges tend to become borders.
/// </summary>
public static class RegionPartitioner
{
    public static RegionLayout Partition(TerrainGrid grid, int targetRegions, Rng rng)
    {
        var components = TerrainGenerator.LandComponents(grid);
        var landCount = components.Sum(c => c.Count);
        if (landCount == 0) throw new InvalidOperationException("The world has no land to divide into regions.");

        var seeds = new List<int>();
        var spacing = Math.Sqrt((double)landCount / targetRegions) * 0.8;
        foreach (var component in components)
        {
            var wanted = Math.Max(1, (int)Math.Round((double)component.Count * targetRegions / landCount));
            seeds.AddRange(PickSeeds(grid, component, wanted, spacing, rng));
        }
        return new RegionLayout(seeds.Count, Grow(grid, seeds), grid);
    }

    private static List<int> PickSeeds(TerrainGrid grid, List<int> component, int wanted, double spacing, Rng rng)
    {
        var candidates = component.Where(i => grid.BiomeAt(i) != Biome.Mountain).ToList();
        if (candidates.Count == 0) candidates = component.ToList();
        for (var k = candidates.Count - 1; k > 0; k--)
        {
            var j = rng.NextInt(0, k + 1);
            (candidates[k], candidates[j]) = (candidates[j], candidates[k]);
        }

        var chosen = new List<int>();
        for (var gap = spacing; chosen.Count < wanted && gap >= 1; gap *= 0.85)
            foreach (var tile in candidates)
            {
                if (chosen.Count >= wanted) break;
                var c = grid.Coord(tile);
                if (chosen.All(s => grid.Coord(s).DistanceSquared(c) >= gap * gap)) chosen.Add(tile);
            }
        return chosen;
    }

    private static int[] Grow(TerrainGrid grid, List<int> seeds)
    {
        var regionOf = new int[grid.Count];
        var cost = new double[grid.Count];
        Array.Fill(cost, double.MaxValue);
        var queue = new PriorityQueue<int, (double Cost, long Seq)>();
        long seq = 0;
        for (var r = 0; r < seeds.Count; r++)
        {
            regionOf[seeds[r]] = r + 1;
            cost[seeds[r]] = 0;
            queue.Enqueue(seeds[r], (0, seq++));
        }

        while (queue.TryDequeue(out var tile, out var priority))
        {
            if (priority.Cost > cost[tile]) continue;
            foreach (var next in grid.Neighbours4(tile))
            {
                if (!grid.IsLand(next)) continue;
                var step = (BiomeInfo.MoveCost(grid.BiomeAt(tile)) + BiomeInfo.MoveCost(grid.BiomeAt(next))) / 2;
                var total = cost[tile] + step;
                if (total >= cost[next]) continue;
                cost[next] = total;
                regionOf[next] = regionOf[tile];
                queue.Enqueue(next, (total, seq++));
            }
        }
        return regionOf;
    }
}

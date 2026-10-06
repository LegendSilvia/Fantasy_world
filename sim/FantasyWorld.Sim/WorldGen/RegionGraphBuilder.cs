using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Builds land links from shared borders, then sea lanes until every region is reachable.</summary>
public static class RegionGraphBuilder
{
    public const double DaysPerTile = 0.5;
    public const double SeaDaysPerTile = 0.25;

    public static RegionGraph Build(TerrainGrid grid, RegionLayout layout)
    {
        var borders = new SortedDictionary<(int A, int B), (int Length, int River, int Mountain)>();
        for (var i = 0; i < grid.Count; i++)
        {
            var a = layout.RegionOf[i];
            if (a == 0) continue;
            var x = i % grid.Width;
            foreach (var j in new[] { x + 1 < grid.Width ? i + 1 : -1, i + grid.Width < grid.Count ? i + grid.Width : -1 })
            {
                if (j < 0) continue;
                var b = layout.RegionOf[j];
                if (b == 0 || b == a) continue;
                var key = (Math.Min(a, b), Math.Max(a, b));
                var (length, river, mountain) = borders.GetValueOrDefault(key);
                var isRiver = grid.Has(i, TileFlags.River) || grid.Has(j, TileFlags.River);
                var isMountain = grid.BiomeAt(i) == Biome.Mountain && grid.BiomeAt(j) == Biome.Mountain;
                borders[key] = (length + 1, river + (isRiver ? 1 : 0), mountain + (isMountain ? 1 : 0));
            }
        }

        var links = new List<RegionLink>();
        foreach (var ((a, b), (length, river, mountain)) in borders)
        {
            var pass = mountain * 2 >= length;
            var terrain = (BiomeInfo.MoveCost(CenterBiome(grid, layout, a)) + BiomeInfo.MoveCost(CenterBiome(grid, layout, b))) / 2;
            var days = Distance(layout, a, b) * DaysPerTile * terrain * (pass ? 1.5 : 1.0) + (river > 0 ? 1 : 0);
            links.Add(new RegionLink(a, b, LinkKind.Land, Math.Round(days, 2), length, river > 0, pass));
        }
        AddSeaLanes(grid, layout, links);
        return new RegionGraph(links);
    }

    private static void AddSeaLanes(TerrainGrid grid, RegionLayout layout, List<RegionLink> links)
    {
        var coastal = Enumerable.Range(1, layout.Count)
            .Where(r => layout.Tiles[r].Any(i => grid.Has(i, TileFlags.Coast)))
            .ToList();
        while (true)
        {
            var connected = new RegionGraph(links).Reachable(1);
            if (connected.Count == layout.Count) return;
            var outside = coastal.Where(r => !connected.Contains(r)).ToList();
            var inside = coastal.Where(r => connected.Contains(r)).ToList();
            if (outside.Count == 0 || inside.Count == 0)
                throw new InvalidOperationException("Cannot connect regions: an isolated region has no coast.");
            var (a, b) = outside.SelectMany(o => inside.Select(i => (o, i)))
                .OrderBy(p => Distance(layout, p.o, p.i)).ThenBy(p => p.o).ThenBy(p => p.i).First();
            links.Add(new RegionLink(Math.Min(a, b), Math.Max(a, b), LinkKind.Sea,
                Math.Round(Distance(layout, a, b) * SeaDaysPerTile + 2, 2), 0, false, false));
        }
    }

    private static Biome CenterBiome(TerrainGrid grid, RegionLayout layout, int region) =>
        grid.BiomeAt(grid.Index(layout.Centers[region].X, layout.Centers[region].Y));

    private static double Distance(RegionLayout layout, int a, int b) =>
        Math.Sqrt(layout.Centers[a].DistanceSquared(layout.Centers[b]));
}

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The roads armies take (sub-project 10 §3.2): a step to a neighbouring region costs its travel days, more the more of
/// the straight way between the two region centres runs over mountains, and a crossing that is mostly mountain with no
/// low ground anywhere on the shared border (no pass) is closed to armies. Only the stored terrain is read, so trade,
/// migration and every other mover keep the plain region graph.
/// </summary>
public static class Campaigns
{
    /// <summary>A crossing whose straight way is at least this share of mountain needs a pass.</summary>
    public const double PassNeeded = 0.6;

    /// <summary>Each share of mountain on the way multiplies the days by 1 + this.</summary>
    public const double MountainToll = 4;

    private sealed class Terrain(Dictionary<(long, long), int> lowPairs)
    {
        public Dictionary<(long, long), int> LowPairs { get; } = lowPairs;
        public ConcurrentDictionary<(long, long), double> Shares { get; } = new();
    }

    private static readonly ConditionalWeakTable<Geography, Terrain> Cache = new();

    /// <summary>Days for an army to step from one region to a neighbour, or null when the way is closed to armies.</summary>
    public static double? StepDays(World world, RegionLink link, long from)
    {
        if (link.Kind == LinkKind.Sea || world.Geography is not { } geography) return link.TravelDays;
        var to = link.Other(from);
        var share = MountainShare(world, from, to);
        if (share >= PassNeeded && !HasPass(geography, from, to)) return null;
        return link.TravelDays * (1 + MountainToll * share);
    }

    /// <summary>The share of mountain tiles on the straight way between two region centres (0–1).</summary>
    public static double MountainShare(World world, long a, long b)
    {
        var geography = world.Geography!;
        var terrain = TerrainOf(geography);
        var key = (Math.Min(a, b), Math.Max(a, b));
        if (terrain.Shares.TryGetValue(key, out var known)) return known;
        var grid = geography.Terrain;
        var (from, to) = (world.Regions[key.Item1].Center, world.Regions[key.Item2].Center);
        var samples = Math.Max(2, Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) * 2);
        var mountain = 0;
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var x = (int)Math.Round(from.X + (to.X - from.X) * t);
            var y = (int)Math.Round(from.Y + (to.Y - from.Y) * t);
            if (grid.InBounds(x, y) && grid.BiomeAt(grid.Index(x, y)) == Biome.Mountain) mountain++;
        }
        return terrain.Shares[key] = mountain / (double)(samples + 1);
    }

    /// <summary>The shared border has low ground: two touching tiles, one in each region, neither of them mountain.</summary>
    public static bool HasPass(Geography geography, long a, long b) =>
        TerrainOf(geography).LowPairs.GetValueOrDefault((Math.Min(a, b), Math.Max(a, b))) > 0;

    /// <summary>The cheapest army road between two regions, step by step (the start first), or null when none is open.</summary>
    public static (IReadOnlyList<long> Path, double Days)? Road(World world, long from, long to)
    {
        var (days, previous) = Roads(world, from);
        if (!days.TryGetValue(to, out var total)) return null;
        var path = new List<long> { to };
        while (path[^1] != from) path.Add(previous[path[^1]]);
        path.Reverse();
        return (path, total);
    }

    /// <summary>Army road days from one region to every region it can reach, with each one's previous step.</summary>
    public static (IReadOnlyDictionary<long, double> Days, IReadOnlyDictionary<long, long> Previous) Roads(World world, long from)
    {
        var graph = world.Geography!.Graph;
        var days = new Dictionary<long, double> { [from] = 0 };
        var previous = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Id)>();
        queue.Enqueue(from, (0, from));
        var done = new HashSet<long>();
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (!done.Add(region)) continue;
            foreach (var link in graph.LinksOf(region))
            {
                var next = link.Other(region);
                if (done.Contains(next) || StepDays(world, link, region) is not { } step) continue;
                var total = priority.Days + step;
                if (days.TryGetValue(next, out var best) && best <= total) continue;
                days[next] = total;
                previous[next] = region;
                queue.Enqueue(next, (total, next));
            }
        }
        return (days, previous);
    }

    private static Terrain TerrainOf(Geography geography) => Cache.GetValue(geography, g =>
    {
        var grid = g.Terrain;
        var low = new Dictionary<(long, long), int>();
        for (var i = 0; i < grid.Count; i++)
        {
            var a = g.RegionOf[i];
            if (a == 0 || grid.BiomeAt(i) == Biome.Mountain) continue;
            var x = i % grid.Width;
            foreach (var j in new[] { x + 1 < grid.Width ? i + 1 : -1, i + grid.Width < grid.Count ? i + grid.Width : -1 })
            {
                if (j < 0) continue;
                var b = g.RegionOf[j];
                if (b == 0 || b == a || grid.BiomeAt(j) == Biome.Mountain) continue;
                var key = ((long)Math.Min(a, b), (long)Math.Max(a, b));
                low[key] = low.GetValueOrDefault(key) + 1;
            }
        }
        return new Terrain(low);
    });
}

using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Drainage and rivers. A priority flood from the sea gives every land tile a downstream neighbour,
/// so all water reaches the sea (depressions are filled, never trapped). Flow accumulates
/// downstream; the wettest channels become rivers.
/// </summary>
public static class Hydrology
{
    /// <summary>Marks River and Coast flags. Returns each tile's downstream tile (-1 for water).</summary>
    public static int[] Apply(TerrainGrid grid, double riverFraction = 0.04)
    {
        var downstream = Drainage(grid, out var order);
        MarkRivers(grid, downstream, order, riverFraction);
        MarkCoasts(grid);
        return downstream;
    }

    /// <summary>Priority flood. order lists land tiles from the sea inland (upstream last).</summary>
    public static int[] Drainage(TerrainGrid grid, out List<int> order)
    {
        var downstream = new int[grid.Count];
        Array.Fill(downstream, -1);
        var filled = new int[grid.Count];
        var visited = new bool[grid.Count];
        var queue = new PriorityQueue<int, (int Level, long Seq)>();
        long seq = 0;
        order = new List<int>();

        for (var i = 0; i < grid.Count; i++)
            if (!grid.IsLand(i))
            {
                visited[i] = true;
                filled[i] = grid.Elevation[i];
                queue.Enqueue(i, (filled[i], seq++));
            }

        while (queue.TryDequeue(out var current, out _))
            foreach (var next in grid.Neighbours8(current))
            {
                if (visited[next]) continue;
                visited[next] = true;
                downstream[next] = current;
                filled[next] = Math.Max(grid.Elevation[next], filled[current]);
                order.Add(next);
                queue.Enqueue(next, (filled[next], seq++));
            }
        return downstream;
    }

    private static void MarkRivers(TerrainGrid grid, int[] downstream, List<int> order, double riverFraction)
    {
        if (order.Count == 0) return;
        var flow = new double[grid.Count];
        foreach (var i in order) flow[i] = 0.1 + grid.Moisture[i] / 255.0;
        for (var k = order.Count - 1; k >= 0; k--)
        {
            var i = order[k];
            if (grid.IsLand(downstream[i])) flow[downstream[i]] += flow[i];
        }

        var sorted = order.Select(i => flow[i]).OrderBy(f => f).ToArray();
        var threshold = sorted[Math.Clamp((int)((1 - riverFraction) * sorted.Length), 0, sorted.Length - 1)];
        foreach (var i in order)
            if (flow[i] >= threshold) grid.Set(i, TileFlags.River);
    }

    private static void MarkCoasts(TerrainGrid grid)
    {
        for (var i = 0; i < grid.Count; i++)
            if (grid.IsLand(i) && grid.Neighbours4(i).Any(n => !grid.IsLand(n)))
                grid.Set(i, TileFlags.Coast);
    }
}

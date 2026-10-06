using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Model;

public enum LinkKind { Land, Sea }

/// <summary>An undirected connection between two regions (A &lt; B). TravelDays is the cost used by every mover.</summary>
public sealed record RegionLink(
    long A,
    long B,
    LinkKind Kind,
    double TravelDays,
    int BorderLength,
    bool RiverCrossing,
    bool MountainPass)
{
    public long Other(long region) => region == A ? B : A;
}

/// <summary>The adjacency graph all movement uses: armies, trade, migration, plague, monsters.</summary>
public sealed class RegionGraph
{
    private static readonly IReadOnlyList<RegionLink> NoLinks = Array.Empty<RegionLink>();
    private readonly Dictionary<long, List<RegionLink>> _byRegion = new();

    [JsonConstructor]
    public RegionGraph(IReadOnlyList<RegionLink> links)
    {
        Links = links.OrderBy(l => l.A).ThenBy(l => l.B).ToList();
        foreach (var link in Links)
        {
            if (link.A >= link.B) throw new ArgumentException($"Link {link.A}-{link.B} must have A < B.", nameof(links));
            Add(link.A, link);
            Add(link.B, link);
        }
    }

    public IReadOnlyList<RegionLink> Links { get; }

    public IReadOnlyList<RegionLink> LinksOf(long region) =>
        _byRegion.TryGetValue(region, out var list) ? list : NoLinks;

    public IEnumerable<long> Neighbours(long region) => LinksOf(region).Select(l => l.Other(region));

    public RegionLink? Between(long a, long b) => LinksOf(a).FirstOrDefault(l => l.Other(a) == b);

    /// <summary>Cheapest route by travel days (Dijkstra), or null when unreachable.</summary>
    public (IReadOnlyList<long> Path, double Days)? ShortestPath(long from, long to)
    {
        var best = new Dictionary<long, double> { [from] = 0 };
        var previous = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Region)>();
        queue.Enqueue(from, (0, from));
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (priority.Days > best[region]) continue;
            if (region == to)
            {
                var path = new List<long> { to };
                while (path[^1] != from) path.Add(previous[path[^1]]);
                path.Reverse();
                return (path, priority.Days);
            }
            foreach (var link in LinksOf(region))
            {
                var next = link.Other(region);
                var days = priority.Days + link.TravelDays;
                if (best.TryGetValue(next, out var known) && known <= days) continue;
                best[next] = days;
                previous[next] = region;
                queue.Enqueue(next, (days, next));
            }
        }
        return null;
    }

    /// <summary>Regions reachable from start, including start.</summary>
    public IReadOnlySet<long> Reachable(long start)
    {
        var seen = new HashSet<long> { start };
        var queue = new Queue<long>(seen);
        while (queue.Count > 0)
            foreach (var next in Neighbours(queue.Dequeue()))
                if (seen.Add(next)) queue.Enqueue(next);
        return seen;
    }

    private void Add(long region, RegionLink link)
    {
        if (!_byRegion.TryGetValue(region, out var list)) _byRegion[region] = list = new List<RegionLink>();
        list.Add(link);
    }
}

using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Spreads cultures over the region graph from widely spaced homelands (each region joins the
/// nearest homeland by travel days), then groups neighbouring cultures under shared faiths.
/// </summary>
public static class CultureGenerator
{
    public static (List<Culture> Cultures, List<Faith> Faiths) Generate(
        IReadOnlyList<Region> regions, RegionGraph graph, Rng rng, IdGen ids)
    {
        var regionIds = regions.Select(r => r.Id).ToList();
        var cultureCount = Math.Clamp(regions.Count / 14, 3, 9);
        var homes = SpreadOut(graph, regionIds, cultureCount, rng);
        var (_, nearestHome) = Distances(graph, homes);

        var cultures = new List<Culture>();
        foreach (var home in homes)
        {
            var style = NameStyle.Derive(rng);
            cultures.Add(new Culture
            {
                Id = ids.Take(Culture.Kind),
                Name = new NameGen(rng, style).Proper(),
                Style = style,
                Lineage = rng.Chance(0.25) ? Lineage.Matrilineal : Lineage.Patrilineal,
                HomeRegionId = home,
            });
        }

        var faithCount = Math.Clamp((cultures.Count + 1) / 2, 2, 5);
        var holy = SpreadOut(graph, homes, faithCount, rng);
        var (_, nearestHoly) = Distances(graph, holy);
        var names = new NameGen(rng);
        var faiths = new List<Faith>();
        foreach (var site in holy)
        {
            var deity = names.Proper();
            var tenets = Faith.TenetPool.OrderBy(_ => rng.NextULong()).Take(2).OrderBy(t => t, StringComparer.Ordinal).ToList();
            faiths.Add(new Faith
            {
                Id = ids.Take(Faith.Kind),
                Name = rng.Chance(0.5) ? $"The Way of {deity}" : $"The {names.Proper()} Covenant",
                Deity = deity,
                Tenets = tenets,
                HolyRegionId = site,
            });
        }

        foreach (var culture in cultures)
            culture.FaithId = faiths[holy.IndexOf(nearestHoly[culture.HomeRegionId])].Id;
        foreach (var region in regions)
        {
            var culture = cultures[homes.IndexOf(nearestHome[region.Id])];
            region.CultureId = culture.Id;
            region.FaithId = culture.FaithId;
        }
        return (cultures, faiths);
    }

    /// <summary>Farthest-point sampling: a random start, then repeatedly the candidate farthest from all chosen.</summary>
    private static List<long> SpreadOut(RegionGraph graph, IReadOnlyList<long> candidates, int count, Rng rng)
    {
        var chosen = new List<long> { candidates[rng.NextInt(0, candidates.Count)] };
        while (chosen.Count < Math.Min(count, candidates.Count))
        {
            var (distance, _) = Distances(graph, chosen);
            chosen.Add(candidates.Where(c => !chosen.Contains(c))
                .OrderByDescending(c => distance.GetValueOrDefault(c, double.MaxValue)).ThenBy(c => c).First());
        }
        return chosen;
    }

    /// <summary>Multi-source Dijkstra over travel days: distance to, and identity of, the nearest source.</summary>
    private static (Dictionary<long, double> Distance, Dictionary<long, long> Nearest) Distances(
        RegionGraph graph, IReadOnlyList<long> sources)
    {
        var distance = new Dictionary<long, double>();
        var nearest = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Region)>();
        foreach (var s in sources)
        {
            distance[s] = 0;
            nearest[s] = s;
            queue.Enqueue(s, (0, s));
        }
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (priority.Days > distance[region]) continue;
            foreach (var link in graph.LinksOf(region))
            {
                var next = link.Other(region);
                var days = priority.Days + link.TravelDays;
                if (distance.TryGetValue(next, out var known) && known <= days) continue;
                distance[next] = days;
                nearest[next] = nearest[region];
                queue.Enqueue(next, (days, next));
            }
        }
        return (distance, nearest);
    }
}

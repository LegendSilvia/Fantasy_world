using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>A trade road on the block: its line over the ground and whether it is open.</summary>
public sealed record Road(EntityRef Ref, string Name, bool Open, IReadOnlyList<Vector3> Line, bool Sea = false);

/// <summary>
/// Trade roads laid on the diorama (the map's *Trade routes* layer) as one network: every stretch of road between two
/// places is drawn once, however many routes travel it, so routes sharing a way do not wobble side by side. Stretches are
/// joined into roads between junctions (and market ends), each smoothed into a gentle curve whose ends stay put, so roads
/// meet cleanly where they fork, then laid over the ground point by point so they follow the land. A stretch is open
/// while any route on it is.
/// </summary>
public static class RouteLayout
{
    public const float Step = 0.008f;
    public const float Lift = 0.004f;

    public static IReadOnlyList<Road> Roads(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        // The network: each stretch between two consecutive places of any route, once, open if any route on it is open.
        static (long, long) Key(MapPoint p) => ((long)Math.Round(p.X * 100_000), (long)Math.Round(p.Y * 100_000));
        var at = new Dictionary<(long, long), MapPoint>();
        var stretches = new Dictionary<((long, long) A, (long, long) B), (bool Open, MapRoute Route, bool Sea)>();
        foreach (var route in map.Routes ?? Array.Empty<MapRoute>())
            for (var i = 0; i + 1 < route.Path.Count; i++)
            {
                var (a, b) = (Key(route.Path[i]), Key(route.Path[i + 1]));
                if (a == b) continue;
                at[a] = route.Path[i];
                at[b] = route.Path[i + 1];
                var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                var open = route.State == "open";
                var sea = route.Sea is { } legs && i < legs.Count && legs[i];
                stretches[key] = stretches.TryGetValue(key, out var known) ? (known.Open || open, known.Route, known.Sea || sea) : (open, route, sea);
            }

        // Stretches of one state are joined through places where exactly two of them meet; anywhere else a road ends.
        // Sea stretches join only sea stretches, so a voyage is drawn apart from the roads it meets in port.
        var ends = new Dictionary<((long, long), bool, bool), List<((long, long), (long, long))>>();
        foreach (var (key, value) in stretches)
            foreach (var end in new[] { key.A, key.B })
                (ends.TryGetValue((end, value.Open, value.Sea), out var list) ? list : ends[(end, value.Open, value.Sea)] = new()).Add(key);
        var used = new HashSet<((long, long), (long, long))>();
        var roads = new List<Road>();
        foreach (var (first, value) in stretches.OrderBy(s => s.Key.A).ThenBy(s => s.Key.B))
        {
            if (used.Contains(first)) continue;
            var chain = new LinkedList<(long, long)>(new[] { first.A, first.B });
            used.Add(first);
            void Grow(bool forward)
            {
                while (true)
                {
                    var tip = forward ? chain.Last!.Value : chain.First!.Value;
                    var meeting = ends[(tip, value.Open, value.Sea)];
                    if (meeting.Count != 2) return;
                    var free = meeting.Where(s => !used.Contains(s)).ToList();
                    if (free.Count == 0) return;
                    var next = free[0];
                    used.Add(next);
                    var other = next.Item1 == tip ? next.Item2 : next.Item1;
                    if (forward) chain.AddLast(other);
                    else chain.AddFirst(other);
                }
            }
            Grow(forward: true);
            Grow(forward: false);
            roads.Add(new Road(value.Route.Ref, value.Route.Name, value.Open, Lay(grid, chain.Select(k => at[k]).ToList()), value.Sea));
        }
        return roads;
    }

    /// <summary>A road through places, rounded (its ends kept) and laid over the ground.</summary>
    private static List<Vector3> Lay(Sim.Geo.TerrainGrid grid, IReadOnlyList<MapPoint> places)
    {
        var points = places.Select(p => new Vector2((float)p.X, (float)p.Y)).ToList();
        for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed: false);
        var line = new List<Vector3>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var steps = Math.Max(1, (int)(Vector2.Distance(a, b) * 2 / Step));
            for (var s = 0; s < steps; s++) line.Add(Ground(grid, Vector2.Lerp(a, b, s / (float)steps)));
        }
        line.Add(Ground(grid, points[^1]));
        return line;
    }

    private static Vector3 Ground(Sim.Geo.TerrainGrid grid, Vector2 p) =>
        new(p.X * 2 - 1, Math.Max(0, BlockMesh.HeightAt(grid, p.X, p.Y)) + Lift, p.Y * 2 - 1);
}

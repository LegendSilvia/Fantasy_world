using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>A trade road on the block: its line over the ground and whether it is open.</summary>
public sealed record Road(EntityRef Ref, string Name, bool Open, IReadOnlyList<Vector3> Line);

/// <summary>
/// Trade roads laid on the diorama (the map's *Trade routes* layer): each route's way, market to market through the
/// regions between, smoothed into a gentle curve and laid over the ground point by point so it follows the land.
/// </summary>
public static class RouteLayout
{
    public const float Step = 0.008f;
    public const float Lift = 0.004f;

    public static IReadOnlyList<Road> Roads(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var roads = new List<Road>();
        foreach (var route in map.Routes ?? Array.Empty<MapRoute>())
        {
            var points = route.Path.Select(p => new Vector2((float)p.X, (float)p.Y)).ToList();
            for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed: false);
            var line = new List<Vector3>();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var (a, b) = (points[i], points[i + 1]);
                var steps = Math.Max(1, (int)(Vector2.Distance(a, b) * 2 / Step));
                for (var s = 0; s < steps; s++) line.Add(Ground(grid, Vector2.Lerp(a, b, s / (float)steps)));
            }
            line.Add(Ground(grid, points[^1]));
            roads.Add(new Road(route.Ref, route.Name, route.State == "open", line));
        }
        return roads;
    }

    private static Vector3 Ground(Sim.Geo.TerrainGrid grid, Vector2 p) =>
        new(p.X * 2 - 1, Math.Max(0, BlockMesh.HeightAt(grid, p.X, p.Y)) + Lift, p.Y * 2 - 1);
}

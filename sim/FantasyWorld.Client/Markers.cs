using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

public enum MarkerShape { Settlement, Landmark, Labyrinth, Ruin }

/// <summary>One thing standing on the block: where, what shape, how big and in what colour.</summary>
public sealed record Marker(EntityRef Ref, string Name, MarkerShape Shape, Vector3 Position, float Size, Vector3 Colour, PinMarker Kind);

/// <summary>A numbered digest pin floating above its place.</summary>
public sealed record Pin(int Number, EntityRef Place, string Label, Vector3 Position, long EventId);

/// <summary>A straight piece of a realm border on the block's surface.</summary>
public sealed record Segment(Vector3 From, Vector3 To);

/// <summary>
/// Lays the map screen out on the diorama (sub-project 4 §3): settlement blocks sized by tier and coloured by their
/// holder (orange when occupied), diamonds for landmarks, nested diamonds for labyrinths, numbered pins above digest
/// places, and realm borders along the tile edges where two differently held regions meet.
/// </summary>
public static class Markers
{
    public const float PinLift = 0.14f;

    public static float SizeOf(string tier) => tier switch
    {
        nameof(SettlementTier.City) => 0.045f,
        nameof(SettlementTier.Town) => 0.034f,
        nameof(SettlementTier.Village) => 0.024f,
        _ => 0.016f,
    };

    public static IReadOnlyList<Marker> For(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var colourOf = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Colour);
        var markers = new List<Marker>();
        foreach (var s in map.Settlements.Where(s => s.Position is not null))
        {
            var region = RegionAt(geography, s.Position!);
            var colour = s.Marker == PinMarker.Conflict ? Palette.Conflict
                : colourOf.TryGetValue(region, out var name) && name != "none" ? Palette.Heraldic(name) : Palette.Muted;
            markers.Add(new Marker(s.Ref, s.Name, MarkerShape.Settlement, Projection.ToBlock(grid, s.Position!, 0), SizeOf(s.Tier), colour, s.Marker));
        }
        foreach (var st in map.Structures.Where(st => st.Position is not null))
        {
            var shape = st.State == nameof(StructureState.Ruined) ? MarkerShape.Ruin
                : st.Kind == nameof(StructureKind.Labyrinth) ? MarkerShape.Labyrinth : MarkerShape.Landmark;
            var colour = shape == MarkerShape.Ruin ? Palette.Muted : shape == MarkerShape.Labyrinth ? Palette.For(Category.FaithAndMagic) : Palette.Ink;
            // Structures stand beside their settlement, not inside it.
            var at = Projection.ToBlock(grid, st.Position!, 0) + new Vector3(0.03f, 0, 0.03f);
            markers.Add(new Marker(st.Ref, st.Name, shape, at with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (at.X + 1) / 2, (at.Z + 1) / 2)) },
                0.022f, colour, PinMarker.Plain));
        }
        return markers;
    }

    /// <summary>The digest's numbered pins, lifted above the block so they read from any angle.</summary>
    public static IReadOnlyList<Pin> Pins(DigestScreen digest, Geography geography) =>
        digest.Pins.Select(p => new Pin(p.Number, p.Place, p.Label, Projection.ToBlock(geography.Terrain, p.Position, PinLift), p.EventId)).ToList();

    /// <summary>Tile edges between land regions whose holders differ, slightly above the surface.</summary>
    public static IReadOnlyList<Segment> Borders(MapScreen map, Geography geography, float lift = 0.006f)
    {
        var grid = geography.Terrain;
        return BorderEdges(map, geography).Select(e => new Segment(Lift(grid, e.A.X / (double)grid.Width, e.A.Y / (double)grid.Height, lift),
            Lift(grid, e.B.X / (double)grid.Width, e.B.Y / (double)grid.Height, lift))).ToList();
    }

    /// <summary>
    /// The borders as smooth lines: tile edges chained into polylines, their corners rounded (Chaikin, twice) so they
    /// read as drawn lines rather than staircases, then laid on the surface.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Vector3>> BorderLines(MapScreen map, Geography geography, float lift = 0.004f)
    {
        var grid = geography.Terrain;
        var lines = new List<IReadOnlyList<Vector3>>();
        foreach (var chain in Chains(BorderEdges(map, geography)))
        {
            var points = chain.Select(p => new Vector2(p.X / (float)grid.Width, p.Y / (float)grid.Height)).ToList();
            var closed = chain.Count > 2 && chain[0] == chain[^1];
            // Drop the staircase first (keep only corners that matter at under a tile), then round what is left.
            points = Simplify(points, 0.75f / grid.Width);
            for (var k = 0; k < 3; k++) points = Chaikin(points, closed);
            lines.Add(points.Select(p => Lift(grid, p.X, p.Y, lift)).ToList());
        }
        return lines;
    }

    private static Vector3 Lift(TerrainGrid grid, double u, double v, float lift) =>
        new((float)(u * 2 - 1), Math.Max(0, BlockMesh.HeightAt(grid, u, v)) + lift, (float)(v * 2 - 1));

    /// <summary>Border edges between tile corners (integer lattice points).</summary>
    private static List<(TileCoord A, TileCoord B)> BorderEdges(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var owner = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Owner?.Id ?? 0);
        long Owner(int region) => owner.GetValueOrDefault(region);
        var edges = new List<(TileCoord, TileCoord)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var here = geography.RegionOf[grid.Index(x, y)];
                if (here == 0) continue;
                if (x + 1 < grid.Width && geography.RegionOf[grid.Index(x + 1, y)] is var right and not 0 && right != here && Owner(right) != Owner(here))
                    edges.Add((new TileCoord(x + 1, y), new TileCoord(x + 1, y + 1)));
                if (y + 1 < grid.Height && geography.RegionOf[grid.Index(x, y + 1)] is var down and not 0 && down != here && Owner(down) != Owner(here))
                    edges.Add((new TileCoord(x, y + 1), new TileCoord(x + 1, y + 1)));
            }
        return edges;
    }

    /// <summary>Chains edges into polylines: walks from every end or junction, then around any loops left over.</summary>
    public static List<List<TileCoord>> Chains(IEnumerable<(TileCoord A, TileCoord B)> edges)
    {
        var links = new Dictionary<TileCoord, List<TileCoord>>();
        foreach (var (a, b) in edges)
        {
            (links.TryGetValue(a, out var la) ? la : links[a] = new()).Add(b);
            (links.TryGetValue(b, out var lb) ? lb : links[b] = new()).Add(a);
        }
        var used = new HashSet<(TileCoord, TileCoord)>();
        bool Use(TileCoord a, TileCoord b) => used.Add((a, b)) & used.Add((b, a));
        var chains = new List<List<TileCoord>>();
        void Walk(TileCoord start, TileCoord next)
        {
            if (!Use(start, next)) return;
            var chain = new List<TileCoord> { start, next };
            var at = next;
            while (links[at].Count == 2 && links[at].FirstOrDefault(n => !used.Contains((at, n))) is var step && links[at].Any(n => !used.Contains((at, n))))
            {
                Use(at, step);
                chain.Add(step);
                at = step;
            }
            chains.Add(chain);
        }
        foreach (var node in links.Keys.Where(n => links[n].Count != 2).OrderBy(n => n.Y).ThenBy(n => n.X).ToList())
            foreach (var next in links[node].ToList()) Walk(node, next);
        foreach (var node in links.Keys.OrderBy(n => n.Y).ThenBy(n => n.X).ToList())
            foreach (var next in links[node].ToList()) Walk(node, next);
        return chains;
    }

    /// <summary>Douglas–Peucker: keeps the points that stray more than <paramref name="tolerance"/> from a straight run.</summary>
    internal static List<Vector2> Simplify(List<Vector2> points, float tolerance)
    {
        if (points.Count < 3) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var (best, far) = (-1, tolerance);
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i], points[a], points[b]);
                if (d > far) (best, far) = (i, d);
            }
            if (best < 0) continue;
            keep[best] = true;
            stack.Push((a, best));
            stack.Push((best, b));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + t * ab);
    }

    /// <summary>One round of corner cutting; open lines keep their ends.</summary>
    internal static List<Vector2> Chaikin(List<Vector2> points, bool closed)
    {
        if (points.Count < 3) return points;
        var result = new List<Vector2>(points.Count * 2);
        if (!closed) result.Add(points[0]);
        for (var i = 0; i < points.Count - 1; i++)
        {
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
            result.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
        }
        if (closed) result.Add(result[0]);
        else result.Add(points[^1]);
        return result;
    }

    private static long RegionAt(Geography geography, MapPoint point)
    {
        var grid = geography.Terrain;
        var x = (int)Math.Clamp(point.X * grid.Width, 0, grid.Width - 1);
        var y = (int)Math.Clamp(point.Y * grid.Height, 0, grid.Height - 1);
        return geography.RegionOf[grid.Index(x, y)];
    }
}

using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>A campaign arrow: the army it belongs to, its colour, its mesh and where its head points.</summary>
public sealed record Arrow(EntityRef Army, Vector3 Colour, MeshData Mesh, Vector3 Head);

/// <summary>A front: the line where two warring sides' lands meet, with teeth pointing at the attacker.</summary>
public sealed record Front(EntityRef War, Vector3 Colour, IReadOnlyList<Vector3> Line, MeshData Teeth);

/// <summary>
/// War drawn like a strategy map (sub-project 4, the war layer): broad tapered arrows along each host's road to its
/// target, front lines between the warring sides' lands with triangular teeth facing the attacker, and occupied land
/// hatched in the conflict colour with an outline. Pure geometry in block space; the renderer adds animation.
/// </summary>
public static class WarLayout
{
    public const float Lift = 0.014f;
    public const float TailWidth = 0.016f;
    public const float BodyWidth = 0.036f;
    public const float HeadWidth = 0.08f;
    public const float HeadLength = 0.065f;

    /// <summary>Arrows shorter than this (in block units, the map's width being 2) are not drawn.</summary>
    public const float MinLength = 0.02f;
    public const float ToothSpacing = 0.05f;
    public const float ToothSize = 0.02f;

    /// <summary>Strategy-map side colours: attackers in red, defenders in blue (banners keep their realm's colours).</summary>
    public static readonly Vector3 Attacker = BlockMesh.Hex(0xd2382d);
    public static readonly Vector3 Defender = BlockMesh.Hex(0x2f6db5);

    public static Vector3 SideColour(string side) => side == "attacker" ? Attacker : Defender;

    /// <summary>One arrow per host on the march (a route of two or more regions), in its realm's colour.</summary>
    public static IReadOnlyList<Arrow> Arrows(MapScreen map, Geography geography) =>
        (map.Armies ?? Array.Empty<MapArmy>()).Where(a => a.Route.Count >= 2)
            .Select(a => (Army: a, Mesh: ArrowMesh(a.Route.Select(p => new Vector2((float)p.X, (float)p.Y)).ToList(), geography.Terrain, SideColour(a.Side))))
            .Where(x => x.Mesh is not null)
            .Select(x => new Arrow(x.Army.Ref, SideColour(x.Army.Side), x.Mesh!, x.Mesh!.Positions[^1]))
            .ToList();

    /// <summary>A realm's colour for war marks: its heraldic colour, kept away from black and white so it reads on terrain.</summary>
    public static Vector3 Colour(string heraldic)
    {
        var c = Palette.Heraldic(heraldic);
        var luma = 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;
        if (luma < 0.2f) return Palette.Ink * 0.6f + Palette.Muted * 0.4f;
        if (luma > 0.8f) return BlockMesh.Hex(0x8f8a80);
        return c;
    }

    /// <summary>
    /// A ribbon along a map polyline (0–1 coordinates), smoothed, widening from tail to body and ending in a head;
    /// UVs run across (−1 to 1) and along (0 at the tail, 1 at the tip). Null if the route is too short to draw.
    /// </summary>
    public static MeshData? ArrowMesh(List<Vector2> route, Sim.Geo.TerrainGrid grid, Vector3 colour)
    {
        var points = route;
        for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed: false);
        var path = points.Select(p => new Vector3(p.X * 2 - 1, 0, p.Y * 2 - 1)).ToList();
        var lengths = new List<float> { 0 };
        for (var i = 1; i < path.Count; i++) lengths.Add(lengths[^1] + Vector3.Distance(path[i - 1], path[i]));
        var total = lengths[^1];
        // A short march (one region to the next) keeps its arrow with a smaller head; only a stub too short to read is dropped.
        if (total < MinLength) return null;
        var head = Math.Min(HeadLength, total / 2.2f);
        var headWidth = HeadWidth * head / HeadLength;
        var bodyEnd = total - head;

        Vector3 OnGround(Vector3 p) => p with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (p.X + 1) / 2, (p.Z + 1) / 2)) + Lift };
        Vector3 Across(int i)
        {
            var d = path[Math.Min(path.Count - 1, i + 1)] - path[Math.Max(0, i - 1)];
            return Vector3.Normalize(new Vector3(-d.Z, 0, d.X));
        }
        Vector3 At(float s)
        {
            var i = lengths.FindIndex(l => l >= s);
            if (i <= 0) return path[0];
            var t = (s - lengths[i - 1]) / Math.Max(1e-6f, lengths[i] - lengths[i - 1]);
            return Vector3.Lerp(path[i - 1], path[i], t);
        }

        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        for (var i = 0; i < path.Count && lengths[i] <= bodyEnd; i++)
        {
            var w = TailWidth + (BodyWidth - TailWidth) * (lengths[i] / bodyEnd);
            var n = Across(i) * (w / 2);
            positions.Add(OnGround(path[i] - n));
            positions.Add(OnGround(path[i] + n));
            uvs.Add(new Vector2(-1, lengths[i] / total));
            uvs.Add(new Vector2(1, lengths[i] / total));
        }
        // The head: a wide base at the end of the body, then the tip.
        var baseCentre = At(bodyEnd);
        var tip = path[^1];
        var dir = Vector3.Normalize(tip - baseCentre);
        var across = new Vector3(-dir.Z, 0, dir.X);
        positions.Add(OnGround(baseCentre - across * (BodyWidth / 2)));
        positions.Add(OnGround(baseCentre + across * (BodyWidth / 2)));
        uvs.Add(new Vector2(-1, bodyEnd / total));
        uvs.Add(new Vector2(1, bodyEnd / total));
        for (var k = 0; k + 3 < positions.Count; k += 2)
            indices.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
        var b = positions.Count;
        positions.Add(OnGround(baseCentre - across * (headWidth / 2)));
        positions.Add(OnGround(baseCentre + across * (headWidth / 2)));
        positions.Add(OnGround(tip));
        uvs.Add(new Vector2(-1, bodyEnd / total));
        uvs.Add(new Vector2(1, bodyEnd / total));
        uvs.Add(new Vector2(0, 1));
        indices.AddRange(new[] { b, b + 2, b + 1 });
        return new MeshData(positions.ToArray(), Enumerable.Repeat(Vector3.UnitY, positions.Count).ToArray(),
            Enumerable.Repeat(colour, positions.Count).ToArray(), indices.ToArray())
        { Uvs = uvs.ToArray(), Alphas = Enumerable.Repeat(0.9f, positions.Count).ToArray() };
    }

    /// <summary>Fronts of the wars on the map: where a region of one side meets a region of the other.</summary>
    public static IReadOnlyList<Front> Fronts(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var owner = map.Regions.ToDictionary(r => r.Ref.Id, r => r);
        var fronts = new List<Front>();
        foreach (var conflict in map.Conflicts)
        {
            var attackers = conflict.Attackers.ToHashSet();
            var defenders = conflict.Defenders.ToHashSet();
            int Side(int region) => region != 0 && owner.TryGetValue(region, out var r) && r.Owner is { } o
                ? attackers.Contains(o) ? 1 : defenders.Contains(o) ? 2 : 0 : 0;
            var edges = new List<(Sim.Geo.TileCoord, Sim.Geo.TileCoord)>();
            for (var y = 0; y < grid.Height; y++)
                for (var x = 0; x < grid.Width; x++)
                {
                    var here = Side(geography.RegionOf[grid.Index(x, y)]);
                    if (here == 0) continue;
                    if (x + 1 < grid.Width && Side(geography.RegionOf[grid.Index(x + 1, y)]) is var r and not 0 && r != here)
                        edges.Add((new(x + 1, y), new(x + 1, y + 1)));
                    if (y + 1 < grid.Height && Side(geography.RegionOf[grid.Index(x, y + 1)]) is var d and not 0 && d != here)
                        edges.Add((new(x, y + 1), new(x + 1, y + 1)));
                }
            if (edges.Count == 0) continue;
            var colour = Defender;
            foreach (var chain in Markers.Chains(edges))
            {
                var points = chain.Select(p => new Vector2(p.X / (float)grid.Width, p.Y / (float)grid.Height)).ToList();
                points = Markers.Simplify(points, 0.75f / grid.Width);
                for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed: false);
                var line = points.Select(p => Ground(grid, p, 0.006f)).ToList();
                fronts.Add(new Front(conflict.War, colour, line, Teeth(line, geography, Side, colour)));
            }
        }
        return fronts;
    }

    /// <summary>Triangles along a front, every <see cref="ToothSpacing"/>, pointing into the attacker's land.</summary>
    private static MeshData Teeth(IReadOnlyList<Vector3> line, Geography geography, Func<int, int> side, Vector3 colour)
    {
        var grid = geography.Terrain;
        var positions = new List<Vector3>();
        var travelled = 0f;
        var next = ToothSpacing / 2;
        for (var i = 1; i < line.Count; i++)
        {
            var (a, b) = (line[i - 1], line[i]);
            var length = Vector3.Distance(a, b);
            while (next <= travelled + length && length > 1e-6f)
            {
                var t = (next - travelled) / length;
                var p = Vector3.Lerp(a, b, t);
                var dir = Vector3.Normalize(b - a);
                var n = new Vector3(-dir.Z, 0, dir.X);
                // Point the tooth toward the attacker's side of the line.
                var probe = p + n * (ToothSize * 1.5f);
                var tile = grid.Index(Math.Clamp((int)((probe.X + 1) / 2 * grid.Width), 0, grid.Width - 1), Math.Clamp((int)((probe.Z + 1) / 2 * grid.Height), 0, grid.Height - 1));
                if (side(geography.RegionOf[tile]) != 1) n = -n;
                var half = dir * (ToothSize / 2);
                positions.AddRange(new[] { p - half, p + n * ToothSize, p + half }.Select(v => v with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (v.X + 1) / 2, (v.Z + 1) / 2)) + 0.007f }));
                next += ToothSpacing;
            }
            travelled += length;
        }
        return new MeshData(positions.ToArray(), Enumerable.Repeat(Vector3.UnitY, positions.Count).ToArray(),
            Enumerable.Repeat(colour, positions.Count).ToArray(), Enumerable.Range(0, positions.Count).ToArray());
    }

    /// <summary>
    /// A quad over every tile of an occupied region, just above the ground, for the hatch shader; coloured by the
    /// occupier's side (red for an attacker, blue for a defender who has struck back).
    /// </summary>
    public static MeshData? Occupation(MapScreen map, Geography geography)
    {
        var occupied = map.Regions.Where(r => r.Occupier is not null).Select(r => r.Ref.Id).ToHashSet();
        if (occupied.Count == 0) return null;
        var attackers = map.Conflicts.SelectMany(c => c.Attackers).ToHashSet();
        var tint = map.Regions.Where(r => r.Occupier is not null).ToDictionary(r => r.Ref.Id, r => attackers.Contains(r.Occupier!.Value) ? Attacker : Defender);
        var grid = geography.Terrain;
        var positions = new List<Vector3>();
        var colours = new List<Vector3>();
        var indices = new List<int>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (!occupied.Contains(geography.RegionOf[grid.Index(x, y)])) continue;
                var k = positions.Count;
                foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
                {
                    positions.Add(Ground(grid, new Vector2((x + dx) / (float)grid.Width, (y + dy) / (float)grid.Height), 0.005f));
                    colours.Add(tint[geography.RegionOf[grid.Index(x, y)]]);
                }
                indices.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
        return new MeshData(positions.ToArray(), Enumerable.Repeat(Vector3.UnitY, positions.Count).ToArray(), colours.ToArray(), indices.ToArray());
    }

    /// <summary>The outline of each occupied area (edges between occupied and unoccupied tiles), smoothed.</summary>
    public static IReadOnlyList<IReadOnlyList<Vector3>> OccupationOutline(MapScreen map, Geography geography)
    {
        var occupied = map.Regions.Where(r => r.Occupier is not null).Select(r => r.Ref.Id).ToHashSet();
        var grid = geography.Terrain;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && occupied.Contains(geography.RegionOf[grid.Index(x, y)]);
        var edges = new List<(Sim.Geo.TileCoord, Sim.Geo.TileCoord)>();
        for (var y = -1; y < grid.Height; y++)
            for (var x = -1; x < grid.Width; x++)
            {
                if (In(x, y) != In(x + 1, y) && y >= 0) edges.Add((new(x + 1, y), new(x + 1, y + 1)));
                if (In(x, y) != In(x, y + 1) && x >= 0) edges.Add((new(x, y + 1), new(x + 1, y + 1)));
            }
        return Markers.Chains(edges).Select(chain =>
        {
            var points = chain.Select(p => new Vector2(p.X / (float)grid.Width, p.Y / (float)grid.Height)).ToList();
            var closed = chain.Count > 2 && chain[0] == chain[^1];
            points = Markers.Simplify(points, 0.75f / grid.Width);
            for (var k = 0; k < 3; k++) points = Markers.Chaikin(points, closed);
            return (IReadOnlyList<Vector3>)points.Select(p => Ground(grid, p, 0.006f)).ToList();
        }).ToList();
    }

    private static Vector3 Ground(Sim.Geo.TerrainGrid grid, Vector2 p, float lift) =>
        new(p.X * 2 - 1, Math.Max(0, BlockMesh.HeightAt(grid, p.X, p.Y)) + lift, p.Y * 2 - 1);
}

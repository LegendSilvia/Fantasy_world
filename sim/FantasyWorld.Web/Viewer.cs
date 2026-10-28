using System.Globalization;
using System.Numerics;
using System.Text.Json;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using Microsoft.JSInterop;

namespace FantasyWorld.Web;

/// <summary>
/// What the page asks of the world (sub-project 11 W2): it loads the day's bundle once, then asks for screens (the same
/// view JSON the game reads) and for the diorama's geometry (the same meshes the game draws), in block space: x and z
/// across the map from −1 to 1, y up. Meshes come packed (see <see cref="Pack"/>); everything else is JSON. Read-only.
/// </summary>
public static class Viewer
{
    private static World? _world;
    private static EventLog? _log;

    private static World World => _world ?? throw new InvalidOperationException("Load the world first.");
    private static EventLog Log => _log ?? throw new InvalidOperationException("Load the world first.");
    private static Geography Geography => World.Geography ?? throw new InvalidOperationException("This world has no geography.");

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Loads the gzipped bundle; answers the world's name, its first and current year, and today's date.</summary>
    [JSInvokable]
    public static string Load(byte[] gzip)
    {
        (_world, _log) = WebBundle.FromGzip(gzip).Load();
        return JsonSerializer.Serialize(new { name = DigestScreen.DisplayName(World), year = World.Date.Year, firstYear = 1, today = World.Date.ToString() }, Json);
    }

    /// <summary>
    /// A screen's JSON, as the game's views make it: <c>map</c> (arg: a year, or empty for today), <c>character</c>,
    /// <c>family</c>, <c>realm</c>, <c>artifact</c> (arg: an id), <c>why</c> (arg: an event id), <c>chronicle</c> (arg: a year, or empty).
    /// </summary>
    /// <summary>
    /// What the players may answer today (sub-project 13 N5): the open prayers (who prays, to whom, for what, told as the
    /// chronicle tells it) and the face-offs still to be fought, with both sides, as JSON.
    /// </summary>
    [JSInvokable]
    public static string Prayers()
    {
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(World);
        var prayers = World.Prayers.Values.Where(p => p.IsOpen && Log.TryGet(p.EventId, out _)).OrderByDescending(p => p.Day).ThenBy(p => p.Id)
            .Select(p => new
            {
                id = p.Id, plea = p.Plea, god = World.Faiths.GetValueOrDefault(p.FaithId)?.Deity, text = narrator.Sentence(Log.Get(p.EventId)),
                supplicant = World.Characters[p.SupplicantId].Ref.ToString(), year = new WorldDate(p.Day).Year,
            });
        var faceOffs = PrayerSystem.Blessable(World, Log).Select(e => FantasyWorld.Sim.Chronicle.FaceOffs.For(World, e)).OfType<FantasyWorld.Sim.Chronicle.FaceOff>()
            .Select(f => new
            {
                id = f.EventId, kind = f.Kind, stakes = f.Stakes, odds = f.OddsA,
                a = new { name = f.A.Name, colour = V3(WarLayout.Colour(f.A.Colour)), @class = f.A.Class }, b = new { name = f.B.Name, colour = V3(WarLayout.Colour(f.B.Colour)), @class = f.B.Class },
            });
        return JsonSerializer.Serialize(new { prayers, faceOffs }, Json);
        static float[] V3(Vector3 v) => [v.X, v.Y, v.Z];
    }

    [JSInvokable]
    public static string Screen(string kind, string arg)
    {
        long Id() => long.Parse(arg, CultureInfo.InvariantCulture);
        return kind switch
        {
            "map" => ViewJson.Serialize(MapAt(arg)),
            "character" => ViewJson.Serialize(CharacterScreen.Build(World, Log, Id())),
            "family" => ViewJson.Serialize(FamilyTree.Build(World, Id())),
            "realm" => ViewJson.Serialize(RealmScreen.Build(World, Log, Id())),
            "artifact" => ViewJson.Serialize(ArtifactScreen.Build(World, Log, Id())),
            "why" => ViewJson.Serialize(WhyScreen.Build(World, Log, Id())),
            "chronicle" => ViewJson.Serialize(int.TryParse(arg, out var y) ? ChronicleScreen.Build(World, Log, y, y) : ChronicleScreen.Build(World, Log)),
            _ => throw new ArgumentException($"No screen '{kind}'."),
        };
    }

    /// <summary>The map at the end of a year, as the game's history slider shows it (empty or this year: today).</summary>
    private static MapScreen MapAt(string year) =>
        MapScreen.Build(World, Log, int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y >= 1 && y < World.Date.Year
            ? new WorldDate((long)y * WorldDate.DaysPerYear - 1) : null);

    /// <summary>The terrain block's parts as packed meshes: <c>top</c>, <c>sides</c> or <c>water</c>.</summary>
    [JSInvokable]
    public static byte[] Block(string part, int resolution)
    {
        var grid = Geography.Terrain;
        return Pack(part switch
        {
            "top" => BlockMesh.Top(grid, resolution),
            "sides" => BlockMesh.Sides(grid, resolution),
            "water" => BlockMesh.Water(grid),
            _ => throw new ArgumentException($"No block part '{part}'."),
        });
    }

    /// <summary>
    /// The woods, eight floats a tree (x, y, z, height, kind, r, g, b), leaving clearings round today's settlements
    /// as the game does.
    /// </summary>
    [JSInvokable]
    public static float[] Trees()
    {
        var map = MapScreen.Build(World, Log);
        var clearings = Markers.For(map, Geography).Where(m => m.Shape == MarkerShape.Settlement).Select(m => m.Position);
        return Scatter.Trees(Geography.Terrain, clearings)
            .SelectMany(t => new[] { t.Position.X, t.Position.Y, t.Position.Z, t.Height, (float)t.Kind, t.Colour.X, t.Colour.Y, t.Colour.Z }).ToArray();
    }

    /// <summary>
    /// The things standing on the block in a year (empty: today), as JSON: houses (nine numbers each: x, y, z, size, yaw,
    /// roof r, g, b, tower), markers for landmarks and labyrinths, border and road lines, realms' standards and hosts'
    /// banners with where their names go, and each host's current move as a packed arrow mesh (base64).
    /// </summary>
    [JSInvokable]
    public static string Scene(string year)
    {
        var map = MapAt(year);
        var geography = Geography;
        var grid = geography.Terrain;
        var markers = Markers.For(map, geography);
        float[] V(Vector3 v) => [v.X, v.Y, v.Z];
        var scene = new
        {
            houses = markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, grid))
                .SelectMany(h => new[] { h.Position.X, h.Position.Y, h.Position.Z, h.Size, h.Yaw, h.Roof.X, h.Roof.Y, h.Roof.Z, h.Tower ? 1f : 0f }).ToArray(),
            settlements = markers.Where(m => m.Shape == MarkerShape.Settlement)
                .Select(m => new { @ref = m.Ref.ToString(), name = m.Name, at = V(m.Position), size = m.Size, kind = m.Kind.ToString() }),
            structures = markers.Where(m => m.Shape != MarkerShape.Settlement)
                .Select(m => new { @ref = m.Ref.ToString(), name = m.Name, shape = m.Shape.ToString(), at = V(m.Position), size = m.Size, colour = V(m.Colour) }),
            borders = Markers.BorderLines(map, geography).Select(line => line.SelectMany(V).ToArray()),
            roads = RouteLayout.Roads(map, geography).Select(r => new { open = r.Open, points = r.Line.SelectMany(V).ToArray() }),
            realms = (map.Realms ?? []).Select(r => new
            {
                @ref = r.Ref.ToString(), name = r.Name, rank = r.Rank, regions = r.Regions,
                at = V(Projection.ToBlock(grid, r.Position, 0)), colour = V(WarLayout.Colour(r.Colour)), accent = V(Palette.Heraldic(r.Accent)),
            }),
            armies = (map.Armies ?? []).Select(a => new
            {
                @ref = a.Ref.ToString(), name = a.Name, soldiers = a.Soldiers, side = a.Side, at = V(Projection.ToBlock(grid, a.Position, 0)),
                colour = V(WarLayout.Colour(a.Colour)), commander = a.Commander?.Name,
            }),
            arrows = WarLayout.Arrows(map, geography).Select(a => new { army = a.Army.ToString(), mesh = Convert.ToBase64String(Pack(a.Mesh)) }),
            // The map's badges (the owner, 2026-10-28): boss markers over beasts, crossed swords where fights are.
            beasts = (map.Monsters ?? []).Select(m => new
            {
                name = m.Name, breed = m.Breed, might = m.Might, at = V(Projection.ToBlock(grid, m.Position, 0)), eventId = m.RoseEventId,
                badge = m.Slumbering ? "asleep" : m.Legendary ? "legend" : "boss",
            }),
            fights = (map.Battles ?? []).Select(b => new
            {
                kind = b.Kind, underWay = b.UnderWay, at = V(Projection.ToBlock(grid, b.Position, 0)), eventId = b.EventId,
                badge = b.UnderWay ? "underway" : "fought",
            }),
            badges = new Dictionary<string, string>
            {
                ["boss"] = Badges.Boss(false, false), ["legend"] = Badges.Boss(true, false), ["asleep"] = Badges.Boss(false, true),
                ["fought"] = Badges.Swords(false), ["underway"] = Badges.Swords(true),
            },
        };
        return JsonSerializer.Serialize(scene, Json);
    }

    /// <summary>
    /// A mesh as little-endian bytes: vertex count and index count (int32), a flags int32 (1: alphas, 2: uvs), then
    /// positions, normals and colours (3 float32 each per vertex), alphas (1 each), uvs (2 each), and the indices (int32).
    /// </summary>
    public static byte[] Pack(MeshData mesh)
    {
        var n = mesh.Positions.Length;
        var flags = (mesh.Alphas is not null ? 1 : 0) | (mesh.Uvs is not null ? 2 : 0);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(n);
        w.Write(mesh.Indices.Length);
        w.Write(flags);
        foreach (var set in new[] { mesh.Positions, mesh.Normals, mesh.Colors })
            foreach (var v in set) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        if (mesh.Alphas is { } alphas) foreach (var a in alphas) w.Write(a);
        if (mesh.Uvs is { } uvs) foreach (var uv in uvs) { w.Write(uv.X); w.Write(uv.Y); }
        foreach (var i in mesh.Indices) w.Write(i);
        w.Flush();
        return stream.ToArray();
    }
}

using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Broadcast.Media;

/// <summary>A painted image: width, height and three bytes a pixel.</summary>
public sealed record Raster(int Width, int Height, byte[] Rgb)
{
    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = (y * Width + x) * 3;
        return (Rgb[i], Rgb[i + 1], Rgb[i + 2]);
    }
}

/// <summary>
/// The political map as a picture for the broadcast (sub-project 6 §4, M2), from the same map view the game draws:
/// land in its biome's colour washed with the holding realm's heraldic colour, the sea by depth, rivers, realm borders
/// in ink and region borders faintly, occupied land striped in the colour of conflict, and towns as dots. No text: the
/// post names the realms.
/// </summary>
public static class MapPainter
{
    /// <summary>6 px a tile on a medium world: 1,536 px, big enough to serve as podcast cover art (1,400 px at least).</summary>
    public const int DefaultWidth = 1536;

    /// <summary>How much of a realm's colour washes over the land it holds.</summary>
    public const float RealmWash = 0.45f;

    public static byte[] Png(World world, EventLog log, int width = DefaultWidth)
    {
        var raster = Paint(world, log, width);
        return Media.Png.Encode(raster.Width, raster.Height, raster.Rgb);
    }

    public static Raster Paint(World world, EventLog log, int width = DefaultWidth)
    {
        var geography = world.Geography ?? throw new InvalidOperationException("The world has no geography to draw.");
        var terrain = geography.Terrain;
        var scale = Math.Max(1, width / terrain.Width);
        var (w, h) = (terrain.Width * scale, terrain.Height * scale);
        var map = MapScreen.Build(world, log);
        var regions = map.Regions.ToDictionary(r => r.Ref.Id);
        var rgb = new byte[w * h * 3];

        long OwnerOf(int tile) => geography.RegionOf[tile] is var r and > 0 && regions.TryGetValue(r, out var region) ? region.Owner?.Id ?? 0 : -1;

        for (var tile = 0; tile < terrain.Count; tile++)
        {
            var (tx, ty) = (tile % terrain.Width, tile / terrain.Width);
            var colour = TileColour(terrain, tile, geography.RegionOf[tile] is var id and > 0 ? regions.GetValueOrDefault(id) : null);
            var occupied = geography.RegionOf[tile] > 0 && regions.GetValueOrDefault(geography.RegionOf[tile])?.Occupier is not null;
            for (var py = 0; py < scale; py++)
                for (var px = 0; px < scale; px++)
                {
                    var (x, y) = (tx * scale + px, ty * scale + py);
                    var c = occupied && (x + y) % 8 < 2 ? Palette.Conflict : colour;
                    Set(rgb, w, x, y, c);
                }

            // Borders on this tile's right and bottom edges: ink between realms (and at the coast), faint between regions.
            foreach (var (nx, ny, right) in new[] { (tx + 1, ty, true), (tx, ty + 1, false) })
            {
                if (nx >= terrain.Width || ny >= terrain.Height) continue;
                var other = terrain.Index(nx, ny);
                var (a, b) = (OwnerOf(tile), OwnerOf(other));
                Vector3? line = a != b && (a > 0 || b > 0) ? Palette.Ink
                    : geography.RegionOf[tile] != geography.RegionOf[other] && a >= 0 && b >= 0 ? Mix(colour, Palette.Ink, 0.25f)
                    : null;
                if (line is not { } ink) continue;
                var thick = a != b ? Math.Max(1, scale / 2) : 1;
                for (var t = 0; t < scale; t++)
                    for (var k = 0; k < thick; k++)
                        if (right) Set(rgb, w, tx * scale + scale - 1 - k, ty * scale + t, ink);
                        else Set(rgb, w, tx * scale + t, ty * scale + scale - 1 - k, ink);
            }
        }

        foreach (var s in map.Settlements)
        {
            if (s.Position is not { } at) continue;
            var radius = s.Tier switch { nameof(SettlementTier.City) => scale * 2, nameof(SettlementTier.Town) => scale * 3 / 2, _ => Math.Max(1, scale / 2) };
            Dot(rgb, w, h, (int)(at.X * w), (int)(at.Y * h), radius + 1, Palette.Card);
            Dot(rgb, w, h, (int)(at.X * w), (int)(at.Y * h), radius, Palette.Ink);
        }
        return new Raster(w, h, rgb);
    }

    private static Vector3 TileColour(TerrainGrid terrain, int tile, MapRegion? region)
    {
        if (!terrain.IsLand(tile))
        {
            var depth = Math.Clamp((terrain.SeaLevel - terrain.Elevation[tile]) / (float)Math.Max(1, (int)terrain.SeaLevel), 0, 1);
            return Mix(BlockMesh.Shallows, BlockMesh.Deep, depth);
        }
        var land = BlockMesh.ColourOf(terrain.BiomeAt(tile));
        // Heraldic colours are deep; lightened, they tint the land without hiding it.
        land = region is { Owner: not null } ? Mix(land, Mix(Palette.Heraldic(region.Colour), Palette.Card, 0.3f), RealmWash) : Mix(land, Palette.Ground, 0.35f);
        return terrain.Has(tile, TileFlags.River) ? Mix(land, BlockMesh.River, 0.6f) : land;
    }

    private static Vector3 Mix(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    private static void Dot(byte[] rgb, int w, int h, int cx, int cy, int r, Vector3 c)
    {
        for (var y = cy - r; y <= cy + r; y++)
            for (var x = cx - r; x <= cx + r; x++)
                if (x >= 0 && y >= 0 && x < w && y < h && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Set(rgb, w, x, y, c);
    }

    private static void Set(byte[] rgb, int w, int x, int y, Vector3 c)
    {
        var i = (y * w + x) * 3;
        rgb[i] = Byte(c.X);
        rgb[i + 1] = Byte(c.Y);
        rgb[i + 2] = Byte(c.Z);
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
}

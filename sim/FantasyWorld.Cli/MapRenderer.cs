using System.Text;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Cli;

/// <summary>ASCII map of the world. Each character covers a block of tiles (twice as tall as wide).</summary>
public static class MapRenderer
{
    public const string Legend =
        "~ sea  = river  . grassland  f forest  % swamp  , savanna  _ desert  J jungle  : tundra  T taiga  n hills  ^ mountains  @ city  O town  o village";

    private const string RegionGlyphs = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Render(World world, string layer)
    {
        var geography = world.Geography ?? throw new InvalidOperationException("This world has no map.");
        var grid = geography.Terrain;
        var step = Math.Max(1, grid.Width / 128);
        var places = new Dictionary<(int, int), SettlementTier>();
        foreach (var s in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Village))
        {
            var cell = (s.Tile.X / step, s.Tile.Y / (step * 2));
            if (!places.TryGetValue(cell, out var tier) || s.Tier > tier) places[cell] = s.Tier;
        }

        var text = new StringBuilder();
        for (var y = 0; y < grid.Height; y += step * 2)
        {
            for (var x = 0; x < grid.Width; x += step)
            {
                var i = grid.Index(x, y);
                text.Append(places.TryGetValue((x / step, y / (step * 2)), out var tier)
                    ? tier switch { SettlementTier.City => '@', SettlementTier.Town => 'O', _ => 'o' }
                    : layer == "regions"
                        ? geography.RegionOf[i] == 0 ? '~' : RegionGlyphs[geography.RegionOf[i] % RegionGlyphs.Length]
                        : Glyph(grid, i));
            }
            text.AppendLine();
        }
        text.Append(layer == "regions" ? "Each glyph is a region (id mod 62); @ O o are settlements." : Legend);
        return text.ToString();
    }

    private static char Glyph(TerrainGrid grid, int i) =>
        grid.Has(i, TileFlags.River)
            ? '='
            : grid.BiomeAt(i) switch
            {
                Biome.Ocean => '~',
                Biome.Tundra => ':',
                Biome.Taiga => 'T',
                Biome.Grassland => '.',
                Biome.Forest => 'f',
                Biome.Swamp => '%',
                Biome.Desert => '_',
                Biome.Savanna => ',',
                Biome.Jungle => 'J',
                Biome.Hills => 'n',
                _ => '^',
            };
}

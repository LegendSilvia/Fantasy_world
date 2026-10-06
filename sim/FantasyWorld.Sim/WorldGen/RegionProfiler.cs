using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Turns each region's tiles into a Region: biome, climate, fertility, danger, ley power and resources.</summary>
public static class RegionProfiler
{
    public static List<Region> Profile(TerrainGrid grid, RegionLayout layout, ulong seed, Rng rng, IdGen ids, NameGen names)
    {
        var ley = new Noise2D(StableHash.Derive(seed, "terrain/ley"));
        var regions = new List<Region>();
        for (var r = 1; r <= layout.Count; r++)
        {
            var tiles = layout.Tiles[r];
            var count = tiles.Count;
            var biomes = tiles.Select(grid.BiomeAt).ToList();
            int CountOf(params Biome[] kinds) => biomes.Count(kinds.Contains);

            var mountains = CountOf(Biome.Mountain);
            var riverTiles = tiles.Count(i => grid.Has(i, TileFlags.River));
            var coastTiles = tiles.Count(i => grid.Has(i, TileFlags.Coast));
            var farmable = biomes.Sum(BiomeInfo.Farmland);
            var farmland = farmable + 0.3 * riverTiles;
            var fertility = farmable > 0
                ? biomes.Sum(b => BiomeInfo.BaseFertility(b) * BiomeInfo.Farmland(b)) / farmable
                : biomes.Average(BiomeInfo.BaseFertility);
            fertility = Math.Clamp(fertility + (riverTiles > 0 ? 10 : 0), 0, 100);

            var leyMean = tiles.Average(i =>
            {
                var c = grid.Coord(i);
                return ley.Fractal(c.X * 3.0 / grid.Width, c.Y * 3.0 / grid.Height, 3);
            });
            var mountainShare = (double)mountains / count;
            var leyPower = Math.Clamp((leyMean - 0.5) * 250 + 40 + mountainShare * 20, 0, 100);

            var biome = biomes.GroupBy(b => b).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            var baseDanger = Math.Clamp(BiomeInfo.BaseDanger(biome) + leyPower / 5, 0, 100);
            var temperature = tiles.Average(i => grid.Temperature[i] / 255.0);

            var region = new Region
            {
                Id = ids.Take(Region.Kind),
                Name = names.Region(),
                TileCount = count,
                Center = layout.Centers[r],
                Biome = biome,
                Climate = temperature < 0.35 ? ClimateBand.Cold : temperature < 0.65 ? ClimateBand.Temperate : ClimateBand.Hot,
                Coastal = coastTiles > 0,
                HasRiver = riverTiles > 0,
                Farmland = Math.Round(farmland, 2),
                MountainShare = Math.Round(mountainShare, 3),
                BaseFertility = Math.Round(fertility, 1),
                Fertility = Math.Round(fertility, 1),
                BaseDanger = Math.Round(baseDanger, 1),
                Danger = Math.Round(Math.Clamp(baseDanger + rng.NextInt(-5, 6), 0, 100), 1),
                BaseLey = Math.Round(leyPower, 1),
                LeyPower = Math.Round(leyPower, 1),
            };
            if (region.Id != r) throw new InvalidOperationException("Region ids must follow layout order.");

            AddDeposit(region, ResourceKinds.Grain, farmland);
            AddDeposit(region, ResourceKinds.Timber, CountOf(Biome.Forest, Biome.Jungle, Biome.Taiga));
            AddDeposit(region, ResourceKinds.Ore, CountOf(Biome.Hills) * 0.5 + mountains);
            AddDeposit(region, ResourceKinds.Stone, mountains * 0.8 + CountOf(Biome.Hills) * 0.5 + CountOf(Biome.Desert) * 0.2);
            AddDeposit(region, ResourceKinds.Horses, CountOf(Biome.Grassland) * 0.6 + CountOf(Biome.Savanna) * 0.5);
            AddDeposit(region, ResourceKinds.Fish, coastTiles + riverTiles * 0.5);
            if (mountainShare >= 0.1 && rng.Chance(0.15 + leyPower / 400))
                AddDeposit(region, ResourceKinds.Mithril, Math.Max(1, mountains * 0.2));
            if (biome is Biome.Desert or Biome.Tundra or Biome.Mountain && rng.Chance(0.08))
                AddDeposit(region, ResourceKinds.Dragonbone, 5);
            if (leyPower >= 65 && rng.Chance(0.4))
                AddDeposit(region, ResourceKinds.ArcaneCrystal, leyPower / 10);

            regions.Add(region);
        }
        return regions;
    }

    private static void AddDeposit(Region region, string kind, double capacity)
    {
        if (capacity < 2 && !ResourceKinds.IsRare(kind)) return;
        var rounded = Math.Round(capacity, 1);
        region.Resources[kind] = new ResourceDeposit { Capacity = rounded, Yield = rounded };
    }
}

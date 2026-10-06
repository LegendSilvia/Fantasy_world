using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Founds 1–4 settlements per region on the best sites (rivers, coasts, farmland), then ranks every
/// site worldwide: the best become cities (at most one per region, only where farmland can feed one),
/// then towns, villages, hamlets. Regions start with farmers within 80% of their farm capacity.
/// </summary>
public static class SettlementPlacer
{
    public const int MinSpacing = 4;
    public const double StartingFoodMonths = 6;
    public const double MinCityFarmland = 270;
    public const double StartingFarmLoad = 0.8;
    public const int MinPopulation = 30;

    private sealed class Draft
    {
        public required Region Region { get; init; }
        public required int Tile { get; init; }
        public required SettlementTier Tier { get; set; }
        public int Population { get; set; }
    }

    public static List<Settlement> Place(TerrainGrid grid, RegionLayout layout, IReadOnlyList<Region> regions,
        Rng rng, IdGen ids, NameGen names)
    {
        var meanFarmland = regions.Average(r => r.Farmland);
        var sites = new List<(Region Region, int Tile, double Score)>();
        foreach (var region in regions)
        {
            var wanted = Math.Clamp((int)Math.Round(region.Farmland / Math.Max(1, meanFarmland) * 2), 1, 4);
            var ranked = layout.Tiles[(int)region.Id]
                .Select(i => (Tile: i, Score: SiteScore(grid, i)))
                .OrderByDescending(s => s.Score).ThenBy(s => s.Tile)
                .ToList();
            var chosen = new List<int>();
            foreach (var (tile, score) in ranked)
            {
                if (chosen.Count >= wanted) break;
                var c = grid.Coord(tile);
                if (chosen.Any(t => grid.Coord(t).DistanceSquared(c) < MinSpacing * MinSpacing)) continue;
                chosen.Add(tile);
                sites.Add((region, tile, score + region.Fertility / 20 + rng.NextDouble()));
            }
        }

        var cityCount = Math.Clamp(regions.Count / 15, 2, 8);
        var drafts = new List<Draft>();
        var regionsWithCity = new HashSet<long>();
        var ordered = sites.OrderByDescending(s => s.Score).ThenBy(s => s.Tile).ToList();
        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var (region, tile, _) = ordered[rank];
            var share = (double)rank / ordered.Count;
            SettlementTier tier;
            if (regionsWithCity.Count < cityCount && region.Farmland >= MinCityFarmland && regionsWithCity.Add(region.Id))
                tier = SettlementTier.City;
            else tier = share < 0.2 ? SettlementTier.Town : share < 0.55 ? SettlementTier.Village : SettlementTier.Hamlet;
            drafts.Add(new Draft { Region = region, Tile = tile, Tier = tier });
        }

        foreach (var draft in drafts.OrderBy(d => d.Region.Id).ThenBy(d => d.Tile))
            draft.Population = draft.Tier switch
            {
                SettlementTier.City => rng.NextInt(6000, 10_000),
                SettlementTier.Town => rng.NextInt(1500, 3500),
                SettlementTier.Village => rng.NextInt(300, 900),
                _ => rng.NextInt(60, 250),
            };
        foreach (var group in drafts.GroupBy(d => d.Region.Id)) FitToFarmland(group.ToList());

        var settlements = new List<Settlement>();
        foreach (var draft in drafts.OrderBy(d => d.Region.Id).ThenBy(d => d.Tile))
        {
            var (region, tile, tier, population) = (draft.Region, draft.Tile, draft.Tier, draft.Population);
            var children = population * 35 / 100;
            var adults = population * 55 / 100;
            var settlement = new Settlement
            {
                Id = ids.Take(Settlement.Kind),
                Name = names.Settlement(),
                RegionId = region.Id,
                Tile = grid.Coord(tile),
                Tier = tier,
                Children = children,
                Adults = adults,
                Elders = population - children - adults,
                Wealth = population * 0.5,
                Walls = tier switch { SettlementTier.City => 2, SettlementTier.Town => 1, _ => 0 },
                Garrison = tier >= SettlementTier.Town ? population / 50 : 0,
                Unrest = 10 + rng.NextInt(0, 11),
                Buildings = StartingBuildings(tier, region, grid.Has(tile, TileFlags.Coast)),
            };
            region.Settlements.Add(settlement.Id);
            settlements.Add(settlement);
        }

        foreach (var region in regions)
            region.Food = Math.Round(settlements.Where(s => s.RegionId == region.Id).Sum(Demography.Consumption) * StartingFoodMonths);
        return settlements;
    }

    /// <summary>
    /// Shrinks a crowded region's settlements so its farmers fit the land. Tiers follow the new sizes,
    /// and smaller tiers farm more, so repeat until it fits (or every settlement is at the minimum).
    /// </summary>
    private static void FitToFarmland(IReadOnlyList<Draft> drafts)
    {
        var limit = Demography.FarmCapacity(drafts[0].Region) * StartingFarmLoad;
        for (var pass = 0; pass < 10; pass++)
        {
            var farmers = drafts.Sum(d => d.Population * 0.55 * Demography.FarmShare(d.Tier));
            if (farmers <= limit || drafts.All(d => d.Population == MinPopulation)) return;
            var scale = limit / farmers;
            foreach (var d in drafts)
            {
                d.Population = Math.Max(MinPopulation, (int)(d.Population * scale));
                d.Tier = Demography.TierFor(d.Population);
            }
        }
    }

    public static double SiteScore(TerrainGrid grid, int tile)
    {
        var biome = grid.BiomeAt(tile);
        var score = BiomeInfo.Farmland(biome) * 3;
        if (grid.Has(tile, TileFlags.River)) score += 3;
        if (grid.Has(tile, TileFlags.Coast)) score += 2;
        if (biome == Biome.Mountain) score -= 10;
        if (biome == Biome.Hills) score -= 1;
        return score;
    }

    private static List<string> StartingBuildings(SettlementTier tier, Region region, bool coastal)
    {
        var buildings = new List<string>();
        if (tier >= SettlementTier.Village) buildings.Add("shrine");
        if (tier >= SettlementTier.Town) buildings.AddRange(new[] { "market", "temple" });
        if (tier >= SettlementTier.Town && coastal) buildings.Add("harbor");
        if (tier >= SettlementTier.Village && region.Resources.ContainsKey(ResourceKinds.Ore)) buildings.Add("forge");
        if (tier == SettlementTier.City && region.LeyPower >= 60) buildings.Add("mage tower");
        buildings.Sort(StringComparer.Ordinal);
        return buildings;
    }
}

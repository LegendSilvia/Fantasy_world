using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class SettlementPlacerTests
{
    private static (TerrainGrid Grid, RegionLayout Layout, List<Region> Regions, List<Settlement> Settlements) Make(ulong seed = 2)
    {
        var grid = TerrainGenerator.Generate(seed, new TerrainOptions(Width: 128, Height: 128, MinIslandTiles: 60));
        Hydrology.Apply(grid);
        var layout = RegionPartitioner.Partition(grid, 30, new Rng(seed));
        var ids = new IdGen();
        var names = new NameGen(new Rng(seed + 2));
        var regions = RegionProfiler.Profile(grid, layout, seed, new Rng(seed + 1), ids, names);
        var settlements = SettlementPlacer.Place(grid, layout, regions, new Rng(seed + 3), ids, names);
        return (grid, layout, regions, settlements);
    }

    [Fact]
    public void EveryRegion_HasOneToFourSettlements()
    {
        var (_, _, regions, settlements) = Make();
        Assert.All(regions, r => Assert.InRange(r.Settlements.Count, 1, 4));
        Assert.Equal(settlements.Select(s => s.Id).OrderBy(i => i), regions.SelectMany(r => r.Settlements).OrderBy(i => i));
    }

    [Fact]
    public void Settlements_SitInsideTheirRegion_AndAreSpacedApart()
    {
        var (grid, layout, _, settlements) = Make();
        foreach (var s in settlements)
            Assert.Equal(s.RegionId, layout.RegionOf[grid.Index(s.Tile.X, s.Tile.Y)]);
        foreach (var group in settlements.GroupBy(s => s.RegionId))
            foreach (var a in group)
                foreach (var b in group.Where(b => b.Id > a.Id))
                    Assert.True(a.Tile.DistanceSquared(b.Tile) >= SettlementPlacer.MinSpacing * SettlementPlacer.MinSpacing);
    }

    [Fact]
    public void Tiers_IncludeCities_AtMostOnePerRegion()
    {
        var (_, _, _, settlements) = Make();
        var cities = settlements.Where(s => s.Tier == SettlementTier.City).ToList();
        Assert.InRange(cities.Count, 1, 8);
        Assert.Equal(cities.Count, cities.Select(c => c.RegionId).Distinct().Count());
        Assert.Contains(settlements, s => s.Tier == SettlementTier.Hamlet);
        Assert.All(settlements, s => Assert.Equal(s.Tier, Demography.TierFor(s.Population)));
    }

    [Fact]
    public void Regions_StartWithFarmersTheLandCanEmploy()
    {
        var (_, _, regions, settlements) = Make();
        foreach (var r in regions)
        {
            var farmers = settlements.Where(s => s.RegionId == r.Id).Sum(Demography.Farmers);
            Assert.True(farmers <= Demography.FarmCapacity(r) * SettlementPlacer.StartingFarmLoad + 30,
                $"{r.Name}: {farmers:0} farmers for capacity {Demography.FarmCapacity(r):0}");
        }
    }

    [Fact]
    public void Regions_StartWithSixMonthsOfFood()
    {
        var (_, _, regions, settlements) = Make();
        foreach (var r in regions)
        {
            var need = settlements.Where(s => s.RegionId == r.Id).Sum(Demography.Consumption);
            Assert.InRange(r.Food, need * 6 - 1, need * 6 + 1);
        }
    }

    [Fact]
    public void SiteScore_FavoursRiversAndCoasts()
    {
        var grid = new TerrainGrid(3, 1);
        grid.SetBiome(0, Biome.Grassland);
        grid.SetBiome(1, Biome.Grassland);
        grid.Set(1, TileFlags.River);
        grid.SetBiome(2, Biome.Mountain);
        Assert.True(SettlementPlacer.SiteScore(grid, 1) > SettlementPlacer.SiteScore(grid, 0));
        Assert.True(SettlementPlacer.SiteScore(grid, 0) > SettlementPlacer.SiteScore(grid, 2));
    }
}

using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Hand-built worlds for system tests: two regions, one settlement each.</summary>
public static class TestWorlds
{
    public static World TwoRegions(ulong seed = 1)
    {
        var world = WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch);
        AddRegion(world, "Westmark");
        AddRegion(world, "Eastmoor");
        AddSettlement(world, 1, SettlementTier.Village, 1000);
        AddSettlement(world, 2, SettlementTier.Village, 1000);
        return world;
    }

    public static Region AddRegion(World world, string name)
    {
        var region = new Region
        {
            Id = world.Ids.Take(Region.Kind),
            Name = name,
            TileCount = 100,
            Biome = Biome.Grassland,
            Climate = ClimateBand.Temperate,
            Farmland = 100,
            BaseFertility = 60,
            Fertility = 60,
            BaseDanger = 20,
            Danger = 20,
            BaseLey = 30,
            LeyPower = 30,
        };
        world.Regions[region.Id] = region;
        return region;
    }

    public static Settlement AddSettlement(World world, long regionId, SettlementTier tier, int population)
    {
        var settlement = new Settlement
        {
            Id = world.Ids.Take(Settlement.Kind),
            Name = $"Town{world.Settlements.Count + 1}",
            RegionId = regionId,
            Tier = tier,
            Children = population * 35 / 100,
            Adults = population * 55 / 100,
            Elders = population - population * 35 / 100 - population * 55 / 100,
            Wealth = 100,
            Unrest = 10,
        };
        world.Settlements[settlement.Id] = settlement;
        var region = world.Regions[regionId];
        region.Settlements.Add(settlement.Id);
        region.Food = Demography.Consumption(settlement) * 6 + region.Food;
        return settlement;
    }
}

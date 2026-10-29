using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class StocksTests
{
    private static (SimContext Ctx, Realm Realm) Realm(double oreYield = 12)
    {
        var world = TestRealms.TwoKingdoms();
        var realm = world.Realms[1];
        var region = world.Regions[world.Settlements[realm.CapitalId].RegionId];
        region.Resources.Clear();
        if (oreYield > 0) region.Resources[ResourceKinds.Ore] = new ResourceDeposit { Capacity = oreYield, Yield = oreYield };
        return (new SimContext(world, new EventLog(), new EventQueue(), new Rng(1)), realm);
    }

    [Fact]
    public void AMonthsYield_FillsTheStock_UpToTwoYearsOfIt()
    {
        var (ctx, realm) = Realm();
        Stocks.Fill(ctx, realm);
        Assert.Equal(1, Stocks.Stock(realm, ResourceKinds.Ore), 3);
        for (var month = 0; month < 40; month++) Stocks.Fill(ctx, realm);
        Assert.Equal(12 * Stocks.YearsKept, Stocks.Stock(realm, ResourceKinds.Ore), 3);
        Assert.False(realm.Stocks.ContainsKey(ResourceKinds.Horses));
    }

    [Fact]
    public void WantingMoreThanTheStock_BeginsAShortage_ThatEndsWhenTheStockRecovers()
    {
        var (ctx, realm) = Realm();
        realm.Stocks[ResourceKinds.Ore] = 2;
        Assert.True(Stocks.Use(ctx, realm, ResourceKinds.Ore, 1));
        Assert.False(Stocks.Use(ctx, realm, ResourceKinds.Ore, 5));
        Assert.True(Stocks.Short(realm, ResourceKinds.Ore));
        Assert.Equal(0, Stocks.Stock(realm, ResourceKinds.Ore));
        var began = Assert.Single(ctx.Log.All, e => e.Type == "ResourceShortageBegan");
        Assert.Equal(ResourceKinds.Ore, began.Payload["resource"]);

        Assert.False(Stocks.Use(ctx, realm, ResourceKinds.Ore, 5));
        Assert.Single(ctx.Log.All, e => e.Type == "ResourceShortageBegan");

        ctx.World.Armies[99] = new Army { Id = 99, RealmId = realm.Id };
        for (var month = 0; month < 2; month++) Stocks.Fill(ctx, realm);
        Assert.True(Stocks.Short(realm, ResourceKinds.Ore));
        Stocks.Fill(ctx, realm);
        Assert.False(Stocks.Short(realm, ResourceKinds.Ore));
        var ended = Assert.Single(ctx.Log.All, e => e.Type == "ResourceShortageEnded");
        Assert.Equal(began.Id, ended.Causes.Single().EventId);
    }

    [Fact]
    public void ShortOfOre_AHostFightsAtThreeQuarters_AndHorsesInHandAddATenth()
    {
        var (ctx, realm) = Realm();
        Assert.Equal(1, Stocks.Arms(ctx.World, realm));
        realm.Stocks[ResourceKinds.Horses] = 5;
        Assert.True(Stocks.Mounted(realm));
        Assert.Equal(1.1, Stocks.Arms(ctx.World, realm), 6);
        Stocks.Use(ctx, realm, ResourceKinds.Ore, 1);
        Assert.Equal(0.75 * 1.1, Stocks.Arms(ctx.World, realm), 6);
    }

    [Fact]
    public void Building_BuysWhatTheStockLacks_FromTheTreasury_AndIsShortOnlyWhenItCannotPay()
    {
        var (ctx, realm) = Realm(0);
        realm.Treasury = Stocks.BuildingCost * Stocks.BuyingPrice * 1.5;
        Assert.True(Stocks.CanBuild(realm, ResourceKinds.Stone));
        Assert.True(Stocks.Supply(ctx, realm, ResourceKinds.Stone, Stocks.BuildingCost));
        Assert.Equal(Stocks.BuildingCost * Stocks.BuyingPrice * 0.5, realm.Treasury, 6);
        Assert.False(Stocks.CanBuild(realm, ResourceKinds.Stone));
        Assert.False(Stocks.Supply(ctx, realm, ResourceKinds.Stone, Stocks.BuildingCost));
        Assert.True(Stocks.Short(realm, ResourceKinds.Stone));
        Assert.Equal(0, realm.Treasury, 6);
    }

    [Fact]
    public void Plenty_SteadiesTheRealm_AndShortagesUnsettleIt()
    {
        var (ctx, realm) = Realm();
        Assert.Equal(0, Stocks.Contentment(ctx.World, realm));
        realm.Stocks[ResourceKinds.Ore] = 12;
        Assert.Equal(2, Stocks.Contentment(ctx.World, realm));
        Stocks.Use(ctx, realm, ResourceKinds.Horses, 1);
        Assert.Equal(2 - 6, Stocks.Contentment(ctx.World, realm));
        Assert.Equal([ResourceKinds.Horses], Stocks.Shortages(realm));
    }
}

using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class TradeSystemTests
{
    /// <summary>A tiny world with exactly two markets in neighbouring regions of different realms.</summary>
    private static (Simulator Sim, Settlement A, Settlement B) TwoMarkets()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var link = world.Geography!.Graph.Links.First(l => Realms.SovereignOf(world, world.Regions[l.A]) is { } x && Realms.SovereignOf(world, world.Regions[l.B]) is { } y
                                                          && x.Id != y.Id
                                                          && world.LivingSettlements(world.Regions[l.A]).Any() && world.LivingSettlements(world.Regions[l.B]).Any());
        foreach (var s in world.Settlements.Values) s.Tier = SettlementTier.Village;
        var a = world.LivingSettlements(world.Regions[link.A]).First();
        var b = world.LivingSettlements(world.Regions[link.B]).First();
        foreach (var (s, wealth) in new[] { (a, 4000.0), (b, 1000.0) }) (s.Tier, s.Wealth) = (SettlementTier.Town, wealth);
        foreach (var region in world.Regions.Values) region.Danger = 0;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new TradeSystem() }, checkInvariants: true);
        return (sim, a, b);
    }

    [Fact]
    public void MarketsAtPeace_OpenARoad_ThatEnrichesBoth()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = Assert.Single(sim.World.Routes.Values);
        Assert.Equal((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), (route.FromId, route.ToId));
        Assert.Equal(sim.World.Settlements[route.FromId].RegionId, route.Regions[0]);
        Assert.Equal(sim.World.Settlements[route.ToId].RegionId, route.Regions[^1]);
        Assert.Equal(RouteState.Open, route.State);
        Assert.True(route.Volume > 0);
        Assert.True(b.Wealth > 1000);
        var opened = sim.Log.Get(route.OpenedEventId);
        Assert.Equal("RouteOpened", opened.Type);
        Assert.Equal(route.Ref, opened.Actors["route"]);
    }

    [Fact]
    public void War_SeversTheRoad_AndPeaceReopensIt()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = sim.World.Routes.Values.Single();
        var world = sim.World;
        var (x, y) = (Realms.SovereignOf(world, a)!.Id, Realms.SovereignOf(world, b)!.Id);
        var cb = sim.Log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = sim.Log.Append(world.Date, new EventDraft("WarDeclared", Causes: new[] { new Cause(cb.Id, CauseKind.Motive, 0.9) }));
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Toll War", AttackerId = x, DefenderId = y, Attackers = { x }, Defenders = { y },
            CasusBelli = CasusBelli.Revenge, CasusBelliEventId = cb.Id, DeclaredEventId = declared.Id, StartDay = world.Date.Day,
        };
        world.Wars[war.Id] = war;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Severed, route.State);
        var severed = sim.Log.Get(route.SeveredEventId);
        Assert.Equal("war", severed.Payload["reason"]);
        Assert.Equal(declared.Id, severed.Causes.Single().EventId);
        Assert.Equal(0, route.Volume);

        war.Ended = true;
        war.EndedEventId = sim.Log.Append(world.Date, new EventDraft("PeaceSigned")).Id;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Open, route.State);
        Assert.Equal(route.SeveredEventId, sim.Log.All.Last(e => e.Type == "RouteRestored").Causes.Single().EventId);
    }

    [Fact]
    public void AnAbandonedMarket_ClosesItsRoads()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        foreach (var c in sim.World.Characters.Values.Where(c => c.SettlementId == a.Id)) c.SettlementId = b.Id;
        if (a.LordId != 0) a.LordId = 0;
        a.Abandoned = true;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Contains(sim.Log.All, e => e.Type == "RouteClosed");
    }

    [Fact]
    public void AMarketThatShrinksBelowATown_ClosesItsRoads()
    {
        var (sim, a, _) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        a.Tier = SettlementTier.Village;
        sim.AdvanceDays(31);
        Assert.Equal(RouteState.Closed, sim.World.Routes.Values.Single().State);
        Assert.Equal(0, sim.World.Routes.Values.Single().Volume);
    }

    [Fact]
    public void TheMap_ShowsRoadsAsTheyWere()
    {
        var (sim, a, b) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var route = sim.World.Routes.Values.Single();
        var opened = sim.Log.Get(route.OpenedEventId).Date;
        var map = FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log);
        var road = Assert.Single(map.Routes!);
        Assert.Equal(route.Ref, road.Ref);
        Assert.Equal("open", road.State);
        Assert.Equal(FantasyWorld.Sim.Views.MapPoint.Of(sim.World, sim.World.Settlements[route.FromId].Ref), road.Path[0]);
        Assert.Equal(FantasyWorld.Sim.Views.MapPoint.Of(sim.World, sim.World.Settlements[route.ToId].Ref), road.Path[^1]);
        Assert.Empty(FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log, opened.AddDays(-1)).Routes!);
    }

    [Fact]
    public void Roads_FollowTheGround_FromMarketToMarket()
    {
        var (sim, _, _) = TwoMarkets();
        sim.AdvanceDays(360 * 40);
        var map = FantasyWorld.Sim.Views.MapScreen.Build(sim.World, sim.Log);
        var road = Assert.Single(FantasyWorld.Client.RouteLayout.Roads(map, sim.World.Geography!));
        Assert.True(road.Open);
        Assert.True(road.Line.Count > 4);
        var grid = sim.World.Geography!.Terrain;
        Assert.All(road.Line, p => Assert.Equal(Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(grid, (p.X + 1) / 2, (p.Z + 1) / 2)) + FantasyWorld.Client.RouteLayout.Lift, p.Y, 3));
        var start = FantasyWorld.Client.Projection.ToBlock(grid, map.Routes![0].Path[0], 0);
        Assert.True(Math.Abs(road.Line[0].X - start.X) < 1e-4 && Math.Abs(road.Line[0].Z - start.Z) < 1e-4);
    }
}

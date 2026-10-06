using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class MonsterSystemTests
{
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static Monster Stage(World world, EventLog log, long region, double might)
    {
        var id = world.Ids.Take(Monster.Kind);
        var rose = log.Append(world.Date, new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
            Place: world.Regions[region].Ref));
        var beast = new Monster { Id = id, Name = "The Wyrm of Esk", Breed = "wyrm", RegionId = region, Might = might, RoseEventId = rose.Id };
        world.Monsters[beast.Id] = beast;
        return beast;
    }

    [Fact]
    public void ABeastRises_InDangerousLand_OfABreedThatFits()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 95;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 80);
        var rose = sim.Log.All.First(e => e.Type == "MonsterRose");
        var beast = world.Monsters[rose.Actors["monster"].Id];
        Assert.Equal(2, beast.RegionId);
        Assert.Contains(beast.Breed, Monster.BreedsOf(world.Regions[2].Biome));
        Assert.InRange(beast.Might, 1, 100);
        Assert.True(world.Regions[2].Danger >= beast.Might / 2 || !beast.Alive);
    }

    [Fact]
    public void AMightyBeast_Raids_UntilItBecomesALegend()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var town = world.Settlements[1];
        var people = town.Adults;
        var beast = Stage(world, log, town.RegionId, 90);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MonsterSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 20);
        var raids = sim.Log.All.Where(e => e.Type == "MonsterRaid").ToList();
        Assert.NotEmpty(raids);
        Assert.All(raids, r => Assert.Equal(beast.Ref, r.Actors["monster"]));
        Assert.True(town.Adults < people || raids.All(r => r.Place != town.Ref));
        Assert.True(beast.Legendary);
        Assert.Contains(sim.Log.All, e => e.Type == "MonsterLegendary");
    }

    [Fact]
    public void AHero_HuntsTheBeastByName_AndSlaysIt()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var beast = Stage(world, log, 2, 70);
        world.Regions[2].Danger = 60;
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("hunt"), new FixedArbiter("success")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 3);
        var slain = sim.Log.All.First(e => e.Type == "MonsterSlain");
        Assert.Equal(hunter.Ref, slain.Actors["hunter"]);
        Assert.Equal(beast.Ref, slain.Actors["monster"]);
        Assert.Equal((MonsterState.Slain, slain.Id, hunter.Id), (beast.State, beast.SlainEventId, beast.SlayerId));
        Assert.True(hunter.Renown > 0);
        Assert.Contains(world.Artifacts.Values, a => a.HolderId == hunter.Id && a.OriginEventId == slain.Id);
    }

    [Fact]
    public void AFailedHunt_FeedsTheBeastsNotoriety()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var beast = Stage(world, log, 2, 70);
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = new Simulator(world, log, new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider("hunt"), new FixedArbiter("failure_wounded")) }, checkInvariants: true);
        sim.AdvanceDays(360 * 5);
        Assert.Contains(sim.Log.All, e => e.Type == "MonsterHuntFailed" && e.Actors["monster"] == beast.Ref);
        Assert.True(beast.Notoriety >= 8);
        Assert.True(beast.Alive);
    }

    [Fact]
    public void ARaid_SeversTheRoadsThroughItsLand_ForASeason()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        foreach (var town in world.Settlements.Values) town.Tier = SettlementTier.Town;
        var opened = log.Append(world.Date, new EventDraft("RouteOpened"));
        var road = new TradeRoute { Id = world.Ids.Take(TradeRoute.Kind), Name = "The Road", FromId = 1, ToId = 2, Regions = { 1, 2 }, State = RouteState.Open, OpenedEventId = opened.Id };
        world.Routes[road.Id] = road;
        var beast = Stage(world, log, 1, 90);
        var sim = new Simulator(world, log, new EventQueue(), new ISimSystem[] { new MonsterSystem(), new TradeSystem() }, checkInvariants: true);
        while (road.State == RouteState.Open) sim.AdvanceDays(30);
        var cut = sim.Log.Get(road.SeveredEventId);
        Assert.Equal("raid", cut.Payload["reason"]);
        Assert.Equal("MonsterRaid", sim.Log.Get(cut.Causes.Single().EventId).Type);
        (beast.State, beast.SlainEventId) = (MonsterState.Slain, cut.Id);
        sim.AdvanceDays(360);
        var reopened = sim.Log.All.Single(e => e.Type == "RouteRestored");
        Assert.True(reopened.Date.Day - cut.Date.Day >= TradeSystem.RaidDays);
    }
}

using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FaithSystemTests
{
    private static Faith AddFaith(World world, string name, long holyRegion, params string[] tenets)
    {
        var faith = new Faith { Id = world.Ids.Take(Faith.Kind), Name = name, Deity = name, HolyRegionId = holyRegion, Tenets = tenets.ToList() };
        world.Faiths[faith.Id] = faith;
        return faith;
    }

    private static Simulator Run(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new FaithSystem() }, checkInvariants: true);

    [Fact]
    public void ARealmsFaith_SpreadsToItsLands_BecauseTheRealmTookThem()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Place: world.Regions[1].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 200);
        var turned = sim.Log.All.First(e => e.Type == "RegionConverted" && e.Place == world.Regions[1].Ref);
        Assert.Equal(sun.Ref, turned.Actors["faith"]);
        Assert.Contains(turned.Causes, c => c.EventId == ceded.Id);
    }

    [Fact]
    public void APeopleThatTurned_KeepTheirNewFaithForAGeneration()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        world.Regions[2].FaithId = sun.Id; // each realm's people hold the other's faith, so both lands keep being pulled
        var sim = Run(world);
        sim.AdvanceDays(360 * 300);
        Assert.True(sim.Log.All.Count(e => e.Type == "RegionConverted") >= 3);
        foreach (var region in world.Regions.Values)
        {
            var turns = sim.Log.Involving(region.Ref).Where(e => e.Type == "RegionConverted").ToList();
            for (var i = 1; i < turns.Count; i++)
                Assert.True(turns[i].Date.Day - turns[i - 1].Date.Day >= FaithSystem.SettleYears * 360L);
        }
    }

    [Fact]
    public void Temples_HoldThePeopleToTheirFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Realms[1].FaithId = sun.Id;
        var (faith, bare) = FaithSystem.Odds(world, world.Regions[1]);
        world.Settlements[1].Buildings.AddRange(new[] { "shrine", "temple" });
        var (_, kept) = FaithSystem.Odds(world, world.Regions[1]);
        Assert.Equal(sun.Id, faith);
        Assert.True(kept < bare);
    }

    [Fact]
    public void NeighbourPressure_IsWeakerForTolerantFaiths()
    {
        var world = TestPeople.World();
        var zealots = AddFaith(world, "The Iron Word", 2, "zealous");
        var tolerant = AddFaith(world, "The Open Hand", 2, "tolerant");
        world.Regions[2].FaithId = zealots.Id;
        var strong = FaithSystem.Pressure(world, world.Regions[1])[zealots.Id];
        world.Regions[2].FaithId = tolerant.Id;
        var weak = FaithSystem.Pressure(world, world.Regions[1])[tolerant.Id];
        Assert.True(weak < strong);
    }

    [Fact]
    public void ARealmWhoseLandsHaveTurned_TakesUpTheNewFaith()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 1);
        world.Regions[2].OwnerRealmId = 1; // realm 1 holds both lands
        world.Realms[2].Destroyed = true;
        world.Regions[1].FaithId = world.Regions[2].FaithId = sun.Id;
        var sim = Run(world);
        sim.AdvanceDays(360 * 30);
        var converted = sim.Log.All.First(e => e.Type == "RealmConverted");
        Assert.Equal(world.Realms[1].Ref, converted.Actors["realm"]);
        Assert.Equal(sun.Id, world.Realms[1].FaithId);
        Assert.Equal(sun.Id, Realms.RulerOf(world, world.Realms[1])!.FaithId);
    }

    [Fact]
    public void LosingTheHolyLand_RaisesZeal_AndRegainingItIsRecorded()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        world.Realms[2].FaithId = 1; // the holy land of the Sun Creed is held by the Way of Ohm, since it was ceded
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        var lost = sim.Log.All.Single(e => e.Type == "HolySiteLost");
        Assert.Equal(sun.Ref, lost.Actors["faith"]);
        Assert.Contains(lost.Causes, c => c.EventId == ceded.Id);
        Assert.Equal(lost.Id, sun.HolySiteLostEventId);
        Assert.True(sun.Zeal >= FaithSystem.LossZeal - 3);

        world.Realms[2].FaithId = sun.Id;
        sim.AdvanceDays(360);
        var regained = sim.Log.All.Single(e => e.Type == "HolySiteRegained");
        Assert.Contains(regained.Causes, c => c.EventId == lost.Id);
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandNeverHeld_IsNotALoss()
    {
        var world = TestRealms.TwoKingdoms();
        var sun = AddFaith(world, "The Sun Creed", 2, "zealous");
        var sim = Run(world);
        sim.AdvanceDays(360 * 3);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteLost");
        Assert.Equal(0, sun.HolySiteLostEventId);
    }

    [Fact]
    public void AHolyLandLeftToNoRealm_IsNotRegained()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var sun = AddFaith(world, "The Sun Creed", 2);
        world.Realms[2].FaithId = 1;
        var ceded = log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["to"] = world.Realms[2].Ref }, Place: world.Regions[2].Ref));
        var sim = Run(world, log);
        sim.AdvanceDays(360);
        Assert.NotEqual(0, sun.HolySiteLostEventId);
        (world.Regions[2].OwnerRealmId, world.Realms[2].Destroyed) = (0, true); // its holder fell
        sim.AdvanceDays(360);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "HolySiteRegained");
        Assert.NotEqual(0, sun.HolySiteLostEventId);
    }
}

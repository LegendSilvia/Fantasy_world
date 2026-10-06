using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class MagicSystemTests
{
    private static Simulator Run(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new MagicSystem() }, checkInvariants: true);

    /// <summary>Region 1's town gets a tower; a surge is under way there.</summary>
    private static (World World, EventLog Log, WorldEvent Surge) Surging(MagicLaw law)
    {
        var world = TestRealms.TwoKingdoms();
        foreach (var realm in world.Realms.Values) realm.MagicLaw = law;
        world.Settlements[1].Buildings.Add("mage tower");
        var log = new EventLog();
        var surge = log.Append(world.Date, new EventDraft("LeySurge", Place: world.Regions[1].Ref));
        world.Regions[1].Conditions[Conditions.LeySurge] = surge.Id;
        return (world, log, surge);
    }

    [Fact]
    public void LeyPower_RisesAndFallsInASlowTide()
    {
        var world = TestPeople.World();
        var region = world.Regions[1];
        var tides = Enumerable.Range(0, MagicSystem.TideYears).Select(y => MagicSystem.Tide(region, new WorldDate(y * 360L))).ToList();
        Assert.InRange(tides.Max() - tides.Min(), MagicSystem.TideAmplitude, MagicSystem.TideAmplitude * 2 + 0.01);
        Assert.Equal(MagicSystem.Tide(region, new WorldDate(0)), MagicSystem.Tide(region, new WorldDate(MagicSystem.TideYears * 360L)), 6);
        Assert.NotEqual(MagicSystem.Tide(world.Regions[1], new WorldDate(0)), MagicSystem.Tide(world.Regions[2], new WorldDate(0)));
    }

    [Fact]
    public void ASurgeAtATower_GoesWild_AndTheRealmTightensItsLaw()
    {
        var (world, log, surge) = Surging(MagicLaw.Embraced);
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 30);
        var wild = sim.Log.All.First(e => e.Type == "WildMagic");
        Assert.Equal(world.Settlements[1].Ref, wild.Place);
        Assert.Contains(wild.Causes, c => c.EventId == surge.Id);
        Assert.Contains(wild.Payload["kind"], new[] { "storm", "blight", "portal" });
        var law = sim.Log.All.First(e => e.Type == "MagicLawChanged");
        Assert.Contains(law.Causes, c => c.EventId == wild.Id || sim.Log.Get(c.EventId).Type == "WildMagic");
        Assert.NotEqual(MagicLaw.Embraced, world.Realms[1].MagicLaw);
    }

    [Fact]
    public void WildMagic_IsRarerWhereMagicIsLicensed()
    {
        Assert.True(MagicSystem.WildOdds(MagicLaw.Licensed) < MagicSystem.WildOdds(MagicLaw.Embraced));
        Assert.True(MagicSystem.WildOdds(MagicLaw.Outlawed) < MagicSystem.WildOdds(MagicLaw.Licensed));
    }

    [Fact]
    public void ATower_TrainsMages_WhereTheLawAllows()
    {
        var world = TestRealms.TwoKingdoms();
        world.Settlements[1].Buildings.Add("mage tower");
        var pupil = TestPeople.Add(world, 1, 25);
        pupil.Attributes.Lore = 12;
        var sim = Run(world);
        sim.AdvanceDays(360 * 20);
        var trained = sim.Log.All.First(e => e.Type == "MageTrained");
        Assert.Equal(pupil.Ref, trained.Actors["mage"]);
        Assert.Equal(Role.Mage, pupil.Role);

        var banned = TestRealms.TwoKingdoms();
        banned.Realms[1].MagicLaw = MagicLaw.Outlawed;
        banned.Settlements[1].Buildings.Add("mage tower");
        TestPeople.Add(banned, 1, 25).Attributes.Lore = 12;
        var none = Run(banned);
        none.AdvanceDays(360 * 20);
        Assert.DoesNotContain(none.Log.All, e => e.Type == "MageTrained");
    }

    [Fact]
    public void WhereMagicIsOutlawed_MagesFleeToAFreerRealm()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Realms[1].MagicLaw = MagicLaw.Outlawed;
        world.Realms[2].MagicLaw = MagicLaw.Licensed;
        var banned = log.Append(world.Date, new EventDraft("MagicLawChanged", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref }));
        var mage = TestPeople.Add(world, 1, 30, role: Role.Mage);
        var sim = Run(world, log);
        sim.AdvanceDays(360 * 40);
        var fate = sim.Log.All.First(e => e.Type == "MageFled" || e.Type == "CharacterDied" && e.Actors["deceased"] == mage.Ref);
        Assert.Contains(fate.Causes, c => c.EventId == banned.Id);
        if (fate.Type == "MageFled") Assert.Equal(2, mage.SettlementId);
        else Assert.Equal("burned for sorcery", mage.DeathCause);
    }

    [Fact]
    public void TowersAreNotBuilt_WhereMagicIsOutlawed()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].MagicLaw = MagicLaw.Outlawed;
        var town = world.Settlements[1];
        (town.Tier, town.Wealth, town.Walls) = (SettlementTier.City, 1_000_000, 3);
        town.Buildings.AddRange(new[] { "granary", "guildhall", "market", "temple" });
        world.Regions[1].LeyPower = 90;
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new SettlementSystem() }, checkInvariants: true);
        sim.AdvanceDays(360 * 3);
        Assert.DoesNotContain("mage tower", town.Buildings);
    }
}

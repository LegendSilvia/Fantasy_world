using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class RealmModelTests
{
    [Fact]
    public void TwoKingdoms_SatisfyInvariants()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Empty(Invariants.Check(world, new EventLog()));
        Assert.Equal(2, Realms.Independent(world).Count());
    }

    [Fact]
    public void Ruler_IsTheLordOfTheCapital()
    {
        var world = TestRealms.TwoKingdoms();
        var realm = world.Realms[1];
        Assert.Equal(world.Settlements[1].LordId, Realms.RulerOf(world, realm)!.Id);
        world.Characters[world.Settlements[1].LordId].Role = Role.None;
        world.Settlements[1].LordId = 0;
        Assert.Null(Realms.RulerOf(world, realm));
    }

    [Fact]
    public void Vassals_AddToTerritory_AndTopIsTheIndependentLiege()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[2].LiegeId = 1;
        Assert.Equal(1, Realms.Top(world, world.Realms[2]).Id);
        Assert.Equal(new long[] { 1, 2 }, Realms.Territory(world, world.Realms[1]).Select(r => r.Id));
        Assert.Single(Realms.Domain(world, world.Realms[1]));
        Assert.Equal(1, Realms.SovereignOf(world, world.Regions[2])!.Id);
        Assert.Single(Realms.Independent(world));
    }

    [Fact]
    public void Neighbours_AreFoundAcrossTheGraph()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Equal(2, Realms.Neighbours(world, world.Realms[1]).Single().Id);
    }

    [Fact]
    public void Invariants_CatchBrokenRealms()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].LiegeId = 2;
        world.Realms[2].LiegeId = 1;
        world.Regions[1].OwnerRealmId = 2;
        world.Realms[2].Council[CouncilSeats.Marshal] = 999;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("cycle"));
        Assert.Contains(errors, e => e.Contains("outside its domain"));
        Assert.Contains(errors, e => e.Contains("inactive marshal"));
    }

    [Fact]
    public void Snapshot_RoundTripsRealmsWarsAndArmies()
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].Treaties.Add(new Treaty(TreatyKind.Alliance, 2, 0, 0));
        world.Realms[1].Council[CouncilSeats.Steward] = world.Settlements[1].LordId;
        world.Wars[1] = new War { Id = 1, Name = "The Border War", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 }, CasusBelli = CasusBelli.Revenge };
        world.Armies[1] = new Army { Id = 1, RealmId = 1, WarId = 1, Levies = 300, RegionId = 1, Path = { 2 }, RaisedFrom = { [1] = 300 } };
        var queue = new EventQueue();
        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options);
        var copy = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!.ToWorld();
        Assert.Equal(json, JsonSerializer.Serialize(WorldSnapshot.Capture(copy, queue), SimJson.Options));
        Assert.True(copy.Realms[1].Has(TreatyKind.Alliance, 2));
        Assert.Equal(300, copy.Armies[1].RaisedFrom[1]);
        Assert.Same(StringComparer.Ordinal, copy.Realms[1].Council.Comparer);
    }

    [Fact]
    public void Rank_FollowsSize()
    {
        var world = TestRealms.TwoKingdoms();
        Assert.Equal(RealmRank.County, Realms.RankFor(world, world.Realms[1]));
    }
}

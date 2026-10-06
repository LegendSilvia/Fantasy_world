using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RealmGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_StartsWithSixToEightKingdoms_AndTheirCounties()
    {
        var world = Medium();
        Assert.InRange(Realms.Independent(world).Count(), 6, 8);
        Assert.True(world.Realms.Values.Count(r => r.LiegeId != 0) >= 10);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryRegion_BelongsToARealm()
    {
        var world = Medium();
        Assert.All(world.Regions.Values, r => Assert.NotNull(Realms.SovereignOf(world, r)));
    }

    [Fact]
    public void Kingdoms_AreRuledByCityLords_AndCountiesByTownLords()
    {
        var world = Medium();
        foreach (var realm in world.Realms.Values)
        {
            var capital = world.Settlements[realm.CapitalId];
            Assert.Equal(realm.LiegeId == 0 ? SettlementTier.City : SettlementTier.Town, capital.Tier);
            Assert.Equal(capital.LordId, Realms.RulerOf(world, realm)!.Id);
            Assert.Equal(2, realm.Colors.Distinct().Count());
        }
    }

    [Fact]
    public void Kingdoms_HoldContiguousTerritories()
    {
        var world = Medium();
        var graph = world.Geography!.Graph;
        foreach (var kingdom in Realms.Independent(world))
        {
            var mine = Realms.Territory(world, kingdom).Select(r => r.Id).ToHashSet();
            var start = world.Settlements[kingdom.CapitalId].RegionId;
            var seen = new HashSet<long> { start };
            var queue = new Queue<long>(seen);
            while (queue.Count > 0)
                foreach (var n in graph.Neighbours(queue.Dequeue()))
                    if (mine.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(mine.Count, seen.Count);
            Assert.Equal(RealmRank.Kingdom, kingdom.Rank);
        }
    }

    [Fact]
    public void Laws_FollowFaithAndGovernment()
    {
        var world = Medium();
        foreach (var realm in world.Realms.Values)
        {
            if (realm.Government == Government.Magocracy) Assert.Equal(MagicLaw.Embraced, realm.MagicLaw);
            if (realm.Government == Government.Tribal) Assert.Equal(SuccessionLaw.Seniority, realm.SuccessionLaw);
        }
    }

    [Fact]
    public void TinyWorlds_CrownTheirLargestTowns()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.Equal(2, Realms.Independent(world).Count());
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Theory]
    [InlineData(14UL)]
    [InlineData(17UL)]
    [InlineData(49UL)]
    [InlineData(57UL)]
    public void TinyWorlds_WithTwoTownsInOneRegion_CrownOneOfThem(ulong seed)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var capitals = Realms.Independent(world).Select(r => world.Settlements[r.CapitalId].RegionId).ToList();
        Assert.NotEmpty(capitals);
        Assert.Equal(capitals.Count, capitals.Distinct().Count());
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }
}

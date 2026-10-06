using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class HistoryTests
{
    private sealed record Seen(WorldDate Date, Dictionary<long, (long Owner, long Occupier)> Regions, Dictionary<long, long> Lieges,
        HashSet<long> Fallen, Dictionary<long, (long[] Attackers, long[] Defenders)> Wars);

    [Theory]
    [InlineData(2UL, 140)]
    [InlineData(7UL, 60)]
    public void ThePoliticalWorld_ReplaysToWhatItWas(ulong seed, int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        Assert.Equal(world.Regions.Values.Where(r => r.OwnerRealmId != 0).Select(r => (r.Id, r.OwnerRealmId)),
            world.FoundingOwners.Select(kv => (kv.Key, kv.Value)));
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        var seen = new List<Seen>();
        for (var year = 0; year < years; year += 2)
        {
            sim.AdvanceDays(720);
            seen.Add(new Seen(world.Date,
                world.Regions.Values.ToDictionary(r => r.Id, r => (r.OwnerRealmId, r.OccupierRealmId)),
                world.Realms.Values.Where(r => !r.Destroyed).ToDictionary(r => r.Id, r => r.LiegeId),
                world.Realms.Values.Where(r => r.Destroyed).Select(r => r.Id).ToHashSet(),
                world.Wars.Values.Where(w => !w.Ended).ToDictionary(w => w.Id, w => (w.Attackers.ToArray(), w.Defenders.ToArray()))));
        }
        Assert.Contains(sim.Log.All, e => e.Type == "RegionCeded");
        if (seed == 2) Assert.Contains(sim.Log.All, e => e.Type == "RealmFell");

        foreach (var past in seen.Take(seen.Count - 1))
        {
            var map = History.PoliticalMapAt(world, sim.Log, past.Date);
            Assert.True(map.Historical);
            Assert.All(past.Regions, kv => Assert.Equal(kv.Value, (map.Regions[kv.Key].OwnerRealmId, map.Regions[kv.Key].OccupierRealmId)));
            Assert.All(past.Lieges, kv => Assert.Equal(kv.Value, map.Lieges[kv.Key]));
            Assert.Equal(past.Fallen.Order(), map.Fallen.Order());
            Assert.All(past.Wars, kv => Assert.Equal($"{string.Join(",", kv.Value.Attackers)} v {string.Join(",", kv.Value.Defenders)}",
                $"{string.Join(",", map.Wars[kv.Key].Attackers)} v {string.Join(",", map.Wars[kv.Key].Defenders)}"));
        }
    }

    [Fact]
    public void WorldsWithoutAFoundingRecord_AreNotReplayed()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new WorldDate(3600);
        var map = History.PoliticalMapAt(world, new EventLog(), new WorldDate(10));
        Assert.False(map.Historical);
        Assert.Equal(1, map.Regions[1].OwnerRealmId);
    }
}

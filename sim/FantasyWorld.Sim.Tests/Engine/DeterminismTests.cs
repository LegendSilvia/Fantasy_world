using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Engine;

public class DeterminismTests
{
    private static Simulator NewSim(ulong seed) =>
        new(WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    [Fact]
    public void SameSeed_ProducesIdenticalWorlds()
    {
        var a = NewSim(7);
        var b = NewSim(7);
        a.AdvanceDays(3600);
        b.AdvanceDays(3600);
        Assert.True(a.Log.Count > 20, "test system should produce events");
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentWorlds()
    {
        var a = NewSim(7);
        var b = NewSim(8);
        a.AdvanceDays(3600);
        b.AdvanceDays(3600);
        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void Chunking_DoesNotChangeTheWorld()
    {
        var oneShot = NewSim(11);
        oneShot.AdvanceDays(3600);

        var chunked = NewSim(11);
        foreach (var chunk in new long[] { 1, 29, 300, 1000, 2270 }) chunked.AdvanceDays(chunk);

        var daily = NewSim(11);
        for (var i = 0; i < 3600; i++) daily.AdvanceDays(1);

        Assert.Equal(Hash(oneShot), Hash(chunked));
        Assert.Equal(Hash(oneShot), Hash(daily));
    }

    [Fact]
    public void SnapshotRoundTrip_ContinuesIdentically()
    {
        var original = NewSim(9);
        original.AdvanceDays(1000);

        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(original.World, original.Queue), SimJson.Options);
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!;
        var log = new EventLog();
        foreach (var ev in original.Log.All)
            log.Restore(JsonSerializer.Deserialize<WorldEvent>(JsonSerializer.Serialize(ev, SimJson.Options), SimJson.Options)!);
        var restored = new Simulator(snapshot.ToWorld(), log, snapshot.ToQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

        Assert.Equal(Hash(original), Hash(restored));
        original.AdvanceDays(360);
        restored.AdvanceDays(360);
        Assert.Equal(Hash(original), Hash(restored));
    }

    [Fact]
    public void Hash_IsUppercaseSha256Hex()
    {
        var hash = Hash(NewSim(1));
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }
}

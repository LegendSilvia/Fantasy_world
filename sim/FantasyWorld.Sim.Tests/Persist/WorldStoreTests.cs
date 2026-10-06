using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.WorldGen;
using Microsoft.Data.Sqlite;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Persist;

public class WorldStoreTests
{
    private static Simulator NewSim(ulong seed = 5) =>
        new(WorldFactory.CreateNew(seed, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

    private static string Hash(Simulator s) => WorldHasher.Hash(s.World, s.Log, s.Queue);

    [Fact]
    public void NewFile_HasSchemaButNoWorld()
    {
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(WorldStore.CurrentSchemaVersion, store.SchemaVersion);
        Assert.False(store.HasWorld);
        Assert.Throws<InvalidOperationException>(() => store.Load());
    }

    [Fact]
    public void SaveThenLoad_RestoresIdenticalState()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(2000);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        Assert.Equal(Hash(sim), WorldHasher.Hash(world, log, queue));
    }

    [Fact]
    public void LoadedWorld_ContinuesIdentically()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(500);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var resumed = new Simulator(world, log, queue, new ISimSystem[] { new CoinSystem() }, checkInvariants: true);

        sim.AdvanceDays(720);
        resumed.AdvanceDays(720);
        Assert.Equal(Hash(sim), Hash(resumed));
    }

    [Fact]
    public void RepeatedSaves_AppendOnlyNewEvents()
    {
        using var file = new TempFile();
        var sim = NewSim();
        using var store = WorldStore.Open(file.Path);

        sim.AdvanceDays(1000);
        store.Save(sim.World, sim.Log, sim.Queue);
        Assert.Equal(sim.Log.Count, store.CountEvents());

        sim.AdvanceDays(1000);
        store.Save(sim.World, sim.Log, sim.Queue);
        store.Save(sim.World, sim.Log, sim.Queue);
        Assert.Equal(sim.Log.Count, store.CountEvents());
        Assert.True(store.HasWorld);
    }

    [Fact]
    public void GeneratedWorld_RoundTripsWithGeography()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), Array.Empty<ISimSystem>(), checkInvariants: true);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var reopened = WorldStore.Open(file.Path);
        Assert.True(reopened.HasGeography);
        var (loaded, log, queue) = reopened.Load();
        Assert.Equal(Hash(sim), WorldHasher.Hash(loaded, log, queue));
        Assert.Equal(world.Regions.Count, loaded.Regions.Count);
        Assert.Equal(world.Geography!.Terrain.Biomes, loaded.Geography!.Terrain.Biomes);
        Assert.Equal(world.Ids.Peek("settlement"), loaded.Ids.Peek("settlement"));
    }

    [Fact]
    public void SchemaVersion1File_IsUpgraded()
    {
        using var file = new TempFile();
        var sim = NewSim();
        sim.AdvanceDays(100);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);
        using (var connection = new SqliteConnection($"Data Source={file.Path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE geography; DROP TABLE llm_calls; DROP TABLE followed; UPDATE meta SET value = '1' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }

        using var upgraded = WorldStore.Open(file.Path);
        Assert.Equal(WorldStore.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.False(upgraded.HasGeography);
        Assert.Equal(0, upgraded.LoadJournal().Count);
        Assert.Equal(Hash(sim), WorldHasher.Hash(upgraded.Load().World, sim.Log, sim.Queue));
    }
}

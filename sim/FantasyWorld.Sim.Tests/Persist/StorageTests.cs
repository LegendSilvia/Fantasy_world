using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Tests.Persist;

public class StorageTests
{
    private static object Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Snapshots_AreCompressed_AndOldPlainOnesStillLoad()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());

        var packed = Assert.IsType<byte[]>(Scalar(file.Path, "SELECT json FROM snapshot"));
        var plain = JsonSerializer.Serialize(WorldSnapshot.Capture(world, new EventQueue()), SimJson.Options);
        Assert.True(packed.Length * 3 < plain.Length);
        Assert.IsType<byte[]>(Scalar(file.Path, "SELECT json FROM geography"));

        using (var connection = new SqliteConnection($"Data Source={file.Path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE snapshot SET json = $json";
            cmd.Parameters.AddWithValue("$json", plain);
            cmd.ExecuteNonQuery();
        }
        using var reopened = WorldStore.Open(file.Path);
        Assert.Equal(WorldHasher.Hash(world, new EventLog(), new EventQueue()),
            WorldHasher.Hash(reopened.Load().World, new EventLog(), new EventQueue()));
    }

    [Fact]
    public void Sessions_KeepTheLastThreeBackups()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        foreach (var _ in Enumerable.Range(0, 4))
            CliApp.Run(new[] { "advance", file.Path, "30d" }, output, () => DateTimeOffset.UnixEpoch);

        Assert.True(File.Exists(Backups.PathOf(file.Path, 3)));
        Assert.False(File.Exists(Backups.PathOf(file.Path, 4)));
        using var newest = WorldStore.Open(Backups.PathOf(file.Path, 1));
        Assert.Equal(90, newest.Load().World.Date.Day);
        using var oldest = WorldStore.Open(Backups.PathOf(file.Path, 3));
        Assert.Equal(30, oldest.Load().World.Date.Day);
    }

    [Fact]
    public void ASessionNeverOverwritesAnotherSessionsSave()
    {
        using var file = new TempFile();
        var sim = new Simulator(WorldFactory.CreateNew(5, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(), new ISimSystem[] { new CoinSystem() });
        sim.AdvanceDays(100);
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);

        using var first = WorldStore.Open(file.Path);
        var (w1, l1, q1) = first.Load();
        using (var second = WorldStore.Open(file.Path))
        {
            var (w2, l2, q2) = second.Load();
            new Simulator(w2, l2, q2, new ISimSystem[] { new CoinSystem() }).AdvanceDays(100);
            second.Save(w2, l2, q2);
        }
        new Simulator(w1, l1, q1, new ISimSystem[] { new CoinSystem() }).AdvanceDays(50);
        var error = Assert.Throws<InvalidOperationException>(() => first.Save(w1, l1, q1));
        Assert.Contains("changed by another session", error.Message);
    }
}

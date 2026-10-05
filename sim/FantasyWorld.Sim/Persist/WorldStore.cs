using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot + append-only event log.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY, day INTEGER NOT NULL, type TEXT NOT NULL, json TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_events_day ON events(day);
        CREATE INDEX IF NOT EXISTS ix_events_type ON events(type);
        CREATE TABLE IF NOT EXISTS event_causes (
            cause_id INTEGER NOT NULL, effect_id INTEGER NOT NULL, kind TEXT NOT NULL, weight REAL NOT NULL,
            PRIMARY KEY (cause_id, effect_id));
        CREATE TABLE IF NOT EXISTS event_actors (
            event_id INTEGER NOT NULL, role TEXT NOT NULL, kind TEXT NOT NULL, entity_id INTEGER NOT NULL,
            PRIMARY KEY (event_id, role));
        CREATE INDEX IF NOT EXISTS ix_event_actors_entity ON event_actors(kind, entity_id);
        """;

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    private WorldStore(SqliteConnection connection) => _connection = connection;

    public static WorldStore Open(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        var store = new WorldStore(connection);
        store.Migrate();
        return store;
    }

    public int SchemaVersion => int.Parse(GetMeta("schema_version")!, CultureInfo.InvariantCulture);

    public bool HasWorld => ScalarLong("SELECT COUNT(*) FROM snapshot") > 0;

    public long CountEvents() => ScalarLong("SELECT COUNT(*) FROM events");

    public void Save(World world, EventLog log, EventQueue queue)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            foreach (var ev in log.Since(persistedMax)) InsertEvent(ev);

            using (var cmd = Command("INSERT INTO snapshot(id, json) VALUES (1, $json) " +
                                     "ON CONFLICT(id) DO UPDATE SET json = excluded.json"))
            {
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options));
                cmd.ExecuteNonQuery();
            }
            SetMeta("world_day", world.Date.Day.ToString(CultureInfo.InvariantCulture));
            SetMeta("last_real_utc", world.LastRealUtc.ToString("O", CultureInfo.InvariantCulture));

            _transaction.Commit();
        }
        catch
        {
            _transaction.Rollback();
            throw;
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public (World World, EventLog Log, EventQueue Queue) Load()
    {
        string json;
        using (var cmd = Command("SELECT json FROM snapshot WHERE id = 1"))
            json = cmd.ExecuteScalar() as string ?? throw new InvalidOperationException("No world has been saved in this file.");
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)
            ?? throw new InvalidOperationException("World snapshot is empty.");

        var log = new EventLog();
        using (var cmd = Command("SELECT json FROM events ORDER BY id"))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                log.Restore(JsonSerializer.Deserialize<WorldEvent>(reader.GetString(0), SimJson.Options)!);

        return (snapshot.ToWorld(), log, snapshot.ToQueue());
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        using (var cmd = Command("PRAGMA journal_mode=WAL;")) cmd.ExecuteNonQuery();
        using (var cmd = Command(Schema)) cmd.ExecuteNonQuery();
        var version = GetMeta("schema_version");
        if (version is null)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
        else if (int.Parse(version, CultureInfo.InvariantCulture) > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"World file schema {version} is newer than this build supports ({CurrentSchemaVersion}).");
    }

    private void InsertEvent(WorldEvent ev)
    {
        using (var cmd = Command("INSERT INTO events(id, day, type, json) VALUES ($id, $day, $type, $json)"))
        {
            cmd.Parameters.AddWithValue("$id", ev.Id);
            cmd.Parameters.AddWithValue("$day", ev.Date.Day);
            cmd.Parameters.AddWithValue("$type", ev.Type);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(ev, SimJson.Options));
            cmd.ExecuteNonQuery();
        }
        foreach (var cause in ev.Causes)
        {
            using var cmd = Command("INSERT INTO event_causes(cause_id, effect_id, kind, weight) VALUES ($c, $e, $k, $w)");
            cmd.Parameters.AddWithValue("$c", cause.EventId);
            cmd.Parameters.AddWithValue("$e", ev.Id);
            cmd.Parameters.AddWithValue("$k", cause.Kind.ToString());
            cmd.Parameters.AddWithValue("$w", cause.Weight);
            cmd.ExecuteNonQuery();
        }
        var actors = ev.Actors.ToList();
        if (ev.Place is { } place) actors.Add(new KeyValuePair<string, EntityRef>("@place", place));
        foreach (var (role, entity) in actors)
        {
            using var cmd = Command("INSERT INTO event_actors(event_id, role, kind, entity_id) VALUES ($e, $r, $k, $i)");
            cmd.Parameters.AddWithValue("$e", ev.Id);
            cmd.Parameters.AddWithValue("$r", role);
            cmd.Parameters.AddWithValue("$k", entity.Kind);
            cmd.Parameters.AddWithValue("$i", entity.Id);
            cmd.ExecuteNonQuery();
        }
    }

    private string? GetMeta(string key)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $key");
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var cmd = Command("INSERT INTO meta(key, value) VALUES ($key, $value) " +
                                "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private long ScalarLong(string sql)
    {
        using var cmd = Command(sql);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private SqliteCommand Command(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _transaction;
        return cmd;
    }
}

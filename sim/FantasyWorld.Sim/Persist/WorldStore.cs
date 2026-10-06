using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using Microsoft.Data.Sqlite;

namespace FantasyWorld.Sim.Persist;

/// <summary>One SQLite file per world: snapshot, static geography (written once), the append-only event log and the LLM journal.</summary>
public sealed class WorldStore : IDisposable
{
    public const int CurrentSchemaVersion = 4;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS geography (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL);
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
        CREATE INDEX IF NOT EXISTS ix_event_causes_effect ON event_causes(effect_id);
        CREATE TABLE IF NOT EXISTS llm_calls (
            hash TEXT PRIMARY KEY, tier TEXT NOT NULL, day INTEGER NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL,
            response TEXT NOT NULL, input_tokens INTEGER NOT NULL, output_tokens INTEGER NOT NULL,
            cache_read_tokens INTEGER NOT NULL, cache_write_tokens INTEGER NOT NULL, cost_usd TEXT NOT NULL,
            real_utc TEXT NOT NULL, note TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS followed (kind TEXT NOT NULL, entity_id INTEGER NOT NULL, PRIMARY KEY (kind, entity_id));
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

    public bool HasGeography => ScalarLong("SELECT COUNT(*) FROM geography") > 0;

    /// <summary>
    /// Saves the month in one transaction (spec §9): compacted events removed, new events, the snapshot and new LLM
    /// answers.
    /// </summary>
    public void Save(World world, EventLog log, EventQueue queue, LlmJournal? journal = null)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            var persistedMax = ScalarLong("SELECT COALESCE(MAX(id), 0) FROM events");
            foreach (var id in log.ForgottenUnsaved) DeleteEvent(id);
            foreach (var ev in log.Since(persistedMax)) InsertEvent(ev);

            if (world.Geography is { } geography && !HasGeography)
            {
                using var cmd = Command("INSERT INTO geography(id, json) VALUES (1, $json)");
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(geography, SimJson.Options));
                cmd.ExecuteNonQuery();
            }

            using (var cmd = Command("INSERT INTO snapshot(id, json) VALUES (1, $json) " +
                                     "ON CONFLICT(id) DO UPDATE SET json = excluded.json"))
            {
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options));
                cmd.ExecuteNonQuery();
            }
            SetMeta("world_day", world.Date.Day.ToString(CultureInfo.InvariantCulture));
            SetMeta("last_real_utc", world.LastRealUtc.ToString("O", CultureInfo.InvariantCulture));
            foreach (var call in journal?.Pending ?? Array.Empty<LlmCall>()) InsertCall(call);

            _transaction.Commit();
            journal?.MarkSaved();
            log.MarkForgottenSaved();
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

        var world = snapshot.ToWorld();
        using (var cmd = Command("SELECT json FROM geography WHERE id = 1"))
        {
            if (cmd.ExecuteScalar() is string geography)
                world.Geography = JsonSerializer.Deserialize<Geography>(geography, SimJson.Options);
        }

        return (world, log, snapshot.ToQueue());
    }

    /// <summary>Saves new LLM answers alone (narration, which changes nothing in the world).</summary>
    public void SaveJournal(LlmJournal journal)
    {
        _transaction = _connection.BeginTransaction();
        try
        {
            foreach (var call in journal.Pending) InsertCall(call);
            _transaction.Commit();
            journal.MarkSaved();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public LlmJournal LoadJournal()
    {
        var journal = new LlmJournal();
        using var cmd = Command("SELECT hash, tier, day, provider, model, response, input_tokens, output_tokens, " +
                                "cache_read_tokens, cache_write_tokens, cost_usd, real_utc, note FROM llm_calls ORDER BY hash");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            journal.Restore(new LlmCall(reader.GetString(0), Enum.Parse<ModelTier>(reader.GetString(1)), reader.GetInt64(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), decimal.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture), reader.GetString(12)));
        return journal;
    }

    public LlmSettings LoadLlmSettings() =>
        GetMeta("llm_settings") is { } json ? JsonSerializer.Deserialize<LlmSettings>(json, SimJson.Options)! : new LlmSettings();

    public void SaveLlmSettings(LlmSettings settings) => SetMeta("llm_settings", JsonSerializer.Serialize(settings, SimJson.Options));

    /// <summary>Entities the player follows (spec §9): their events rank higher in the digest.</summary>
    public SortedSet<EntityRef> LoadFollowed()
    {
        var followed = new SortedSet<EntityRef>(Comparer<EntityRef>.Create((a, b) =>
            string.CompareOrdinal(a.Kind, b.Kind) is var k and not 0 ? k : a.Id.CompareTo(b.Id)));
        using var cmd = Command("SELECT kind, entity_id FROM followed");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) followed.Add(new EntityRef(reader.GetString(0), reader.GetInt64(1)));
        return followed;
    }

    public void Follow(EntityRef entity, bool follow = true)
    {
        using var cmd = Command(follow
            ? "INSERT OR IGNORE INTO followed(kind, entity_id) VALUES ($k, $i)"
            : "DELETE FROM followed WHERE kind = $k AND entity_id = $i");
        cmd.Parameters.AddWithValue("$k", entity.Kind);
        cmd.Parameters.AddWithValue("$i", entity.Id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        using (var cmd = Command("PRAGMA journal_mode=WAL;")) cmd.ExecuteNonQuery();
        using (var cmd = Command(Schema)) cmd.ExecuteNonQuery();
        var version = GetMeta("schema_version");
        var current = version is null ? 0 : int.Parse(version, CultureInfo.InvariantCulture);
        if (current > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"World file schema {version} is newer than this build supports ({CurrentSchemaVersion}).");
        // 1 → 2 adds the geography table, 2 → 3 llm_calls and 3 → 4 followed; the schema script above creates them.
        if (current < CurrentSchemaVersion)
            SetMeta("schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private void DeleteEvent(long id)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM events WHERE id = $id",
                     "DELETE FROM event_causes WHERE effect_id = $id OR cause_id = $id",
                     "DELETE FROM event_actors WHERE event_id = $id",
                 })
        {
            using var cmd = Command(sql);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
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

    private void InsertCall(LlmCall call)
    {
        using var cmd = Command("INSERT OR IGNORE INTO llm_calls VALUES ($h, $t, $d, $p, $m, $r, $i, $o, $cr, $cw, $c, $u, $n)");
        cmd.Parameters.AddWithValue("$h", call.Hash);
        cmd.Parameters.AddWithValue("$t", call.Tier.ToString());
        cmd.Parameters.AddWithValue("$d", call.Day);
        cmd.Parameters.AddWithValue("$p", call.Provider);
        cmd.Parameters.AddWithValue("$m", call.Model);
        cmd.Parameters.AddWithValue("$r", call.Json);
        cmd.Parameters.AddWithValue("$i", call.InputTokens);
        cmd.Parameters.AddWithValue("$o", call.OutputTokens);
        cmd.Parameters.AddWithValue("$cr", call.CacheReadTokens);
        cmd.Parameters.AddWithValue("$cw", call.CacheWriteTokens);
        cmd.Parameters.AddWithValue("$c", call.CostUsd.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", call.RealUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$n", call.Note);
        cmd.ExecuteNonQuery();
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

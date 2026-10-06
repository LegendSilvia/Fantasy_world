using FantasyWorld.Llm;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Client;

/// <summary>
/// One open world for the game client (sub-project 4 §2): catch-up on launch on a worker thread, live stepping while
/// open, saving, and the screens' view models. The simulation is guarded by one lock: the worker holds it while it
/// simulates, the UI uses <see cref="TryRead{T}"/> so a frame never waits on the simulation.
/// </summary>
public sealed class Session : IDisposable
{
    private readonly object _gate = new();
    private readonly WorldStore _store;
    private readonly Simulator _sim;
    private readonly LlmGateway _gateway;
    private readonly LiveHost _live;
    private readonly Func<DateTimeOffset> _clock;
    private bool _disposed;

    private Session(string path, WorldStore store, Simulator sim, LlmGateway gateway, Func<DateTimeOffset> clock)
    {
        Path = path;
        _store = store;
        _sim = sim;
        _gateway = gateway;
        _live = new LiveHost(sim);
        _clock = clock;
    }

    public string Path { get; }

    /// <summary>Makes a new world file with its prehistory (default by size) and opens it.</summary>
    public static Session Create(string path, ulong seed, WorldSize size, Func<DateTimeOffset> clock, int? prehistoryYears = null,
        IProgress<WorldDate>? progress = null)
    {
        if (File.Exists(path)) throw new InvalidOperationException($"{path} already exists.");
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), clock());
        var history = Prehistory.Run(world, prehistoryYears ?? Prehistory.DefaultYearsFor(size), progress);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        // Written beside the target and moved into place, so a launch killed mid-way never leaves a half-made world.
        var partial = path + ".creating";
        if (File.Exists(partial)) File.Delete(partial);
        using (var store = WorldStore.Open(partial)) store.Save(world, history.Log, history.Queue);
        File.Move(partial, path);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(partial + suffix)) File.Move(partial + suffix, path + suffix);
        return Open(path, clock, rotate: false);
    }

    /// <summary>Opens a world (rotating its backups first) with LLM agency when its settings and credentials allow.</summary>
    /// <remarks>
    /// The world is loaded before the backups rotate, so a file that fails to load never pushes the last good copies
    /// out; a world just created has nothing to back up yet.
    /// </remarks>
    public static Session Open(string path, Func<DateTimeOffset> clock, Func<LlmSettings, ILlmProvider?>? providers = null, bool rotate = true)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"No world file at {path}.", path);
        var store = WorldStore.Open(path);
        try
        {
            var (world, log, queue) = store.Load();
            var settings = store.LoadLlmSettings();
            var gateway = new LlmGateway(settings.Enabled ? (providers ?? (s => LlmProviders.Create(s)))(settings) : null, settings, store.LoadJournal());
            if (rotate) Backups.Rotate(store, path);
            var story = new StoryContext(world, log, gateway);
            var sim = new Simulator(world, log, queue, DefaultSystems.Create(new LlmDecider(story), new LlmArbiter(story), new LlmInventor(story)));
            return new Session(path, store, sim, gateway, clock);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>World days owed to real time (what catch-up will simulate, at most 30 years at a time).</summary>
    public long DaysBehind => Read((w, _) => Math.Max(0, TimeScale.WholeWorldDays(_clock() - w.LastRealUtc)));

    public bool Busy { get; private set; }

    /// <summary>Simulates the real time owed on a worker thread and saves; progress reports each new month.</summary>
    public Task<CatchUpResult> CatchUpAsync(IProgress<WorldDate>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                Busy = true;
                try
                {
                    var result = CatchUp.Run(_sim, _clock(), progress, ct);
                    Save();
                    return result;
                }
                finally
                {
                    Busy = false;
                }
            }
        }, ct);

    /// <summary>One live step (call every few seconds): simulates any day now due and saves if one was.</summary>
    public LiveTick Step()
    {
        lock (_gate)
        {
            var tick = _live.Step(_clock());
            if (tick.Days > 0) Save();
            return tick;
        }
    }

    /// <summary>When the next world day falls due.</summary>
    public DateTimeOffset NextDayAt => Read((_, _) => _live.NextDayAt);

    /// <summary>Builds a view under the lock, waiting for the simulation if it is mid-step.</summary>
    public T Read<T>(Func<World, EventLog, T> view)
    {
        lock (_gate) return view(_sim.World, _sim.Log);
    }

    /// <summary>Builds a view only if the simulation is idle; null while it is busy (keep showing the last one).</summary>
    public T? TryRead<T>(Func<World, EventLog, T> view) where T : class
    {
        if (!Monitor.TryEnter(_gate)) return null;
        try
        {
            return view(_sim.World, _sim.Log);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>The digest for the returning reader. Narration may call the LLM: build it off the UI thread.</summary>
    public DigestScreen Digest() => Read((w, l) => DigestScreen.ForReader(w, l, _store.LoadFollowed(), DaysOwed(w), new LlmNarrator(new StoryContext(w, l, _gateway))));

    /// <summary>The reader has seen everything so far.</summary>
    public void MarkSeen()
    {
        lock (_gate)
        {
            _sim.World.LastSeenEventId = _sim.Log.NextId - 1;
            Save();
        }
    }

    public MapScreen Map(int? year = null) =>
        Read((w, l) => MapScreen.Build(w, l, year is { } y ? new WorldDate((long)y * WorldDate.DaysPerYear - 1) : null));

    public CharacterScreen Character(long id) => Read((w, l) => CharacterScreen.Build(w, l, id, _store.LoadFollowed().Contains(new EntityRef(Sim.Model.Character.Kind, id))));

    public FamilyTree Family(long id) => Read((w, _) => FamilyTree.Build(w, id));

    public WhyScreen Why(long eventId, int depth = 4) => Read((w, l) => WhyScreen.Build(w, l, eventId, depth));

    public ChronicleScreen Chronicle(int? fromYear = null, int? toYear = null, Category? only = null, int page = 1) =>
        Read((w, l) => ChronicleScreen.Build(w, l, fromYear, toYear, only, 0, page));

    public RealmScreen Realm(long id) => Read((w, l) => RealmScreen.Build(w, l, id));

    public ArtifactScreen Artifact(long id) => Read((w, l) => ArtifactScreen.Build(w, l, id));

    public bool IsFollowed(EntityRef entity)
    {
        lock (_gate) return _store.LoadFollowed().Contains(entity);
    }

    public void Follow(EntityRef entity, bool follow = true)
    {
        lock (_gate) _store.Follow(entity, follow);
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_gate)
        {
            Save();
            _store.Dispose();
            _disposed = true;
        }
    }

    private long DaysOwed(World world) => Math.Max(0, TimeScale.WholeWorldDays(_clock() - world.LastRealUtc));

    private void Save() => _store.Save(_sim.World, _sim.Log, _sim.Queue, _gateway.Journal);
}

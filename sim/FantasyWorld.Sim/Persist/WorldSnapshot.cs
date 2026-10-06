using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Serializable world + queue state. The event log and the static geography are stored separately.
/// Capture references the live entities, so serialize the snapshot immediately.
/// </summary>
public sealed record WorldSnapshot(
    int SchemaVersion,
    ulong Seed,
    long Day,
    DateTimeOffset LastRealUtc,
    long LastSeenEventId,
    SortedDictionary<string, double> Stats,
    SortedDictionary<string, long> Markers,
    List<ScheduledAction> Queue,
    long QueueNextSeq,
    SortedDictionary<string, long>? Ids = null,
    SortedDictionary<long, Region>? Regions = null,
    SortedDictionary<long, Settlement>? Settlements = null,
    SortedDictionary<long, Culture>? Cultures = null,
    SortedDictionary<long, Faith>? Faiths = null,
    SortedDictionary<long, House>? Houses = null,
    SortedDictionary<long, Character>? Characters = null,
    SortedDictionary<long, Artifact>? Artifacts = null)
{
    public const int CurrentVersion = 3;

    public static WorldSnapshot Capture(World world, EventQueue queue) => new(
        CurrentVersion,
        world.Seed,
        world.Date.Day,
        world.LastRealUtc,
        world.LastSeenEventId,
        Sorted.From(world.Stats),
        Sorted.From(world.Markers),
        queue.Snapshot().ToList(),
        queue.NextSeq,
        Sorted.From(world.Ids.Next),
        world.Regions,
        world.Settlements,
        world.Cultures,
        world.Faiths,
        world.Houses,
        world.Characters,
        world.Artifacts);

    /// <summary>Rebuilds the world. Older snapshots lack newer entities, which load empty.</summary>
    public World ToWorld() => new()
    {
        Seed = Seed,
        Date = new WorldDate(Day),
        LastRealUtc = LastRealUtc,
        LastSeenEventId = LastSeenEventId,
        Stats = Sorted.From(Stats),
        Markers = Sorted.From(Markers),
        Ids = new IdGen { Next = Ids ?? new SortedDictionary<string, long>() },
        Regions = Copy(Regions),
        Settlements = Copy(Settlements),
        Cultures = Copy(Cultures),
        Faiths = Copy(Faiths),
        Houses = Copy(Houses),
        Characters = Copy(Characters),
        Artifacts = Copy(Artifacts),
    };

    private static SortedDictionary<long, T> Copy<T>(SortedDictionary<long, T>? source) =>
        source is null ? new SortedDictionary<long, T>() : new SortedDictionary<long, T>(source);

    public EventQueue ToQueue()
    {
        var queue = new EventQueue();
        queue.Restore(Queue.Select(a => a with { Data = Sorted.From(a.Data) }), QueueNextSeq);
        return queue;
    }
}

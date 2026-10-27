using System.IO.Compression;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// The world as the browser reads it (sub-project 11 §3): the snapshot, the event log and the geography in the sim's own
/// JSON, gzipped, so the same C# loads it without SQLite. Only the world goes in: the LLM journal and settings stay in the
/// world file, so nothing about the providers is ever published.
/// </summary>
public sealed record WebBundle(int Version, WorldSnapshot Snapshot, Geography? Geography, IReadOnlyList<WorldEvent> Events)
{
    public const int CurrentVersion = 1;

    /// <summary>The bundle of a saved world.</summary>
    public static WebBundle Of(WorldStore store) => store.ExportBundle();

    public byte[] ToGzip()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, this, SimJson.Options);
        return output.ToArray();
    }

    /// <summary>The bundle from its bytes: gzipped as written, or already unpacked (a host that serves .gz with Content-Encoding).</summary>
    public static WebBundle FromGzip(byte[] bytes)
    {
        var packed = bytes is [0x1f, 0x8b, ..];
        using var json = packed ? (Stream)new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress) : new MemoryStream(bytes);
        var bundle = JsonSerializer.Deserialize<WebBundle>(json, SimJson.Options) ?? throw new InvalidDataException("The web bundle is empty.");
        if (bundle.Version > CurrentVersion) throw new InvalidDataException($"The web bundle is version {bundle.Version}; this reader knows {CurrentVersion}.");
        return bundle;
    }

    /// <summary>The world and its history, as a world file would load them (the queue is not needed to look at it).</summary>
    public (World World, EventLog Log) Load()
    {
        var log = new EventLog();
        foreach (var ev in Events) log.Restore(ev);
        var world = Snapshot.ToWorld();
        world.Geography = Geography;
        return (world, log);
    }
}

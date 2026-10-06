using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int DigestCommand(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var since = Option(rest, "--since") is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : (long?)null;
        var take = int.Parse(Option(rest, "--take") ?? Digest.DefaultTake.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (take < 1) throw new FormatException("--take must be at least 1.");
        output.WriteLine(TellDigest(store, sim, gateway, since, take));
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        return 0;
    }

    /// <summary>
    /// Narrates the digest since the reader last looked (or since an event) and marks everything as seen; the caller
    /// saves. Explicit --since leaves the reader's place unchanged.
    /// </summary>
    private static string TellDigest(WorldStore store, Simulator sim, LlmGateway gateway, long? sinceEventId, int take = Digest.DefaultTake)
    {
        var digest = Digest.Build(sim.Log, sim.World, sinceEventId ?? sim.World.LastSeenEventId, store.LoadFollowed(), take);
        if (sinceEventId is null) sim.World.LastSeenEventId = sim.Log.NextId - 1;
        return new LlmNarrator(new StoryContext(sim.World, sim.Log, gateway)).Digest(digest);
    }

    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}

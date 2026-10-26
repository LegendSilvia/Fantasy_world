using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Cli;

public class EpisodeCliTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(DateTimeOffset now, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => now);
        return (code, output.ToString());
    }

    private static long Marker(string path, Func<FantasyWorld.Sim.Model.World, long> read)
    {
        using var store = WorldStore.Open(path);
        return read(store.Load().World);
    }

    [Fact]
    public void FwEpisode_WritesTheEpisode_AndMovesOnlyTheBroadcastMarker()
    {
        using var file = new TempFile();
        using var dir = new TempDir();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(T0, "advance", file.Path, "3y");
        var seen = Marker(file.Path, w => w.LastSeenEventId);

        var (code, output) = Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "2026-10-27");
        Assert.Equal(0, code);
        Assert.Contains("Episode 2026-10-27", output);
        var episode = Episode.FromJson(File.ReadAllText(Path.Combine(dir.Path, "episode.json")));
        Assert.Equal(episode.Script, File.ReadAllText(Path.Combine(dir.Path, "script.txt")));
        Assert.Equal(episode.LastEventId, Marker(file.Path, w => w.LastBroadcastEventId));
        Assert.Equal(seen, Marker(file.Path, w => w.LastSeenEventId));
        using (var store = WorldStore.Open(file.Path)) Assert.Equal("2026-10-27", store.Load().World.LastBroadcastDate);

        Run(T0, "advance", file.Path, "1y");
        Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "2026-10-28", "--keep-marker");
        Assert.Equal(episode.LastEventId, Marker(file.Path, w => w.LastBroadcastEventId));
        using (var store = WorldStore.Open(file.Path)) Assert.Equal("2026-10-27", store.Load().World.LastBroadcastDate);
        Assert.Equal(1, Run(T0, "episode", file.Path, "--out", dir.Path, "--date", "28/10/2026").Code);
    }

    [Fact]
    public void QuietCatchUp_NarratesNothing_AndLeavesTheReadersPlace()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        var (code, output) = Run(T0.AddDays(2), "catchup", file.Path, "--quiet");
        Assert.Equal(0, code);
        Assert.Contains("Advanced 720 days", output);
        Assert.DoesNotContain("The chronicle remembers", output);
        Assert.Equal(0, Marker(file.Path, w => w.LastSeenEventId));
    }

    [Fact]
    public void FwSnapshot_WritesACopyThatOpensAsTheSameWorld()
    {
        using var file = new TempFile();
        using var copy = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(T0, "advance", file.Path, "1y");
        Assert.Equal(0, Run(T0, "snapshot", file.Path, copy.Path).Code);
        using var a = WorldStore.Open(file.Path);
        using var b = WorldStore.Open(copy.Path);
        Assert.Equal(a.Load().World.Date, b.Load().World.Date);
        Assert.Equal(a.Load().Log.Count, b.Load().Log.Count);
    }
}

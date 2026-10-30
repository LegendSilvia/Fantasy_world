using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Llm;
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
    public void FwEpisodeNarrate_LetsTheChroniclerWriteTheScript_WithTheTemplateAsFallback()
    {
        using var file = new TempFile();
        using var dir = new TempDir();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        var output = new StringWriter();
        CliApp.Run(["episode", file.Path, "--out", dir.Path, "--date", "2026-10-27", "--narrate", "--keep-marker"], output, () => T0, _ => null);
        Assert.Contains("by the template", output.ToString()); // provider "none": offline

        Run(T0, "llm", file.Path, "--provider", "anthropic", "--model", "narrate=claude-sonnet-5-5");
        var template = Episode.FromJson(File.ReadAllText(Path.Combine(dir.Path, "episode.json")));
        var sections = template.Parts.Select(p => p.Text.Replace("Elsewhere in the chronicle:", "And elsewhere, in brief:")).ToList();
        var retold = string.Join("\n\n", sections);
        var provider = new FakeLlmProvider(_ => System.Text.Json.JsonSerializer.Serialize(new { sections }));
        output = new StringWriter();
        Assert.Equal(0, CliApp.Run(["episode", file.Path, "--out", dir.Path, "--date", "2026-10-27", "--narrate"], output, () => T0, _ => provider));
        Assert.Contains("by the chronicler", output.ToString());
        Assert.Contains("LLM: 2 new answer(s)", output.ToString()); // the chronicler, then the editor
        Assert.Equal(retold, File.ReadAllText(Path.Combine(dir.Path, "script.txt")));
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(2, store.LoadJournal().Count); // the chronicler and the editor
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
    public void CatchUpByTheYear_GoesOnToTheNextYear_HoweverLongItHasBeen()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");
        // The owner, 2026-10-30: each run is exactly one year, whether a day or an hour has passed.
        var (code, output) = Run(T0.AddHours(20), "catchup", file.Path, "--quiet", "--year");
        Assert.Equal(0, code);
        Assert.Contains("Advanced 360 days", output);
        Assert.Contains("Advanced 360 days", Run(T0.AddDays(3), "catchup", file.Path, "--quiet", "--year").Output);
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

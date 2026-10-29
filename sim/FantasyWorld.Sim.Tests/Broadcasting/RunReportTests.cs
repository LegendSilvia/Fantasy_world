using System.Text;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

/// <summary>The day's report and the guard against going public (the owner, 2026-10-29: "a method to guarantee everything").</summary>
public class RunReportTests
{
    [Fact]
    public void Public_NeedsTheOwnersWord()
    {
        Assert.Equal("unlisted", BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny" }""").YouTube.Privacy);
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "youTube": { "privacy": "public" } }"""));
        Assert.Equal("public", BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny", "youTube": { "privacy": "public", "goPublic": true } }""").YouTube.Privacy);
    }

    [Fact]
    public async Task TheReport_NamesWhatWasMade_AndEveryShortfall()
    {
        using var dir = new TempDir();
        var root = new LocalObjectStore(dir.Path);
        var config = BroadcastConfig.Parse("""{ "seed": 3, "size": "tiny" }""");
        var missing = await RunReport.BuildAsync(config, root, dryRun: false, "2026-10-27");
        Assert.Contains("no episode was stored for 2026-10-27", missing.Problems);

        // A day made by the run itself, without speech, video or a channel: its shortfalls are all named.
        Assert.Equal(DailyRun.Ok, await new DailyRun(config, root, [], () => new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero), new StringWriter(),
            new RunOptions(Create: true), _ => null).RunAsync());
        var report = await RunReport.BuildAsync(config, root, dryRun: false, "2026-10-27");
        Assert.Contains("## Broadcast of 2026-10-27", report.Markdown);
        Assert.Contains("the script is the template's, not Claude's", report.Problems);
        Assert.Contains("the episode was not narrated", report.Problems);
        Assert.Contains("the video was not made", report.Problems);
        Assert.Contains("the Discord post was not made", report.Problems);
        Assert.Contains("### What fell short", report.Markdown);
    }
}

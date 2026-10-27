using System.Net;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class ChaptersTests
{
    [Fact]
    public void Chapters_AreTimedFromEachSectionsShareOfTheNarration()
    {
        // 128 kbps is 16,000 bytes a second.
        var timeline = Chapters.Timed(["Opening", "The fall of Esk", "Until tomorrow"], [16_000L * 5, 16_000L * 62, 16_000L * 8], 128);
        Assert.Equal(75, timeline.Length);
        Assert.Equal(new[] { new Chapter("Opening", 0), new Chapter("The fall of Esk", 5), new Chapter("Until tomorrow", 67) }, timeline.Chapters);
        Assert.Equal("0:00 Opening\n0:05 The fall of Esk\n1:07 Until tomorrow", Chapters.Lines(timeline.Chapters));
        Assert.Equal("1:02:05", Chapters.Stamp(3725));
        Assert.Throws<ArgumentException>(() => Chapters.Timed(["One"], [1, 2], 128));
    }

    [Fact]
    public void YouTube_GetsChaptersOfTenSecondsOrMore_AndAtLeastThree()
    {
        var timeline = new Timeline(100,
        [
            new("Opening", 0), new("A death at Esk", 6), new("A challenge at Holt", 30), new("A duel at Holt", 34), new("Until tomorrow", 95),
        ]);
        // The 6-second opening takes in "A death", the 4-second challenge folds into it, the 5-second closing into the duel:
        // two would remain, and YouTube needs three.
        Assert.Empty(timeline.ForYouTube());
        var longer = new Timeline(199, [new("Opening", 0), new("Headline", 12), new("A duel", 60), new("Until tomorrow", 190)]);
        Assert.Equal(new[] { new Chapter("Opening", 0), new Chapter("Headline", 12), new Chapter("A duel", 60) }, longer.ForYouTube());
    }

    [Fact]
    public async Task YouTubeAndDiscord_ListTheChapters()
    {
        var timeline = new Timeline(200, [new("Opening", 0), new("Headline", 12), new("A duel", 60), new("Until tomorrow", 185)]);
        var details = YouTube.Details(BroadcastPartsTests.SampleEpisode(), new BroadcastYouTube(), timeline);
        Assert.StartsWith("0:00 Opening\n0:12 Headline\n1:00 A duel\n3:05 Until tomorrow\n\n", (string)details["snippet"]!["description"]!);
        Assert.DoesNotContain("0:00", (string)YouTube.Details(BroadcastPartsTests.SampleEpisode(), new BroadcastYouTube())["snippet"]!["description"]!);

        using var dir = new TempDir();
        var store = new LocalObjectStore(dir.Path);
        await Chapters.SaveAsync(store, "2026-10-27", timeline);
        Assert.Equal(timeline.Chapters, (await Chapters.LoadAsync(store, "2026-10-27")).Chapters);
        Assert.Same(Timeline.None, await Chapters.LoadAsync(store, "2026-10-28"));
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/T", "The Chronicle");
        discord.Attach(store, TextWriter.Null, "2026-10-27");
        await discord.PublishAsync(BroadcastPartsTests.SampleEpisode(), null, null);
        var body = System.Text.Encoding.UTF8.GetString(http.Seen.Single().Body!);
        Assert.Contains("\"name\":\"In this episode\"", body);
        Assert.Contains("1:00 A duel", body);
    }
}

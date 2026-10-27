using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class SiteTests
{
    [Fact]
    public void ThePostsLinkToTheWorld_OpenOnTheHeadline_OnTheStagingPreviewForADryRun()
    {
        var episode = BroadcastPartsTests.SampleEpisode() with { Headline = new FantasyWorld.Sim.Chronicle.EpisodeStory(42, 101, "War", "The War of Ash begins") };
        var site = new BroadcastSite { Url = "https://fantasy-world-chronicle.pages.dev/" };
        var live = site.LinkFor(episode, dryRun: false)!;
        Assert.Equal($"https://fantasy-world-chronicle.pages.dev/#why/{episode.Headline!.EventId}", live);
        Assert.Equal($"https://staging.fantasy-world-chronicle.pages.dev/#why/{episode.Headline.EventId}", site.LinkFor(episode, dryRun: true));
        // A custom domain has no branch alias: a dry run links to it as it is.
        Assert.StartsWith("https://world.example.org/#why/", new BroadcastSite { Url = "https://world.example.org" }.LinkFor(episode, dryRun: true));
        Assert.Null(new BroadcastSite().LinkFor(episode, dryRun: false));
        // Without a headline, the site opens on the map.
        Assert.Equal("https://fantasy-world-chronicle.pages.dev", site.LinkFor(episode with { Headline = null }, dryRun: false));
        Assert.Equal(("main", "staging"), (BroadcastSite.BranchFor(false), BroadcastSite.BranchFor(true)));

        var description = (string)YouTube.Details(episode, new BroadcastYouTube(), siteLink: live)["snippet"]!["description"]!;
        Assert.StartsWith($"Explore the world: {live}\n\n", description);
        var post = DiscordWebhook.Payload(episode, "The Chronicle", videoUrl: "https://youtu.be/x", siteLink: live);
        Assert.Equal($"▶ Watch the episode: https://youtu.be/x\n🗺 Explore the world: <{live}>", (string)post["content"]!);
        Assert.Equal(live, (string)post["embeds"]![0]!["url"]!);
        Assert.DoesNotContain("Explore", DiscordWebhook.Payload(episode, "The Chronicle").ToJsonString());
    }

    [Theory]
    [InlineData("{\"site\":{\"url\":\"http://insecure.example\"}}", "site.url must be an https address")]
    [InlineData("{\"site\":{\"project\":\"Bad Name\"}}", "site.project must be lower-case")]
    public void TheSiteSettings_AreChecked(string json, string why)
    {
        var e = Assert.Throws<FormatException>(() => BroadcastConfig.Parse(json));
        Assert.Contains(why, e.Message);
    }
}

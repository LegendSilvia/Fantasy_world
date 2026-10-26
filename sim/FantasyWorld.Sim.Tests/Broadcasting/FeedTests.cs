using System.Xml.Linq;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Publishing;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class FeedTests
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly BroadcastConfig Config = BroadcastConfig.Parse("""{ "publicBaseUrl": "https://pub-1.r2.dev" }""");

    private static Manifest Narrated()
    {
        var manifest = new Manifest { WorldName = "Aerenmoor" };
        foreach (var (date, year, headline) in new[] { ("2026-10-27", 101, "The War of the Ash begins."), ("2026-10-28", 102, (string?)null), ("2026-10-29", 103, "Gewick <falls>\u0001") })
        {
            var day = manifest.Day(date);
            (day.ToYear, day.Headline, day.AudioBytes, day.AudioSeconds) = (year, headline, 3_000_000, 187);
            day.Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch;
            day.Steps[Manifest.AudioStep] = DateTimeOffset.UnixEpoch;
        }
        manifest.Day("2026-10-30").Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch; // not narrated: not in the feed
        return manifest;
    }

    [Fact]
    public void TheFeed_IsPodcastRss_NewestFirst_WithStableGuidsAndEnclosures()
    {
        var items = Feed.Items(Narrated(), Config);
        var rss = XDocument.Parse(Feed.Rss("Aerenmoor", "https://pub-1.r2.dev/live/", items, "chronicle@example.com"));
        var channel = rss.Root!.Element("channel")!;
        Assert.Equal("The Chronicle of Aerenmoor", (string?)channel.Element("title"));
        Assert.Equal("https://pub-1.r2.dev/live/cover.png", (string?)channel.Element(Itunes + "image")!.Attribute("href"));
        Assert.Equal("false", (string?)channel.Element(Itunes + "explicit"));
        Assert.Equal("chronicle@example.com", (string?)channel.Element(Itunes + "owner")!.Element(Itunes + "email"));
        Assert.Equal("https://pub-1.r2.dev/live/feed.xml", (string?)channel.Element((XNamespace)"http://www.w3.org/2005/Atom" + "link")!.Attribute("href"));
        Assert.Equal("The War of the Ash begins.", (string?)channel.Elements("item").Last().Element("description"));
        var entries = channel.Elements("item").ToList();
        Assert.Equal(new[] { "Aerenmoor-2026-10-29", "Aerenmoor-2026-10-28", "Aerenmoor-2026-10-27" }, entries.Select(i => (string)i.Element("guid")!));
        var newest = entries[0];
        Assert.Equal("Year 103: Gewick <falls>", (string?)newest.Element("title")); // the control character is dropped
        var enclosure = newest.Element("enclosure")!;
        Assert.Equal(("https://pub-1.r2.dev/live/episodes/2026-10-29/narration.mp3", "3000000", "audio/mpeg"),
            ((string)enclosure.Attribute("url")!, (string)enclosure.Attribute("length")!, (string)enclosure.Attribute("type")!));
        Assert.Equal("187", (string?)newest.Element(Itunes + "duration"));
        Assert.Equal("Wed, 28 Oct 2026 22:30:00 GMT", (string?)newest.Element("pubDate")); // 05:30 on the 29th in Bangkok
        Assert.Equal("Aerenmoor, Year 102", (string?)entries[1].Element("title"));
    }

    [Fact]
    public void TheArchive_ListsEveryEpisode_WithItsWordsEscaped()
    {
        var html = Feed.Archive("Aerenmoor", Feed.Items(Narrated(), Config));
        Assert.Contains("<title>The Chronicle of Aerenmoor</title>", html);
        Assert.Contains("Gewick &lt;falls&gt;", html);
        Assert.DoesNotContain("<falls>", html);
        Assert.Contains("src=\"episodes/2026-10-27/narration.mp3\"", html);
        Assert.True(html.IndexOf("2026-10-29", StringComparison.Ordinal) < html.IndexOf("2026-10-27", StringComparison.Ordinal));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "publicBaseUrl": "http://insecure.example" }"""));
    }
}

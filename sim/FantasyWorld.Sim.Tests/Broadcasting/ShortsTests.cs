using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Sim.Tests.Broadcasting;

/// <summary>The vertical short (the owner, 2026-10-28): cut to a Short's length at a section, titled as a Short.</summary>
public class ShortsTests
{
    [Fact]
    public void AShort_IsTheWholeEpisode_WhenItFits_ElseItEndsAtTheLastSectionBegunInTime()
    {
        var fits = new Timeline(150, [new Chapter("Opening", 0), new Chapter("War", 40)]);
        Assert.Equal(150, DioramaFilm.ShortLength(fits));
        var long_ = new Timeline(230, [new Chapter("Opening", 0), new Chapter("War", 60), new Chapter("Duel", 140), new Chapter("Close", 190)]);
        Assert.Equal(140, DioramaFilm.ShortLength(long_));
        Assert.Equal("episodes/2026-10-28/film/tall-chunk-03.mp4", DioramaFilm.ChunkKey("2026-10-28", 3, tall: true));
    }

    [Fact]
    public void AShortsTitle_CarriesTheHashtag_WithinYouTubesLimit()
    {
        var headline = new EpisodeStory(1, 41, "War", new string('A', 150));
        var episode = new Episode("2026-10-28", "Follvale", "Maren", 40, 41, 3, 1, 3, "", headline, "", [], null, "", "Script");
        var details = YouTube.Details(episode, new BroadcastYouTube(), siteLink: "https://example.org/", shortForm: true);
        var title = details["snippet"]!["title"]!.GetValue<string>();
        Assert.EndsWith(" #Shorts", title);
        Assert.True(title.Length <= YouTube.MaxTitle);
        Assert.Contains("Explore the world: https://example.org/", details["snippet"]!["description"]!.GetValue<string>());
        Assert.Equal("youtube-short", new YouTube(new HttpClient(), null, new FfmpegVideo(), new BroadcastYouTube(), vertical: true).Name);
    }
}

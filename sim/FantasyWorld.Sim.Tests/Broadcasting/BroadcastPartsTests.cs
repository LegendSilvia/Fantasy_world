using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class BroadcastPartsTests
{
    internal static Episode SampleEpisode(string script = "The chronicle of Aerenmoor, Year 12.") =>
        new("2026-10-27", "Aerenmoor", "Ilsa the Grey", 11, 12, 40, 100, 140, "The chronicle of Aerenmoor, Year 12.",
            null, "", [], null, "So it is told.", script);

    [Fact]
    public void Retention_KeepsTheLastDays_AndTheFirstOfEachMonth()
    {
        var keys = Enumerable.Range(0, 75).Select(d => BackupRetention.KeyFor(new DateOnly(2026, 8, 20).AddDays(d).ToString("yyyy-MM-dd")))
            .Append("backups/notes.txt").Append("world.fw.db").ToList();
        var deleted = BackupRetention.ToDelete(keys, "2026-11-02", keepDays: 30);
        var kept = keys.Except(deleted).ToList();
        Assert.Contains("backups/2026-08-20.fw.db", kept); // the first August backup
        Assert.Contains("backups/2026-09-01.fw.db", kept);
        Assert.Contains("backups/2026-10-01.fw.db", kept);
        Assert.Contains("backups/2026-10-04.fw.db", kept); // 30 days ending 2026-11-02
        Assert.Contains("backups/2026-10-03.fw.db", deleted);
        Assert.Contains("backups/notes.txt", kept);
        Assert.Equal(75 - 30 - 3, deleted.Count);
    }

    [Fact]
    public void Manifest_RoundTrips_AndMarksStepsPerDate()
    {
        var manifest = new Manifest();
        manifest.Record("2026-10-27", SampleEpisode());
        manifest.Day("2026-10-27").Steps[Manifest.EpisodeStep] = DateTimeOffset.UnixEpoch;
        var back = Manifest.FromBytes(manifest.ToBytes());
        Assert.True(back.Day("2026-10-27").Done(Manifest.EpisodeStep));
        Assert.False(back.Day("2026-10-27").Done(Manifest.DiscordStep));
        Assert.Equal(12, back.Day("2026-10-27").ToYear);
        Assert.False(back.Day("2026-10-28").Done(Manifest.EpisodeStep));
        Assert.Empty(Manifest.FromBytes(null).Episodes);
    }

    [Fact]
    public void Config_HasSafeDefaults_AndRefusesWhatItCannotDo()
    {
        var config = BroadcastConfig.Parse("{}");
        Assert.Equal(("claude-sonnet-5-5", 0, true), (config.Llm.NarrateModel, config.Llm.StoryBeatsPerDay, config.Llm.Narrate));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "llm": { "monthlyCapUsd": -1 } }"""));
        Assert.Equal("2026-10-27", config.DateAt(new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-10-26", config.DateAt(new DateTimeOffset(2026, 10, 26, 16, 59, 0, TimeSpan.Zero)));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "language": "th" }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "maxCharacters": 5000 }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "livePrefix": "x/", "stagingPrefix": "x/" }"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => BroadcastConfig.Parse("""{ "voiceIdd": "x" }"""));
    }

    [Fact]
    public void TheCheckedInConfig_IsValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "broadcast.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var config = BroadcastConfig.Load(Path.Combine(dir!.FullName, "broadcast.json"));
        Assert.Equal("Asia/Bangkok", config.TimeZone);
    }

    [Fact]
    public async Task Discord_PostsOneEmbed_ThatCanPingNobody()
    {
        var payload = DiscordWebhook.Payload(SampleEpisode(new string('x', 5000)), "The Chronicle");
        var embed = payload["embeds"]![0]!;
        Assert.Equal("Aerenmoor, Years 11–12", (string)embed["title"]!);
        Assert.Equal(DiscordWebhook.MaxDescription, ((string)embed["description"]!).Length);
        Assert.Empty(payload["allowed_mentions"]!["parse"]!.AsArray());

        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"), (HttpStatusCode.TooManyRequests, ""));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "The Chronicle");
        await discord.PublishAsync(SampleEpisode(), null);
        Assert.Null(JsonNode.Parse(http.Seen[0].Body)!["embeds"]![0]!["image"]);
        Assert.Equal("https://discord.test/api/webhooks/1/SECRET-TOKEN?wait=true", http.Seen[0].Request.RequestUri!.ToString());
        Assert.Equal("The Chronicle", (string)JsonNode.Parse(http.Seen[0].Body)!["username"]!);
        var error = await Assert.ThrowsAsync<ChannelException>(() => discord.PublishAsync(SampleEpisode(), null));
        Assert.Equal("Discord answered HTTP 429.", error.Message);

        var slow = new DiscordWebhook(new HttpClient(new ThrowingHttp()), "https://discord.test/api/webhooks/1/SECRET-TOKEN", "x");
        var late = await Assert.ThrowsAsync<ChannelException>(() => slow.PublishAsync(SampleEpisode(), null));
        Assert.DoesNotContain("SECRET", late.Message);
    }

    [Fact]
    public async Task Discord_SendsTheMapAsTheEmbedsImage_InOneRequest()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "{}"));
        var discord = new DiscordWebhook(new HttpClient(http), "https://discord.test/api/webhooks/1/T", "The Chronicle");
        byte[] map = [0x89, 0x50, 0x4e, 0x47, 1, 2, 3];
        await discord.PublishAsync(SampleEpisode(), map);
        var (request, body) = http.Seen.Single();
        Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
        var text = System.Text.Encoding.Latin1.GetString(body!);
        Assert.Contains("name=payload_json", text);
        Assert.Contains("\"url\":\"attachment://map.png\"", text);
        Assert.Contains("filename=map.png", text);
        Assert.Contains(System.Text.Encoding.Latin1.GetString(map), text);
    }

    private sealed class ThrowingHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new TaskCanceledException();
    }
}

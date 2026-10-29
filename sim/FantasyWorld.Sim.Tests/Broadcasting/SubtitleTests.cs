using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class SubtitleTests
{
    /// <summary>An alignment that says each character in a tenth of a second, from <paramref name="from"/>.</summary>
    private static Alignment Spoken(string text, double from = 0) =>
        new(text.Select(c => c.ToString()).ToList(), text.Select((_, i) => from + i * 0.1).ToList(), text.Select((_, i) => from + i * 0.1 + 0.1).ToList());

    [Fact]
    public void Cues_AreTheVoicesOwnTimes_BrokenAtSentencesAndKeptShort()
    {
        const string text = "The war began. Nandlarmoor marched on Thussis, and the hosts of the south were waiting for them at the ford.";
        var cues = Subtitles.Cues(Spoken(text), offset: 10);
        Assert.Equal("The war began.", cues[0].Text);
        Assert.Equal(10, cues[0].Start, 3);
        Assert.Equal(10 + "The war began.".Length * 0.1, cues[0].End, 3); // its last character's end
        Assert.All(cues, c => Assert.True(c.Text.Length <= Subtitles.MaxLine, c.Text));
        Assert.Equal(text, string.Join(" ", cues.Select(c => c.Text)));
        Assert.All(cues.Zip(cues.Skip(1)), p => Assert.True(p.First.End <= p.Second.Start));
    }

    [Fact]
    public void Srt_IsNumbered_Stamped_AndEachCueReadable()
    {
        var srt = Subtitles.Srt(Subtitles.Paced([new Cue(1.25, 1.5, "So it began."), new Cue(1.9, 3, "And so it ended.")]));
        Assert.Equal("1\n00:00:01,250 --> 00:00:01,850\nSo it began.\n\n2\n00:00:01,900 --> 00:00:03,000\nAnd so it ended.\n\n", srt);
    }

    [Fact]
    public void ElevenLabsAlignment_IsRead_WholeOrNotAtAll()
    {
        var whole = JsonNode.Parse("""{"characters":["H","i"],"character_start_times_seconds":[0,0.1],"character_end_times_seconds":[0.1,0.2]}""");
        Assert.Equal(["H", "i"], ElevenLabs.Read(whole)!.Characters);
        Assert.Null(ElevenLabs.Read(JsonNode.Parse("""{"characters":["H","i"],"character_start_times_seconds":[0],"character_end_times_seconds":[0.1,0.2]}""")));
        Assert.Null(ElevenLabs.Read(null));
    }

    private sealed class TimedSpeech : ISpeech
    {
        public Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        /// <summary>One second of 128 kbps sound (16,000 bytes) per part, its characters spread over it.</summary>
        public Task<SpokenPart> SpeakTimedAsync(SpeechRequest request, CancellationToken ct = default) =>
            Task.FromResult(new SpokenPart(new byte[16_000], new Alignment(request.Text.Select(c => c.ToString()).ToList(),
                request.Text.Select((_, i) => i / (double)request.Text.Length).ToList(), request.Text.Select((_, i) => (i + 1) / (double)request.Text.Length).ToList())));

        public Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task EachPartsCues_StartWhereThePartStartsInTheJoinedNarration()
    {
        var voice = new BroadcastVoice { OutputFormat = "mp3_44100_128" };
        var (_, _, cues) = await Voiceover.SpeakTimedAsync(new TimedSpeech(), ["The first section.", "The second.", "The third."], voice, []);
        Assert.Equal(["The first section.", "The second.", "The third."], cues.Select(c => c.Text));
        Assert.Equal([0.0, 1.0, 2.0], cues.Select(c => Math.Round(c.Start, 3)));
    }

    [Fact]
    public void TheFilm_IsJoinedWithItsSubtitlesBurnedIn_OnlyWhenItHasThem()
    {
        var plain = FfmpegVideo.JoinArguments("list.txt", "a.mp3", "out.mp4");
        Assert.Contains("copy", plain);
        var subtitled = FfmpegVideo.JoinArguments("list.txt", "a.mp3", "out.mp4", subtitles: "subtitles.srt", tall: true);
        Assert.Contains(subtitled, a => a.StartsWith("subtitles=subtitles.srt:force_style=", StringComparison.Ordinal) && a.Contains("MarginV=48"));
        Assert.Contains("libx264", subtitled);
    }
}

using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Speech;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class SpeechTests
{
    private static readonly BroadcastVoice Voice = new();

    [Fact]
    public async Task ElevenLabs_SpeaksWithTheVoiceModelAndDictionaries_KeyInTheHeaderOnly()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, "ID3audio"));
        var speech = new ElevenLabs(new HttpClient(http), "SECRET-KEY");
        var audio = await speech.SpeakAsync(new SpeechRequest("voice/1", "eleven_v4_turbo", "Hear this.", "Before.", null,
            [new DictionaryRef("dict1", "v7")], "mp3_44100_128"));
        Assert.Equal("ID3audio"u8.ToArray(), audio);
        var (request, body) = http.Seen.Single();
        Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/voice%2F1?output_format=mp3_44100_128", request.RequestUri!.ToString());
        Assert.Equal("SECRET-KEY", request.Headers.GetValues("xi-api-key").Single());
        var json = JsonNode.Parse(body)!;
        Assert.Equal(("Hear this.", "eleven_v4_turbo", "Before."), ((string)json["text"]!, (string)json["model_id"]!, (string)json["previous_text"]!));
        Assert.Equal("dict1", (string)json["pronunciation_dictionary_locators"]![0]!["pronunciation_dictionary_id"]!);
        Assert.Equal("v7", (string)json["pronunciation_dictionary_locators"]![0]!["version_id"]!);
        Assert.DoesNotContain("SECRET", JsonNode.Parse(body)!.ToJsonString());
    }

    [Fact]
    public async Task ElevenLabs_CreatesAliasDictionaries_AndFailsWithoutLeakingTheKey()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, """{"id":"d1","version_id":"v1","name":"x"}"""), (HttpStatusCode.Unauthorized, ""));
        var speech = new ElevenLabs(new HttpClient(http), "SECRET-KEY");
        var made = await speech.CreateDictionaryAsync("Aerenmoor", new Dictionary<string, string> { ["Deithkialwold"] = "Deeth-kyal-wold" });
        Assert.Equal(new DictionaryRef("d1", "v1"), made);
        var rule = JsonNode.Parse(http.Seen[0].Body)!["rules"]![0]!;
        Assert.Equal(("alias", "Deithkialwold", "Deeth-kyal-wold"), ((string)rule["type"]!, (string)rule["string_to_replace"]!, (string)rule["alias"]!));
        var error = await Assert.ThrowsAsync<SpeechException>(() => speech.SpeakAsync(new SpeechRequest("v", "m", "t", null, null, [], "mp3_44100_128")));
        Assert.Equal("ElevenLabs could not speak: HTTP 401.", error.Message);
    }

    [Fact]
    public async Task ElevenLabs_ExtendsADictionary_AndTurnsAGarbledAnswerIntoASpeechError()
    {
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK, """{"id":"d1","version_id":"v2","version_rules_num":3}"""),
            (HttpStatusCode.OK, "<html>proxy</html>"), (HttpStatusCode.OK, """{"id":"d1"}"""));
        var speech = new ElevenLabs(new HttpClient(http), "K");
        Assert.Equal(new DictionaryRef("d1", "v2"), await speech.AddRulesAsync("d1", new Dictionary<string, string> { ["Fiar"] = "Fee-ar" }));
        Assert.Equal("https://api.elevenlabs.io/v1/pronunciation-dictionaries/d1/add-rules", http.Seen[0].Request.RequestUri!.ToString());
        Assert.Equal("Fee-ar", (string)JsonNode.Parse(http.Seen[0].Body)!["rules"]![0]!["alias"]!);
        await Assert.ThrowsAsync<SpeechException>(() => speech.AddRulesAsync("d1", new Dictionary<string, string> { ["A"] = "B" }));
        await Assert.ThrowsAsync<SpeechException>(() => speech.CreateDictionaryAsync("n", new Dictionary<string, string> { ["A"] = "B" }));
    }

    [Fact]
    public void Scripts_AreSplitOnParagraphs_UnderTheLimit_AndNothingIsLost()
    {
        var paragraphs = Enumerable.Range(1, 9).Select(i => string.Join(" ", Enumerable.Repeat($"Sentence {i} is told.", 8))).ToList();
        var script = string.Join("\n\n", paragraphs);
        var chunks = Voiceover.Chunks(script, 400);
        Assert.All(chunks, c => Assert.True(c.Length <= 400));
        Assert.Equal(script.Replace("\n\n", " "), string.Join(" ", chunks).Replace("\n\n", " "));
        Assert.Single(Voiceover.Chunks(script, 10_000));

        var long1 = string.Join(" ", Enumerable.Repeat("A very long sentence goes on.", 40));
        Assert.All(Voiceover.Chunks(long1, 200), c => Assert.True(c.Length <= 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => Voiceover.Chunks(script, 50));
    }

    [Fact]
    public void Parts_AreJoinedFrameToFrame_AndTimedByBitRate()
    {
        byte[] tagged = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 2, 0xAA, 0xBB, 0xFF, 0xFB];
        var joined = Voiceover.Join([[0xFF, 0xFB, 1], tagged]);
        Assert.Equal(new byte[] { 0xFF, 0xFB, 1, 0xFF, 0xFB }, joined);
        Assert.Equal(128, Voiceover.Kbps("mp3_44100_128"));
        Assert.Equal(60, Voiceover.Seconds(960_000, 128));
        Assert.Throws<FormatException>(() => Voiceover.Kbps("pcm_16000"));
    }

    [Fact]
    public void VoiceSettings_HaveTheOwnersDefaults_AndRefuseNonsense()
    {
        var config = BroadcastConfig.Parse("{}");
        Assert.Equal(("eleven_v4_turbo", "uju3wxzG5OhpWcoi3SMy", true), (config.Voice.Model, config.Voice.VoiceId, config.Voice.Speak));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "voice": { "maxCharactersPerRequest": 10 } }"""));
        Assert.Throws<FormatException>(() => BroadcastConfig.Parse("""{ "voice": { "outputFormat": "pcm_16000" } }"""));
    }
}

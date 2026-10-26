using System.Text.Json;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class PronouncerTests
{
    private const string Script = "The chronicle of Deithkialwold begins. The Duchy of Manreach declared war at Gewick. And so it was.";

    internal sealed class FakeSpeech : ISpeech
    {
        public List<IReadOnlyDictionary<string, string>> Dictionaries { get; } = [];
        public List<SpeechRequest> Spoken { get; } = [];
        public bool Fails { get; set; }
        public bool RefusesDictionaries { get; set; }

        public Task<byte[]> SpeakAsync(SpeechRequest request, CancellationToken ct = default)
        {
            if (Fails) throw new SpeechException("ElevenLabs could not speak: HTTP 500.");
            Spoken.Add(request);
            return Task.FromResult(new byte[] { 0xFF, 0xFB, (byte)Spoken.Count });
        }

        public List<IReadOnlyDictionary<string, string>> Added { get; } = [];

        public Task<DictionaryRef> CreateDictionaryAsync(string name, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default)
        {
            if (RefusesDictionaries) throw new SpeechException("ElevenLabs could not store the pronunciations: HTTP 401.");
            Dictionaries.Add(aliases);
            return Task.FromResult(new DictionaryRef($"d{Dictionaries.Count}", "v1"));
        }

        public Task<DictionaryRef> AddRulesAsync(string dictionaryId, IReadOnlyDictionary<string, string> aliases, CancellationToken ct = default)
        {
            Added.Add(aliases);
            return Task.FromResult(new DictionaryRef(dictionaryId, $"v{Added.Count + 1}"));
        }
    }

    internal static FakeLlmProvider Respeller() => new(request => JsonSerializer.Serialize(new
    {
        respellings = new object[]
        {
            new { word = "Deithkialwold", say = "Deeth-kyal-wold" }, new { word = "Manreach", say = "Man-reech" },
            new { word = "Gewick", say = "<script>" }, new { word = "Unasked", say = "Un-asked" },
        },
    }));

    [Fact]
    public void Words_AreTheScriptsNames_NotItsOrdinaryWords()
    {
        Assert.Equal(new[] { "Deithkialwold", "Manreach", "Gewick" }, Pronouncer.Words(Script));
        Assert.Equal(new[] { "Manreach" }, Pronouncer.Words("Manreach's host. Manreach’s banner. The Banner fell; a banner rose."));
    }

    [Fact]
    public async Task NewNames_AreAskedOnce_CheckedAndKeptForGood()
    {
        var manifest = new Manifest();
        var provider = Respeller();
        var log = new StringWriter();
        Assert.Equal(2, await Pronouncer.LearnAsync(provider, "claude-sonnet-5-5", manifest, Script, log));
        Assert.Equal("Deeth-kyal-wold", manifest.Pronunciations["Deithkialwold"]);
        Assert.False(manifest.Pronunciations.ContainsKey("Gewick")); // its answer was unusable: asked again
        Assert.Equal(1, manifest.PronunciationTries["Gewick"]);
        Assert.False(manifest.Pronunciations.ContainsKey("Unasked"));
        Assert.Equal(ModelTier.Narrate, provider.Requests.Single().Tier);

        Assert.Equal(0, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log));
        Assert.Equal(1, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log)); // third try: said as written
        Assert.Equal("Gewick", manifest.Pronunciations["Gewick"]);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal(0, await Pronouncer.LearnAsync(provider, "m", manifest, Script, log));
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal(new[] { "Deithkialwold", "Manreach" }, Pronouncer.Rules(manifest).Keys);
    }

    [Fact]
    public void ARespelling_MustLookLikeItsName()
    {
        var asked = new[] { "Morwen", "Manreach", "Fiar", "Gewick" };
        var said = Pronouncer.Parse(JsonSerializer.Serialize(new
        {
            respellings = new object[]
            {
                new { word = "Morwen", say = "the traitor king" }, new { word = "Manreach", say = "Man-reech’s." },
                new { word = "Fiar", say = "Fiar" }, new { word = "Gewick", say = "Zzz-wick" },
            },
        }), asked)!;
        Assert.Equal(new[] { "Fiar", "Manreach" }, said.Keys.Order());
        Assert.Equal("Man-reech's", said["Manreach"]);
        Assert.False(Pronouncer.LooksLike("Deeth-kyal-wold-and-more-and-more-and-more", "Deithkialwold"));
        Assert.Null(Pronouncer.Parse("not json", asked));
    }

    [Fact]
    public async Task WithoutAModel_OrWithAGarbledAnswer_NothingIsLearned()
    {
        var manifest = new Manifest();
        Assert.Equal(0, await Pronouncer.LearnAsync(null, "m", manifest, Script, TextWriter.Null));
        Assert.Equal(0, await Pronouncer.LearnAsync(new FakeLlmProvider(_ => throw new LlmUnavailableException("down")), "m", manifest, Script, TextWriter.Null));
        Assert.Empty(manifest.PronunciationTries);
        Assert.Equal(0, await Pronouncer.LearnAsync(new FakeLlmProvider(_ => "{\"nope\":1}"), "m", manifest, Script, TextWriter.Null));
        Assert.Empty(manifest.Pronunciations);
    }

    [Fact]
    public async Task TheDictionary_IsMadeOnce_ThenExtended()
    {
        var manifest = new Manifest();
        var speech = new FakeSpeech();
        Assert.Empty(await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"));
        await Pronouncer.LearnAsync(Respeller(), "m", manifest, Script, TextWriter.Null);
        var first = await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor");
        Assert.Equal(first, await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"));
        Assert.Single(speech.Dictionaries);
        Assert.Empty(speech.Added);
        manifest.Pronunciations["Fiasmere"] = "Fee-as-meer";
        Assert.Equal(new DictionaryRef("d1", "v2"), (await Pronouncer.DictionaryAsync(speech, manifest, "Aerenmoor"))[0]);
        Assert.Equal(new[] { "Fiasmere" }, speech.Added.Single().Keys);
    }
}

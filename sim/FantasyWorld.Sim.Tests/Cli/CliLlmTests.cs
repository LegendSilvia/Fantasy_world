using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Cli;

public class CliLlmTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    /// <summary>Answers every job with the first legal key, a name or a sentence.</summary>
    public static FakeLlmProvider Agreeable() => new(request =>
    {
        var properties = JsonDocument.Parse(request.Schema).RootElement.GetProperty("properties");
        string First(string field) => properties.GetProperty(field).GetProperty("enum")[0].GetString()!;
        if (properties.TryGetProperty("choice", out _)) return $"{{\"choice\":\"{First("choice")}\",\"reasoning\":\"It seemed right.\",\"flavor\":\"\"}}";
        if (properties.TryGetProperty("outcome", out _)) return $"{{\"outcome\":\"{First("outcome")}\",\"turning_point\":\"\",\"account\":\"So it went.\"}}";
        if (properties.TryGetProperty("name", out _)) return "{\"name\":\"The Named Thing\",\"lore\":\"Remembered in song.\"}";
        return "{\"text\":\"Once, long ago, it happened.\"}";
    });

    private static (int Code, string Output) Run(Func<LlmSettings, ILlmProvider?>? providers, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => T0, providers ?? (_ => null));
        return (code, output.ToString());
    }

    [Fact]
    public void LlmSettings_AreShown_Validated_AndSaved()
    {
        using var file = new TempFile();
        Run(null, "new", file.Path, "--seed", "3", "--size", "tiny");

        var (code, text) = Run(null, "llm", file.Path, "--provider", "anthropic", "--model", "narrate=claude-sonnet-5-5", "--cap", "2.5");
        Assert.Equal(0, code);
        Assert.Contains("provider: anthropic", text);
        Assert.Contains("narrate   claude-sonnet-5-5", text);
        Assert.Contains("decide    claude-opus-5-5", text);
        Assert.Contains("cap $2.50 per 30 days", text);
        Assert.Contains("ANTHROPIC_API_KEY", text);
        using (var store = WorldStore.Open(file.Path)) Assert.Equal("anthropic", store.LoadLlmSettings().Provider);

        Assert.Contains("error: Unknown provider", Run(null, "llm", file.Path, "--provider", "oracle").Output);
        Assert.Contains("error: Use --model", Run(null, "llm", file.Path, "--model", "dream=x").Output);
    }

    [Fact]
    public void Advancing_WithAProvider_JournalsItsAnswers()
    {
        using var file = new TempFile();
        Run(null, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(null, "llm", file.Path, "--provider", "anthropic", "--story-beats", "1000");
        var fake = Agreeable();

        var (code, text) = Run(_ => fake, "advance", file.Path, "5y");

        Assert.Equal(0, code);
        Assert.NotEmpty(fake.Requests);
        Assert.Contains($"LLM: {fake.Requests.Count} new answer(s)", text);
        Assert.Contains("motive=It seemed right.", text);
        var stats = Run(null, "stats", file.Path).Output;
        Assert.Contains($"LLM: {fake.Requests.Count} answers journaled", stats);
    }

    [Fact]
    public void Tell_NarratesTheWhyChain_OfflineByTemplate()
    {
        using var file = new TempFile();
        Run(null, "new", file.Path, "--seed", "3", "--size", "tiny");
        Run(null, "advance", file.Path, "10y");
        var (code, text) = Run(null, "tell", file.Path, "1");
        Assert.Equal(0, code);
        Assert.StartsWith("In year ", text);
        Assert.Contains("error: No event #999999", Run(null, "tell", file.Path, "999999").Output);
    }
}

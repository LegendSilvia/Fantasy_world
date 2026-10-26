using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Tests.Llm;

public class LlmGatewayTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Failing : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => "failing";
        public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
        {
            Calls++;
            throw new LlmUnavailableException("offline");
        }
    }

    private static readonly LlmSettings On = new() { Provider = "fake" };

    private static LlmRequest Request(string key = "decide|char#1|day 10", ModelTier tier = ModelTier.Decide, string prompt = "Choose.") =>
        new(tier, key, "bible", prompt, "{}");

    private static bool Valid(string json) => json.StartsWith('{');

    [Fact]
    public void Answers_AreJournaled_AndReplayedWithoutAsking()
    {
        var fake = new FakeLlmProvider(_ => "{\"choice\":\"a\"}");
        var journal = new LlmJournal();
        var gateway = new LlmGateway(fake, On, journal);
        Assert.Equal("{\"choice\":\"a\"}", gateway.Ask(Request(), 10, Valid));
        Assert.Single(journal.Pending);

        var replay = LlmGateway.Offline(journal);
        Assert.Equal("{\"choice\":\"a\"}", replay.Ask(Request(prompt: "Choose, reworded."), 10, Valid));
        Assert.Single(fake.Requests);
        Assert.Equal(1, replay.Replayed);
    }

    [Fact]
    public void WithoutAProvider_TheOfflinePathIsTaken()
    {
        var gateway = new LlmGateway(new FakeLlmProvider(_ => "{}"), new LlmSettings(), new LlmJournal());
        Assert.Null(gateway.Ask(Request(), 10, Valid));
        Assert.Equal(1, gateway.Fallbacks);
    }

    [Fact]
    public void InvalidAnswers_AreRetriedOnce()
    {
        var answers = new Queue<string>(new[] { "nonsense", "{\"ok\":1}" });
        var fake = new FakeLlmProvider(_ => answers.Dequeue());
        var gateway = new LlmGateway(fake, On, new LlmJournal());
        Assert.Equal("{\"ok\":1}", gateway.Ask(Request(), 10, Valid));

        var stubborn = new LlmGateway(new FakeLlmProvider(_ => "nonsense"), On, new LlmJournal());
        Assert.Null(stubborn.Ask(Request(), 10, Valid));
        Assert.All(stubborn.Journal.All, c => Assert.Equal("rejected", c.Note));
        Assert.False(stubborn.Knows(ModelTier.Decide, "decide|char#1|day 10"));
    }

    [Fact]
    public void ARejectedAnswer_KeepsWhyInItsNote()
    {
        var gateway = new LlmGateway(new FakeLlmProvider(_ => "nonsense"), On, new LlmJournal());
        Assert.Null(gateway.Ask(Request(), 10, Valid, whyInvalid: json => $"not JSON: {json}"));
        Assert.All(gateway.Journal.All, c => Assert.Equal("rejected: not JSON: nonsense", c.Note));
    }

    [Fact]
    public void ProviderFailures_FallBack()
    {
        var failing = new Failing();
        var gateway = new LlmGateway(failing, On, new LlmJournal());
        Assert.Null(gateway.Ask(Request(), 10, Valid));
        Assert.Equal(1, failing.Calls);
    }

    [Fact]
    public void StoryBeats_AndNarration_HaveSeparateRollingBudgets()
    {
        var clock = new Clock(DateTimeOffset.UnixEpoch);
        var settings = On with { StoryBeatsPerDay = 2, NarrationsPerDay = 1 };
        var gateway = new LlmGateway(new FakeLlmProvider(_ => "{}"), settings, new LlmJournal(), clock);
        Assert.NotNull(gateway.Ask(Request("a"), 1, Valid));
        Assert.NotNull(gateway.Ask(Request("b", ModelTier.Arbitrate), 1, Valid));
        Assert.Null(gateway.Ask(Request("c"), 1, Valid));
        Assert.NotNull(gateway.Ask(Request("n", ModelTier.Narrate), 1, Valid));
        Assert.Null(gateway.Ask(Request("m", ModelTier.Narrate), 1, Valid));

        clock.Now = clock.Now.AddHours(25);
        Assert.NotNull(gateway.Ask(Request("c"), 1, Valid));
    }

    [Fact]
    public void TheMonthlySpendCap_StopsNewCalls()
    {
        var clock = new Clock(DateTimeOffset.UnixEpoch);
        var journal = new LlmJournal();
        journal.Add(new LlmCall("old", ModelTier.Decide, 1, "anthropic", "m", "{}", 0, 0, 0, 0, 9.99m, clock.Now.AddDays(-2)));
        var gateway = new LlmGateway(new FakeLlmProvider(_ => "{}"), On with { MonthlySpendCapUsd = 10m }, journal, clock);
        Assert.NotNull(gateway.Ask(Request("a"), 1, Valid));
        journal.Add(new LlmCall("big", ModelTier.Decide, 1, "anthropic", "m", "{}", 0, 0, 0, 0, 0.02m, clock.Now));
        Assert.Null(gateway.Ask(Request("b"), 1, Valid));
    }

    [Fact]
    public void Keys_NotPrompts_IdentifyAQuestion()
    {
        Assert.Equal(Request(prompt: "x").Hash, Request(prompt: "y").Hash);
        Assert.NotEqual(Request("k1").Hash, Request("k2").Hash);
        Assert.NotEqual(Request("k1").Hash, Request("k1", ModelTier.Arbitrate).Hash);
    }

    private sealed class Broken(Exception error) : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => "broken";
        public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
        {
            Calls++;
            throw error;
        }
    }

    [Fact]
    public void AnyProviderError_FallsBack_AndTheProviderIsNotAskedAgainThisRun()
    {
        var broken = new Broken(new TaskCanceledException("timed out"));
        var gateway = new LlmGateway(broken, On, new LlmJournal());
        Assert.Null(gateway.Ask(Request("a"), 10, Valid));
        Assert.Null(gateway.Ask(Request("b"), 10, Valid));
        Assert.Equal(1, broken.Calls);
        Assert.False(gateway.Online);
        Assert.Equal("timed out", gateway.Failure);
        Assert.False(gateway.MayAnswer(ModelTier.Decide, "c"));
    }

    [Fact]
    public void RejectedAndRefusedAnswers_AreBilled_ButNeverReplayed()
    {
        var answers = new Queue<string>(new[] { "nonsense", "{\"ok\":1}" });
        var journal = new LlmJournal();
        var gateway = new LlmGateway(new FakeLlmProvider(_ => answers.Dequeue()), On with { StoryBeatsPerDay = 2 }, journal);
        Assert.Equal("{\"ok\":1}", gateway.Ask(Request("a"), 10, Valid));
        Assert.Equal(2, journal.Count);
        Assert.Contains(journal.All, c => c.Note == "rejected" && c.Json == "");
        Assert.False(gateway.MayAnswer(ModelTier.Decide, "b"));

        var refusing = new Broken(new LlmRefusedException("no", new LlmResponse("", "x", "m", 100, 5, 0, 0, 0.5m)));
        var ledger = new LlmJournal();
        var strict = new LlmGateway(refusing, On, ledger);
        Assert.Null(strict.Ask(Request("r"), 10, Valid));
        Assert.Equal(0.5m, ledger.SpendSince(DateTimeOffset.MinValue));
        Assert.True(strict.Online);
        Assert.False(LlmGateway.Offline(ledger).Knows(ModelTier.Decide, "r"));
    }
}

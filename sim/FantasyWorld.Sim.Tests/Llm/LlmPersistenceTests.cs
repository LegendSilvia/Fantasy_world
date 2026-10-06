using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Llm;

public class LlmPersistenceTests
{
    private static LlmCall Call(string hash, decimal cost = 0.0123m) =>
        new(hash, ModelTier.Arbitrate, 400, "anthropic", "claude-opus-5-5", "{\"outcome\":\"yield\"}",
            1200, 80, 1000, 0, cost, new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), "upset");

    [Fact]
    public void Journal_IsSavedWithTheMonth_AndLoadsBack()
    {
        using var file = new TempFile();
        var sim = new Simulator(WorldFactory.CreateNew(5, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(),
            new ISimSystem[] { new CoinSystem() });
        var journal = new LlmJournal();
        journal.Add(Call("a"));
        journal.Add(Call("b"));
        using (var store = WorldStore.Open(file.Path))
        {
            Assert.Equal(WorldStore.CurrentSchemaVersion, store.SchemaVersion);
            store.Save(sim.World, sim.Log, sim.Queue, journal);
            store.Save(sim.World, sim.Log, sim.Queue, journal);
        }
        Assert.Empty(journal.Pending);

        using var reopened = WorldStore.Open(file.Path);
        var loaded = reopened.LoadJournal();
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded.TryGet("a", out var a));
        Assert.Equal(Call("a"), a);
        Assert.Empty(loaded.Pending);
    }

    [Fact]
    public void Settings_RoundTrip_AndDefaultToOffline()
    {
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        Assert.False(store.LoadLlmSettings().Enabled);
        var settings = new LlmSettings { Provider = "anthropic", StoryBeatsPerDay = 12, MonthlySpendCapUsd = 2.5m };
        settings.Models["narrate"] = "claude-sonnet-5-5";
        store.SaveLlmSettings(settings);
        var loaded = store.LoadLlmSettings();
        Assert.Equal("anthropic", loaded.Provider);
        Assert.Equal(12, loaded.StoryBeatsPerDay);
        Assert.Equal(2.5m, loaded.MonthlySpendCapUsd);
        Assert.Equal("claude-sonnet-5-5", loaded.ModelFor(ModelTier.Narrate));
        Assert.Equal(LlmSettings.DefaultModel, loaded.ModelFor(ModelTier.Decide));
    }
}

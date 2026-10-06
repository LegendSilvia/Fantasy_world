using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Cli;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>
/// Spec §7: World = f(seed, event log); LLM results are stored and replay never re-queries. These pin down that
/// the LLM layer keeps that promise.
/// </summary>
public class LlmDeterminismTests
{
    private static readonly LlmSettings On = new() { Provider = "fake", StoryBeatsPerDay = 100_000 };

    private static (Simulator Sim, LlmGateway Gateway) Make(ILlmProvider? provider, LlmJournal journal, bool invariants = false)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var log = new EventLog();
        var gateway = new LlmGateway(provider, provider is null ? new LlmSettings() : On, journal);
        var story = new StoryContext(world, log, gateway);
        var systems = DefaultSystems.Create(new LlmDecider(story), new LlmArbiter(story), new LlmInventor(story));
        return (new Simulator(world, log, new EventQueue(), systems, invariants), gateway);
    }

    private static string Hash(Simulator sim) => WorldHasher.Hash(sim.World, sim.Log, sim.Queue);

    [Fact]
    public void OfflineLlmAgency_LeavesTheWorldExactlyAsTheRulesWouldHave()
    {
        var rules = new Simulator(WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Tiny), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create());
        var (offline, _) = Make(null, new LlmJournal());
        rules.AdvanceDays(360 * 30);
        offline.AdvanceDays(360 * 30);
        Assert.Equal(Hash(rules), Hash(offline));
    }

    [Fact]
    public void AnLlmWorld_ReplaysFromItsJournal_WithoutAsking()
    {
        var fake = CliLlmTests.Agreeable();
        var journal = new LlmJournal();
        var (live, _) = Make(fake, journal, invariants: true);
        live.AdvanceDays(360 * 20);
        Assert.NotEmpty(fake.Requests);
        Assert.Contains(live.Log.All, e => e.Payload.ContainsKey("motive"));

        var (replay, gateway) = Make(null, journal);
        replay.AdvanceDays(360 * 20);
        Assert.Equal(Hash(live), Hash(replay));
        Assert.Equal(journal.Count, gateway.Replayed);
        Assert.Equal(0, gateway.Asked);
    }

    [Fact]
    public void AnLlmWorld_IsChunkAndReloadInvariant()
    {
        var oneGo = CliLlmTests.Agreeable();
        var (whole, _) = Make(oneGo, new LlmJournal());
        whole.AdvanceDays(360 * 12);

        using var file = new Support.TempFile();
        var journal = new LlmJournal();
        var (first, _) = Make(CliLlmTests.Agreeable(), journal);
        first.AdvanceDays(360 * 5);
        using (var store = WorldStore.Open(file.Path)) store.Save(first.World, first.Log, first.Queue, journal);

        using var reopened = WorldStore.Open(file.Path);
        var (world, log, queue) = reopened.Load();
        var gateway = new LlmGateway(CliLlmTests.Agreeable(), On, reopened.LoadJournal());
        var story = new StoryContext(world, log, gateway);
        var resumed = new Simulator(world, log, queue, DefaultSystems.Create(new LlmDecider(story), new LlmArbiter(story), new LlmInventor(story)));
        resumed.AdvanceDays(360 * 7);

        Assert.Equal(Hash(whole), Hash(resumed));
    }

    /// <summary>Always picks the least likely listed outcome or option: upsets galore, so the drama budget engages.</summary>
    private static FakeLlmProvider Contrarian() => new(request =>
    {
        var properties = System.Text.Json.JsonDocument.Parse(request.Schema).RootElement.GetProperty("properties");
        string Last(string field) => properties.GetProperty(field).GetProperty("enum").EnumerateArray().Last().GetString()!;
        if (properties.TryGetProperty("choice", out _)) return $"{{\"choice\":\"{Last("choice")}\",\"reasoning\":\"Against the odds.\",\"flavor\":\"\"}}";
        if (properties.TryGetProperty("outcome", out _)) return $"{{\"outcome\":\"{Last("outcome")}\",\"turning_point\":\"\",\"account\":\"\"}}";
        if (properties.TryGetProperty("name", out _)) return "{\"name\":\"Upset\",\"lore\":\"\"}";
        return "{\"text\":\"x\"}";
    });

    [Fact]
    public void UnderTheDramaBudget_AReplayStillMatches()
    {
        var journal = new LlmJournal();
        var (live, _) = Make(Contrarian(), journal);
        live.AdvanceDays(360 * 20);
        Assert.Contains(journal.All, c => c.Note == "upset");

        var (replay, gateway) = Make(null, journal);
        replay.AdvanceDays(360 * 20);
        Assert.Equal(Hash(live), Hash(replay));
        Assert.Equal(journal.All.Count(c => c.Json.Length > 0), gateway.Replayed);
    }
}

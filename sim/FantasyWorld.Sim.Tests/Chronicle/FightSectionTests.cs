using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Tests.Chronicle;

/// <summary>The fight's own section (sub-project 8 B3): a duel that lasted days, told once, day by day.</summary>
public class FightSectionTests
{
    private static (World World, EventLog Log, WorldEvent Fought) AThreeDayDuel()
    {
        var (world, log, strong, sly, declared) = FaceOffTests.ADuelDeclared();
        var actors = new Dictionary<string, EntityRef> { ["challenger"] = strong.Ref, ["defender"] = sly.Ref };
        WorldEvent Emit(string type, string account, long cause, Dictionary<string, string> payload, int importance) =>
            log.Append(world.Date, new EventDraft(type, Actors: actors, Place: declared.Place,
                Payload: new Dictionary<string, string>(payload) { ["account"] = account, ["stakes"] = "to the death" },
                Causes: new[] { new Cause(cause, CauseKind.Trigger, 1.0) }, Importance: importance, Tags: new[] { "Characters", "Deeds" }));
        var begun = Emit("DuelBegun", "They circled in the rain until dusk, neither landing a blow.", declared.Id,
            new() { ["days"] = "3" }, 30);
        Emit("DuelTurned", "On the second morning the knight's blade found a gap in the mail.", begun.Id, new() { ["day"] = "2", ["days"] = "3" }, 35);
        var fought = Emit("DuelFought", "On the third day the wounded schemer yielded at the well.", begun.Id,
            new() { ["day"] = "3", ["days"] = "3", ["victor"] = "1" }, 45);
        return (world, log, fought);
    }

    [Fact]
    public void AFightOfOneDay_IsToldByHowItWasFought_NotOnlyItsOutcome()
    {
        // The owner, 2026-10-31: a duel of one day was told only by who won.
        var (world, log, strong, sly, declared) = FaceOffTests.ADuelDeclared();
        var fought = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = strong.Ref, ["defender"] = sly.Ref }, Place: declared.Place,
            Payload: new Dictionary<string, string> { ["account"] = "The axe bit the shield in two, and the elder fell back bleeding.", ["stakes"] = "first blood", ["victor"] = "1" },
            Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Characters", "Deeds" }));
        Assert.Single(Fights.Beats(log, fought));
        var episode = Episode.Build(world, log, 0, "2026-10-28", stories: 8);
        Assert.Contains(episode.Parts, p => p.Text.Contains("The axe bit the shield in two", StringComparison.Ordinal));
    }

    [Fact]
    public void AFightOfDays_IsOneStory_ToldDayByDay()
    {
        var (world, log, fought) = AThreeDayDuel();
        var beats = Fights.Beats(log, fought);
        Assert.Equal([1, 2, 3], beats.Select(b => b.Day));
        Assert.StartsWith("They circled", beats[0].Text);

        var episode = Episode.Build(world, log, 0, "2026-10-28", stories: 8);
        var told = Assert.Single(episode.Parts, p => p.Beats is not null);
        Assert.Equal(beats, told.Beats);
        // Its other days are not told again as stories of their own.
        Assert.Single(episode.Parts, p => p.EventId != 0 && log.TryGet(p.EventId, out var ev) && Fights.RootOf(log, ev) == Fights.RootOf(log, fought)
                                          && p.Kind is EpisodeSection.Headline or EpisodeSection.Story);
        Assert.Contains("How it went:", told.Text);
        Assert.Contains("second morning", told.Text);
    }

    [Fact]
    public void TheChronicler_IsGivenEveryDay_OfTheFight()
    {
        var (world, log, _) = AThreeDayDuel();
        var episode = Episode.Build(world, log, 0, "2026-10-28", stories: 8);
        var fake = new FakeLlmProvider(_ => System.Text.Json.JsonSerializer.Serialize(new { sections = episode.Parts.Select(p => p.Text) }));
        var story = new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal()));
        var written = new LlmNarrator(story).EpisodeScript(episode, 20_000);
        var prompt = fake.Requests.First(r => r.Key.StartsWith("narrate|episode-sections", StringComparison.Ordinal)).Prompt;
        Assert.Contains("How the fight was fought; tell its blows, not only its outcome:", prompt);
        Assert.Contains("Day 2: On the second morning", prompt);
        Assert.Contains("blow by blow", prompt);
        Assert.NotNull(written.Parts.Single(p => p.Beats is not null).Beats);
    }
}

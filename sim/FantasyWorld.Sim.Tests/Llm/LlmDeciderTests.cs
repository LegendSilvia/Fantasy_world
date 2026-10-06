using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Llm;

public class LlmDeciderTests
{
    private static readonly LlmSettings On = new() { Provider = "fake" };

    private static (StoryContext Story, Character Who) Story(Func<LlmRequest, string> answer, out FakeLlmProvider fake)
    {
        var world = TestPeople.World();
        var who = TestPeople.Add(world, 1, 30, role: Role.Knight);
        fake = new FakeLlmProvider(answer);
        return (new StoryContext(world, new EventLog(), new LlmGateway(fake, On, new LlmJournal())), who);
    }

    private static DecisionBrief Brief(Character who) => new(who.Id, "renown", new[]
    {
        new DecisionOption("rest", 90, "carry on as before"),
        new DecisionOption("hunt:2", 10, "hunt the monsters of the hills"),
        new DecisionOption("never", 0, "an option the engine ruled out"),
    });

    [Fact]
    public void TheLlm_MayChooseAnyLegalOption_AndGivesAMotive()
    {
        var (story, who) = Story(_ => "{\"choice\":\"hunt:2\",\"reasoning\":\"Glory waits in the hills.\",\"flavor\":\"\"}", out var fake);
        var choice = new LlmDecider(story).Decide(Brief(who), new Rng(1));
        Assert.Equal("hunt:2", choice.Key);
        Assert.Equal("Glory waits in the hills.", choice.Reasoning);

        var request = fake.Requests.Single();
        Assert.Equal(ModelTier.Decide, request.Tier);
        Assert.Equal(story.Bible, request.System);
        Assert.Contains(who.Name, request.Prompt);
        Assert.Contains("hunt:2: hunt the monsters of the hills (inclination 10%)", request.Prompt);
        Assert.DoesNotContain("never", request.Prompt);
        var allowed = JsonDocument.Parse(request.Schema).RootElement.GetProperty("properties").GetProperty("choice").GetProperty("enum");
        Assert.Equal(new[] { "rest", "hunt:2" }, allowed.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void IllegalOrMissingAnswers_FallBackToTheRulesChoice_WithTheSameRandomness()
    {
        var expected = new RulesDecider().Decide(Brief(TestPeople.Add(TestPeople.World(), 1, 30)), new Rng(9));
        var (story, who) = Story(_ => "{\"choice\":\"never\",\"reasoning\":\"x\",\"flavor\":\"\"}", out var fake);
        var rng = new Rng(9);
        var choice = new LlmDecider(story).Decide(Brief(who), rng);
        Assert.Equal(expected.Key, choice.Key);
        Assert.Null(choice.Reasoning);
        Assert.Equal(2, fake.Requests.Count);
        var rulesRng = new Rng(9);
        new RulesDecider().Decide(Brief(who), rulesRng);
        Assert.Equal(rulesRng.NextDouble(), rng.NextDouble());
    }

    [Fact]
    public void AnUnusedMotive_NeverLeaksOntoLaterEvents()
    {
        var world = TestPeople.World();
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        using (ctx.Annotate("motive", "unused")) { }
        Assert.Empty(ctx.Emit(new EventDraft("Later")).Payload);
        using (ctx.Annotate("motive", "used"))
        {
            Assert.Equal("used", ctx.Emit(new EventDraft("First")).Payload["motive"]);
            Assert.Empty(ctx.Emit(new EventDraft("Second")).Payload);
        }
    }

    [Fact]
    public void AWarDeclaredByLlm_CarriesTheRulersMotive()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var raid = log.Append(world.Date, new EventDraft("BorderRaid"));
        world.Realms[1].Memories.Add(new RealmMemory(raid.Id, 2, -80, 90));
        var fake = new FakeLlmProvider(r => r.Key.Contains("war or peace", StringComparison.Ordinal)
            ? "{\"choice\":\"war:0\",\"reasoning\":\"The raid must be answered.\",\"flavor\":\"\"}"
            : "{\"choice\":\"peace\",\"reasoning\":\"\",\"flavor\":\"\"}");
        var story = new StoryContext(world, log, new LlmGateway(fake, On, new LlmJournal()));
        new Simulator(world, log, new EventQueue(), new ISimSystem[] { new DiplomacySystem(new LlmDecider(story)) }, checkInvariants: true)
            .AdvanceDays(360);

        var declared = log.All.Single(e => e.Type == "WarDeclared");
        Assert.Equal("The raid must be answered.", declared.Payload["motive"]);
    }

    [Fact]
    public void NestedAnnotations_RestoreTheOuterText()
    {
        var ctx = new SimContext(TestPeople.World(), new EventLog(), new EventQueue(), new Rng(1));
        using (ctx.Annotate("motive", "outer"))
        {
            using (ctx.Annotate("motive", "inner")) { }
            Assert.Equal("outer", ctx.Emit(new EventDraft("After")).Payload["motive"]);
        }
    }
}

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

public class LlmArbiterTests
{
    private static readonly LlmSettings On = new() { Provider = "fake" };

    private sealed class Duelist : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith("duel", StringComparison.Ordinal)) ?? brief.Options[0];
    }

    private static string[] Allowed(LlmRequest request) =>
        JsonDocument.Parse(request.Schema).RootElement.GetProperty("properties").GetProperty("outcome").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static (StoryContext Story, Encounter Duel) Setup(FakeLlmProvider fake, LlmJournal? journal = null)
    {
        var world = TestPeople.World();
        var strong = TestPeople.Add(world, 1, 30, role: Role.Knight);
        strong.Attributes.Prowess = 18;
        var weak = TestPeople.Add(world, 1, 30);
        weak.Attributes.Prowess = 4;
        var story = new StoryContext(world, new EventLog(), new LlmGateway(fake, On, journal ?? new LlmJournal()));
        return (story, Encounters.DuelBetween(world, strong, weak, toTheDeath: true));
    }

    [Fact]
    public void TheLlm_ChoosesAPlausibleOutcome_AndTellsHowItHappened()
    {
        var fake = new FakeLlmProvider(_ => "{\"outcome\":\"attacker_maims\",\"turning_point\":\"a slip on wet stone\",\"account\":\"The duel ended in the rain.\"}");
        var (story, duel) = Setup(fake);
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(1));
        Assert.Equal("attacker_maims", outcome.Key);
        Assert.Equal("The duel ended in the rain. Turning point: a slip on wet stone", outcome.Account);

        var allowed = Allowed(fake.Requests.Single());
        Assert.All(allowed, key => Assert.True(duel.Outcomes.Single(o => o.Key == key).Probability >= Encounter.PlausibilityFloor));
        Assert.DoesNotContain("both_die", allowed);
        Assert.Contains("odds of prevailing", fake.Requests.Single().Prompt);
    }

    [Fact]
    public void ImplausibleAnswers_FallBackToTheRulesRoll()
    {
        var fake = new FakeLlmProvider(_ => "{\"outcome\":\"both_die\",\"turning_point\":\"\",\"account\":\"\"}");
        var (story, duel) = Setup(fake);
        var expected = new RulesArbiter().Resolve(duel, new Rng(4));
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(4));
        Assert.Equal(expected.Key, outcome.Key);
        Assert.Null(outcome.Account);
    }

    [Fact]
    public void BattleFates_StayWithTheRules()
    {
        var fake = new FakeLlmProvider(_ => "{}");
        var (story, _) = Setup(fake);
        var knight = story.World.Characters.Values.First();
        new LlmArbiter(story).Resolve(MilitarySystem.BattleFate(knight, won: true), new Rng(1));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void TooManyUpsets_TightenTheOdds()
    {
        var journal = new LlmJournal();
        for (var i = 0; i < LlmArbiter.DramaBudget; i++)
            journal.Add(new LlmCall($"old{i}", ModelTier.Arbitrate, 0, "fake", "m", "{}", 0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch, "upset"));
        var fake = new FakeLlmProvider(_ => "{\"outcome\":\"attacker_kills\",\"turning_point\":\"\",\"account\":\"\"}");
        var (story, duel) = Setup(fake, journal);
        new LlmArbiter(story).Resolve(duel, new Rng(1));
        var request = fake.Requests.Single();
        Assert.All(Allowed(request), key => Assert.True(duel.Outcomes.Single(o => o.Key == key).Probability >= LlmArbiter.TightFloor));
        Assert.Contains("Upsets have been frequent", request.Prompt);
    }

    [Fact]
    public void UnderdogWins_AreMarkedAsUpsets()
    {
        var fake = new FakeLlmProvider(_ => "{\"outcome\":\"defender_wounds\",\"turning_point\":\"\",\"account\":\"\"}");
        var (story, duel) = Setup(fake);
        new LlmArbiter(story).Resolve(duel, new Rng(1));
        Assert.Equal("upset", story.Gateway.Journal.All.Single().Note);
    }

    [Fact]
    public void ADuelArbitratedByLlm_CarriesItsAccount()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var insult = log.Append(world.Date, new EventDraft("RivalryBegan"));
        var hothead = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Wrathful });
        var other = TestPeople.Add(world, 1, 30);
        hothead.Memories.Add(new Memory(insult.Id, other.Id, -90, 90));
        var fake = new FakeLlmProvider(_ => "{\"outcome\":\"attacker_first_blood\",\"turning_point\":\"\",\"account\":\"A cut to the cheek ended it.\"}");
        var story = new StoryContext(world, log, new LlmGateway(fake, On, new LlmJournal()));
        new Simulator(world, log, new EventQueue(), new ISimSystem[] { new DeedsSystem(new Duelist(), new LlmArbiter(story)) }, checkInvariants: true)
            .AdvanceDays(360 * 3);

        var duel = log.All.First(e => e.Type == "DuelFought");
        Assert.Equal("A cut to the cheek ended it.", duel.Payload["account"]);
    }
}

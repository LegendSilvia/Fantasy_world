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

    /// <summary>A whole-duel answer: the outcome, its days and its chapters as (day, text).</summary>
    private static string Whole(string outcome, int days, params (int Day, string Text)[] chapters) =>
        JsonSerializer.Serialize(new { outcome, days, chapters = chapters.Select(c => new { day = c.Day, text = c.Text }) });

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
    public void TheLlm_DecidesTheWholeDuel_ItsOutcomeLengthAndCourse()
    {
        var fake = new FakeLlmProvider(_ => Whole("attacker_maims", 200, (1, "The first bout was even."), (90, "A slip on wet stone turned it."),
            (200, "It ended in the rain.")));
        var (story, duel) = Setup(fake);
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(1));
        Assert.Equal("attacker_maims", outcome.Key);
        Assert.Equal(200, outcome.Days);
        Assert.Equal(new[] { 1, 90, 200 }, outcome.Chapters.Select(c => c.Day));
        Assert.Equal("The first bout was even. A slip on wet stone turned it. It ended in the rain.", outcome.Account);
        Assert.Contains("up to 720 days", fake.Requests.Single().Prompt);

        var allowed = Allowed(fake.Requests.Single());
        Assert.All(allowed, key => Assert.True(duel.Outcomes.Single(o => o.Key == key).Probability >= Encounter.PlausibilityFloor));
        Assert.DoesNotContain("both_die", allowed);
        Assert.Contains("odds of prevailing", fake.Requests.Single().Prompt);
    }

    [Fact]
    public void ImplausibleAnswers_FallBackToTheRulesRoll()
    {
        var fake = new FakeLlmProvider(_ => Whole("both_die", 1, (1, "Both fell."), (1, "None stood.")));
        var (story, duel) = Setup(fake);
        var expected = new RulesArbiter().Resolve(duel, new Rng(4));
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(4));
        Assert.Equal(expected.Key, outcome.Key);
        Assert.Null(outcome.Account);
    }

    [Theory]
    [InlineData(0, "days must be a whole number from 1 to 720")]
    [InlineData(721, "days must be a whole number from 1 to 720")]
    public void AFightTooShortOrTooLong_IsRefused(int days, string why)
    {
        var fake = new FakeLlmProvider(_ => Whole("attacker_maims", days, (1, "It began."), (1, "It ended.")));
        var (story, duel) = Setup(fake);
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(4));
        Assert.Equal(1, outcome.Days);
        Assert.Empty(outcome.Chapters);
        Assert.All(story.Gateway.Journal.All, c => Assert.Equal($"rejected: {why}", c.Note));
    }

    [Fact]
    public void ACourseOutOfOrder_OrInventingNames_IsRefused_AndTheRetryIsToldWhy()
    {
        var answers = new Queue<string>(new[]
        {
            Whole("attacker_maims", 30, (20, "It began."), (10, "Sir Bogus struck."), (25, "It ended.")),
            Whole("attacker_maims", 30, (1, "It began."), (30, "It ended.")),
        });
        var fake = new FakeLlmProvider(_ => answers.Dequeue());
        var (story, duel) = Setup(fake);
        var outcome = new LlmArbiter(story).Resolve(duel, new Rng(4));
        Assert.Equal(30, outcome.Days);
        var why = story.Gateway.Journal.All.Single(c => c.Note.StartsWith("rejected", StringComparison.Ordinal)).Note;
        Assert.Contains("each chapter's day must fall within the fight, in order", why);
        Assert.Contains("Bogus", why);
        Assert.Contains("the last chapter must fall on the fight's last day", why);
        Assert.Contains("An earlier answer was rejected for:", fake.Requests[1].Prompt);
    }

    [Fact]
    public void AnAnswerJournaledBeforeFightsHadALength_StillReplays_AsOneDay()
    {
        var journal = new LlmJournal();
        var (_, duel) = Setup(new FakeLlmProvider(_ => ""));
        var key = $"arbitrate|duel|{duel.AttackerId}|{duel.DefenderId}|{TestPeople.World().Date.Day}";
        journal.Add(new LlmCall(LlmRequest.HashOf(ModelTier.Arbitrate + "|" + key), ModelTier.Arbitrate, 0, "fake", "m",
            "{\"outcome\":\"attacker_maims\",\"turning_point\":\"a slip\",\"account\":\"It ended in the rain.\"}", 0, 0, 0, 0, 0, DateTimeOffset.UnixEpoch));
        var (story, again) = Setup(new FakeLlmProvider(_ => throw new InvalidOperationException("asked")), journal);
        var outcome = new LlmArbiter(story).Resolve(again, new Rng(1));
        Assert.Equal(("attacker_maims", 1), (outcome.Key, outcome.Days));
        Assert.Equal("It ended in the rain. Turning point: a slip", outcome.Account);
    }

    /// <summary>Two hosts on one field, the realms' marshals in command, and the stage the arbiter is shown.</summary>
    private static (StoryContext Story, BattleStage Stage) Field(FakeLlmProvider fake)
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var host = MilitarySystem.Mobilise(ctx, war, world.Realms[1]);
        var guard = MilitarySystem.Mobilise(ctx, war, world.Realms[2]);
        host.Levies = guard.Levies = 1000;
        var region = world.Regions[host.RegionId];
        var story = new StoryContext(world, log, new LlmGateway(fake, On, new LlmJournal()));
        return (story, new BattleStage(war.Id, host.Id, guard.Id, region.Id, MilitarySystem.Strength(world, host, region),
            MilitarySystem.Strength(world, guard, region), [host.CommanderId], [guard.CommanderId]));
    }

    private static string Battle(string outcome, int days, int victor, int vanquished, string n1, string n2) =>
        JsonSerializer.Serialize(new
        {
            outcome, days, victor_losses = victor, vanquished_losses = vanquished,
            chapters = new[] { new { day = 1, text = "The lines met." }, new { day = days, text = "The field was won." } },
            fates = new { n1, n2 },
        });

    [Fact]
    public void TheLlm_DecidesTheWholeBattle_WithinTheRulesRanges()
    {
        var fake = new FakeLlmProvider(_ => Battle("defender_wins", 40, 12, 30, "slain", "wounded"));
        var (story, stage) = Field(fake);
        var plan = new LlmArbiter(story).PlanBattle(stage)!;
        Assert.Equal((false, 40, 0.12, 0.30), (plan.AttackerWins, plan.Days, plan.WinnerLosses, plan.LoserLosses));
        Assert.Equal(Fate.Dead, plan.Fates[stage.AttackerNotables[0]]);
        Assert.Equal(Fate.Wounded, plan.Fates[stage.DefenderNotables[0]]);
        var request = fake.Requests.Single();
        Assert.Contains("n1:", request.Prompt);
        Assert.Contains("odds of winning", request.Prompt);
        Assert.Contains("up to 360 days", request.Prompt);
    }

    [Theory]
    [InlineData(20, 30, "wounded", "wounded", "the victors' losses are out of their range")]
    [InlineData(10, 50, "wounded", "wounded", "the vanquished's losses are out of their range")]
    [InlineData(10, 30, "wounded", "slain", "n2's fate is not one their side's lot allows")]
    public void ABattleOutsideTheRules_IsRefused_AndTheRulesFightIt(int victor, int vanquished, string n1, string n2, string why)
    {
        var fake = new FakeLlmProvider(_ => Battle("defender_wins", 1, victor, vanquished, n1, n2));
        var (story, stage) = Field(fake);
        Assert.Null(new LlmArbiter(story).PlanBattle(stage));
        Assert.All(story.Gateway.Journal.All, c => Assert.Contains(why, c.Note));
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
        var fake = new FakeLlmProvider(_ => Whole("attacker_kills", 1, (1, "It began."), (1, "It ended.")));
        var (story, duel) = Setup(fake, journal);
        new LlmArbiter(story).Resolve(duel, new Rng(1));
        var request = fake.Requests.Single();
        Assert.All(Allowed(request), key => Assert.True(duel.Outcomes.Single(o => o.Key == key).Probability >= LlmArbiter.TightFloor));
        Assert.Contains("Upsets have been frequent", request.Prompt);
    }

    [Fact]
    public void UnderdogWins_AreMarkedAsUpsets()
    {
        var fake = new FakeLlmProvider(_ => Whole("defender_wounds", 1, (1, "It began."), (1, "It ended.")));
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
        var fake = new FakeLlmProvider(_ => Whole("attacker_first_blood", 1, (1, "They circled."), (1, "A cut to the cheek ended it.")));
        var story = new StoryContext(world, log, new LlmGateway(fake, On, new LlmJournal()));
        new Simulator(world, log, new EventQueue(), new ISimSystem[] { new DeedsSystem(new Duelist(), new LlmArbiter(story)) }, checkInvariants: true)
            .AdvanceDays(360 * 5);

        var duel = log.All.First(e => e.Type == "DuelFought");
        Assert.Equal("They circled. A cut to the cheek ended it.", duel.Payload["account"]);
        Assert.False(duel.Payload.ContainsKey("days"));
    }
}

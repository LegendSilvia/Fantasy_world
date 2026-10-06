using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Agency;

public class AgencyTests
{
    [Fact]
    public void RulesDecider_FollowsWeights()
    {
        var brief = new DecisionBrief(1, "test", new[]
        {
            new DecisionOption("often", 3, ""),
            new DecisionOption("rarely", 1, ""),
            new DecisionOption("never", 0, ""),
        });
        var rng = new Rng(5);
        var picks = Enumerable.Range(0, 4000).Select(_ => new RulesDecider().Decide(brief, rng).Key).ToList();
        Assert.DoesNotContain("never", picks);
        Assert.InRange(picks.Count(k => k == "often") / 4000.0, 0.72, 0.78);
    }

    [Fact]
    public void RulesDecider_RejectsBriefsWithoutChoices() =>
        Assert.Throws<ArgumentException>(() =>
            new RulesDecider().Decide(new DecisionBrief(1, "stuck", new[] { new DecisionOption("x", 0, "") }), new Rng(1)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuelOutcomes_SumToOne(bool toTheDeath)
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = Encounters.DuelBetween(world, a, b, toTheDeath);
        Assert.Equal(1.0, duel.Outcomes.Sum(o => o.Probability), 9);
        Assert.Equal(toTheDeath, duel.Outcomes.Any(o => o.AttackerFate == Fate.Dead || o.DefenderFate == Fate.Dead));
    }

    [Fact]
    public void Strength_CountsTraitsWoundsAndArtifacts()
    {
        var world = TestPeople.World();
        var plain = TestPeople.Add(world, 1, 30);
        var brave = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave });
        var wounded = TestPeople.Add(world, 1, 30);
        wounded.Wounds.Add("leg");
        var armed = TestPeople.Add(world, 1, 30);
        TestPeople.GiveArtifact(world, armed, new ArtifactPower("attribute", "prowess", 4));

        Assert.Equal(15, Encounters.Strength(world, plain));
        Assert.Equal(17, Encounters.Strength(world, brave));
        Assert.Equal(13, Encounters.Strength(world, wounded));
        Assert.Equal(19, Encounters.Strength(world, armed));
    }

    [Fact]
    public void StrongerDuellist_WinsMoreOften()
    {
        var world = TestPeople.World();
        var strong = TestPeople.Add(world, 1, 30);
        strong.Attributes.Prowess = 20;
        var weak = TestPeople.Add(world, 1, 30);
        weak.Attributes.Prowess = 4;
        var duel = Encounters.DuelBetween(world, strong, weak, toTheDeath: false);
        var rng = new Rng(9);
        var wins = Enumerable.Range(0, 2000).Count(_ => new RulesArbiter().Resolve(duel, rng).AttackerWins);
        Assert.InRange(wins / 2000.0, duel.AttackerOdds - 0.04, duel.AttackerOdds + 0.04);
        Assert.True(duel.AttackerOdds > 0.6);
    }

    [Fact]
    public void Hunts_AreHarderInMoreDangerousRegions()
    {
        var world = TestPeople.World();
        var hunter = TestPeople.Add(world, 1, 30);
        world.Regions[1].Danger = 20;
        world.Regions[2].Danger = 90;
        Assert.True(Encounters.HuntIn(world, hunter, world.Regions[1]).AttackerOdds
                    > Encounters.HuntIn(world, hunter, world.Regions[2]).AttackerOdds);
        Assert.Equal(1.0, Encounters.DelveIn(world, hunter, world.Regions[2]).Outcomes.Sum(o => o.Probability), 9);
    }

    [Fact]
    public void PlausibilityFloor_FlagsUnlikelyOutcomes()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = Encounters.DuelBetween(world, a, b, toTheDeath: true);
        Assert.False(duel.IsPlausible(duel.Outcomes.Single(o => o.Key == "both_die")));
        Assert.True(duel.IsPlausible(duel.Outcomes.Single(o => o.Key == "attacker_kills")));
    }
}

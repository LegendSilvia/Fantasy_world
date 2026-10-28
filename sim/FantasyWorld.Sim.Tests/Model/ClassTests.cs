using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class ClassTests
{
    [Fact]
    public void EachClass_BeatsOne_AndIsBeatenByAnother()
    {
        Assert.All(Classes.All, k =>
        {
            Assert.Single(Classes.All, other => Classes.Counters(k, other));
            Assert.Single(Classes.All, other => Classes.Counters(other, k));
            Assert.NotEmpty(Classes.Why(k));
        });
        Assert.True(Classes.Counters(Classes.Mage, Classes.Warrior));
        Assert.Equal(1.15, Classes.Edge(Classes.Ranger, Classes.Paladin));
        Assert.Equal(1, Classes.Edge(Classes.Paladin, Classes.Ranger));
    }

    [Fact]
    public void ACounter_TiltsADuel_By15Percent()
    {
        var world = TestPeople.World();
        var (a, b) = (TestPeople.Add(world, 1, 30), TestPeople.Add(world, 1, 30));
        a.Class = b.Class = Classes.Warrior;
        var even = Encounters.DuelBetween(world, a, b, toTheDeath: true).Outcomes.Where(o => o.AttackerWins).Sum(o => o.Probability);
        a.Class = Classes.Mage;
        var tilted = Encounters.DuelBetween(world, a, b, toTheDeath: true).Outcomes.Where(o => o.AttackerWins).Sum(o => o.Probability);
        var (sa, sb) = (Encounters.Strength(world, a), Encounters.Strength(world, b));
        Assert.Equal(0.97 * sa * 1.15 / (sa * 1.15 + sb), tilted, 6);
        Assert.True(tilted > even);
    }

    [Fact]
    public void AGrownCharacter_HasACalling_FromTheirGifts()
    {
        var world = TestPeople.World();
        var c = TestPeople.Add(world, 1, 30);
        c.Attributes.Lore = 20;
        c.Attributes.Prowess = c.Attributes.Martial = c.Attributes.Intrigue = c.Attributes.Stewardship = c.Attributes.Diplomacy = 1;
        Assert.Equal(Classes.Mage, Classes.Choose(world, c));
    }
}

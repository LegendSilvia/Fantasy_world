using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FamilySystemTests
{
    private static Simulator Make(World world, IDecider? decider = null) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new FamilySystem(decider) }, checkInvariants: true);

    /// <summary>Always picks the first option.</summary>
    private sealed class EagerDecider : IDecider
    {
        public List<DecisionBrief> Briefs { get; } = new();
        public DecisionOption Decide(DecisionBrief brief, Rng rng)
        {
            Briefs.Add(brief);
            return brief.Options[0];
        }
    }

    [Fact]
    public void Neighbours_Marry()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 25, Sex.Male);
        var b = TestPeople.Add(world, 1, 24, Sex.Female);
        var decider = new EagerDecider();
        var sim = Make(world, decider);

        sim.AdvanceDays(360 * 10);

        Assert.Equal(b.Id, a.SpouseId);
        var wedding = sim.Log.All.First(e => e.Type == "MarriageWed");
        Assert.Equal(wedding.Id, a.MarriageEventId);
        Assert.Contains(decider.Briefs, br => br.Situation == "courtship" && br.Options.Any(o => o.Key == "unwed"));
    }

    [Fact]
    public void Kin_DoNotMarry()
    {
        var world = TestPeople.World();
        var mother = TestPeople.Add(world, 1, 60, Sex.Female);
        TestPeople.Add(world, 1, 25, Sex.Male, motherId: mother.Id, houseId: mother.HouseId);
        TestPeople.Add(world, 1, 24, Sex.Female, motherId: mother.Id, houseId: mother.HouseId);
        var sim = Make(world, new EagerDecider());
        sim.AdvanceDays(360 * 10);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "MarriageWed");
    }

    [Fact]
    public void Lords_StayAtTheirSeat()
    {
        var world = TestPeople.World();
        var lady = TestPeople.Add(world, 2, 30, Sex.Female, Role.Lord);
        var suitor = TestPeople.Add(world, 1, 30, Sex.Male);
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        FamilySystem.Wed(ctx, suitor, lady);
        Assert.Equal(2, suitor.SettlementId);
        Assert.Equal(2, lady.SettlementId);
    }

    [Fact]
    public void Matrilineal_Husbands_MoveToTheirWives()
    {
        var world = TestPeople.World();
        var culture = world.Cultures[1];
        world.Cultures[1] = new Culture { Id = 1, Name = culture.Name, HomeRegionId = 1, FaithId = 1, Lineage = Lineage.Matrilineal };
        var wife = TestPeople.Add(world, 2, 30, Sex.Female);
        var husband = TestPeople.Add(world, 1, 30, Sex.Male);
        FamilySystem.Wed(new SimContext(world, new EventLog(), new EventQueue(), new Rng(1)), husband, wife);
        Assert.Equal(2, husband.SettlementId);
    }

    [Fact]
    public void Couples_HaveChildren_InTheLineageHouse()
    {
        var world = TestPeople.World();
        var father = TestPeople.Add(world, 1, 25, Sex.Male);
        var mother = TestPeople.Add(world, 1, 22, Sex.Female);
        TestPeople.Marry(world, father, mother);
        var sim = Make(world);

        sim.AdvanceDays(360 * 10);

        Assert.InRange(father.Children.Count, 1, 6);
        foreach (var child in father.Children.Select(id => world.Characters[id]))
        {
            Assert.Equal(mother.Id, child.MotherId);
            Assert.Equal(father.HouseId, child.HouseId);
            Assert.Equal(1, child.SettlementId);
        }
        Assert.All(sim.Log.All.Where(e => e.Type == "CharacterBorn"), e => Assert.Equal(mother.Ref, e.Actors["mother"]));
    }

    [Fact]
    public void Elderly_Couples_HaveNoChildren()
    {
        var world = TestPeople.World();
        var father = TestPeople.Add(world, 1, 55, Sex.Male);
        var mother = TestPeople.Add(world, 1, 50, Sex.Female);
        TestPeople.Marry(world, father, mother);
        Make(world).AdvanceDays(360 * 5);
        Assert.Empty(mother.Children);
    }
}

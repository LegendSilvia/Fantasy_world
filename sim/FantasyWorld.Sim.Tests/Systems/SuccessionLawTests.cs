using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class SuccessionLawTests
{
    private static (World World, Character Ruler, Character Son, Character Brother) Dynasty(SuccessionLaw law)
    {
        var world = TestRealms.TwoKingdoms();
        world.Realms[1].SuccessionLaw = law;
        var ruler = world.Characters[world.Settlements[1].LordId];
        var father = TestPeople.Add(world, 1, 80, houseId: ruler.HouseId);
        var mother = TestPeople.Add(world, 1, 78, Sex.Female, houseId: ruler.HouseId);
        world.Characters.Remove(ruler.Id);
        var king = new Character
        {
            Id = ruler.Id, Name = ruler.Name, HouseId = ruler.HouseId, Sex = Sex.Male, BirthDay = ruler.BirthDay,
            CultureId = 1, FaithId = 1, SettlementId = 1, FatherId = father.Id, MotherId = mother.Id,
            Attributes = ruler.Attributes, Role = Role.Lord,
        };
        world.Characters[king.Id] = king;
        father.Children.Add(king.Id);
        mother.Children.Add(king.Id);
        var brother = TestPeople.Add(world, 1, 35, fatherId: father.Id, motherId: mother.Id, houseId: king.HouseId);
        var son = TestPeople.Add(world, 1, 18, fatherId: king.Id, houseId: king.HouseId);
        return (world, king, son, brother);
    }

    private static void Kill(World world, Character c) =>
        Fates.Die(new SimContext(world, new EventLog(), new EventQueue(), new Rng(1)), c, "illness", Array.Empty<Cause>());

    [Fact]
    public void Primogeniture_CrownsTheChild()
    {
        var (world, king, son, _) = Dynasty(SuccessionLaw.Primogeniture);
        Kill(world, king);
        Assert.Equal(son.Id, world.Settlements[1].LordId);
    }

    [Fact]
    public void Seniority_CrownsTheBrother()
    {
        var (world, king, _, brother) = Dynasty(SuccessionLaw.Seniority);
        Kill(world, king);
        Assert.Equal(brother.Id, world.Settlements[1].LordId);
    }

    [Fact]
    public void Elective_CrownsTheAblest_EvenFromTheCouncil()
    {
        var (world, king, _, _) = Dynasty(SuccessionLaw.Elective);
        var chancellor = TestPeople.Add(world, 1, 50);
        chancellor.Attributes.Stewardship = chancellor.Attributes.Diplomacy = chancellor.Attributes.Martial = 20;
        world.Realms[1].Council[CouncilSeats.Steward] = chancellor.Id;
        var log = new EventLog();
        Fates.Die(new SimContext(world, log, new EventQueue(), new Rng(1)), king, "illness", Array.Empty<Cause>());
        Assert.Equal(chancellor.Id, world.Settlements[1].LordId);
        Assert.False(world.Realms[1].Council.ContainsValue(chancellor.Id));
        Assert.Equal("Elective", log.All.Single(e => e.Type == "SuccessionOccurred").Payload["law"]);
        Assert.Empty(Invariants.Check(world, log));
    }
}

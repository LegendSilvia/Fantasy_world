using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class PeopleGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_StartsWithHundredsOfNotables()
    {
        var world = Medium();
        Assert.InRange(world.Characters.Count, 300, 800);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryTownAndCity_HasALord()
    {
        var world = Medium();
        foreach (var s in world.Settlements.Values.Where(s => s.Tier >= SettlementTier.Town))
        {
            var lord = world.Characters[s.LordId];
            Assert.Equal(Role.Lord, lord.Role);
            Assert.Equal(s.Id, lord.SettlementId);
            Assert.Equal(s.Id, world.Houses[lord.HouseId].SeatSettlementId);
            Assert.InRange(lord.Age(world.Date), 25, 55);
        }
    }

    [Fact]
    public void Children_AreYoungerThanBothParents_AndShareTheHouse()
    {
        var world = Medium();
        foreach (var child in world.Characters.Values.Where(c => c.FatherId != 0))
        {
            var father = world.Characters[child.FatherId];
            var mother = world.Characters[child.MotherId];
            Assert.True(father.Age(world.Date) - child.Age(world.Date) >= 18);
            Assert.True(mother.Age(world.Date) - child.Age(world.Date) >= 18);
            Assert.Equal(CharacterFactory.LineageParent(world, father, mother)!.HouseId, child.HouseId);
        }
    }

    [Fact]
    public void Notables_HaveCompatibleTraits_AndCultureOfTheirRegion()
    {
        var world = Medium();
        foreach (var c in world.Characters.Values)
        {
            Assert.InRange(c.Traits.Count, 2, 6); // three or four of the old pairs, a flaw or virtue, and some are unyielding
            Assert.True(world.Cultures.ContainsKey(c.CultureId));
            if (c.FatherId == 0)
                Assert.Equal(world.Regions[world.Settlements[c.SettlementId].RegionId].CultureId, c.CultureId);
        }
    }

    [Fact]
    public void AncientArtifacts_AreHeldByLordsOrLostInDangerousRegions()
    {
        var world = Medium();
        Assert.Equal(world.Regions.Count / 5, world.Artifacts.Count);
        foreach (var a in world.Artifacts.Values)
        {
            Assert.Equal(0, a.OriginEventId);
            if (a.Place == ArtifactPlace.Held) Assert.Equal(Role.Lord, world.Characters[a.HolderId].Role);
            else Assert.Equal(ArtifactPlace.Lost, a.Place);
            Assert.InRange(a.Fame, 30, 60);
        }
        var lostIn = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost).Select(a => world.Regions[a.RegionId].Danger);
        Assert.True(lostIn.Min() >= world.Regions.Values.Select(r => r.Danger).OrderByDescending(d => d).ElementAt(10));
    }

    [Fact]
    public void TinyWorld_HasPeopleToo()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.True(world.Characters.Count >= 10);
        Assert.Equal(3, world.Artifacts.Count);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }
}

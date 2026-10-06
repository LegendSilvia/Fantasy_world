using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Model;

public class PeopleModelTests
{
    [Fact]
    public void Family_SatisfiesInvariants()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, Sex.Male, Role.Lord);
        var lady = TestPeople.Add(world, 1, 38, Sex.Female, houseId: lord.HouseId);
        TestPeople.Marry(world, lord, lady);
        TestPeople.Add(world, 1, 10, Sex.Female, houseId: lord.HouseId, fatherId: lord.Id, motherId: lady.Id);
        TestPeople.GiveArtifact(world, lord, new ArtifactPower("attribute", "prowess", 3));
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Theory]
    [InlineData(10, LifeStage.Child)]
    [InlineData(16, LifeStage.Adult)]
    [InlineData(59, LifeStage.Adult)]
    [InlineData(60, LifeStage.Elder)]
    public void LifeStage_FollowsAge(int age, LifeStage expected)
    {
        var world = TestPeople.World();
        Assert.Equal(expected, TestPeople.Add(world, 1, age).Stage(world.Date));
    }

    [Fact]
    public void DatesBeforeHistory_ReadNaturally()
    {
        Assert.Equal("Y0 M12 D30", new WorldDate(-1).ToString());
        Assert.Equal("Y-1 M1 D1", new WorldDate(-720).ToString());
        Assert.True(new WorldDate(-360).IsFirstOfYear);
        Assert.Equal(new WorldDate(-725), WorldDate.FromYmd(-1, 12, 26).AddDays(-360));
    }

    [Fact]
    public void DeadCharacter_IsDeadStage()
    {
        var world = TestPeople.World();
        var c = TestPeople.Add(world, 1, 30);
        c.Died = world.Date;
        Assert.Equal(LifeStage.Dead, c.Stage(world.Date));
        Assert.False(c.IsActive);
    }

    [Fact]
    public void Opinion_CombinesTraitsKinshipAndMemories()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave, Traits.Honest });
        var b = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave, Traits.Deceitful });
        Assert.Equal(10 - 15, Relations.Opinion(a, b));

        a.Memories.Add(new Memory(1, b.Id, -80, 50));
        Assert.Equal(10 - 15 - 40, Relations.Opinion(a, b));

        TestPeople.Marry(world, a, b);
        Assert.Equal(10 - 15 - 40 + 30, Relations.Opinion(a, b));
    }

    [Fact]
    public void Siblings_AreKin()
    {
        var world = TestPeople.World();
        var mother = TestPeople.Add(world, 1, 40, Sex.Female);
        var x = TestPeople.Add(world, 1, 10, motherId: mother.Id, houseId: mother.HouseId);
        var y = TestPeople.Add(world, 1, 8, motherId: mother.Id, houseId: mother.HouseId);
        Assert.True(Relations.AreSiblings(x, y));
        Assert.True(Relations.AreKin(x, mother));
        Assert.False(Relations.AreSiblings(x, x));
    }

    [Fact]
    public void Attributes_AreClampedAndNamed()
    {
        var attributes = new Attributes();
        attributes.Set("lore", 25);
        Assert.Equal(20, attributes.Get("lore"));
        Assert.Throws<ArgumentException>(() => attributes.Get("charisma"));
    }

    [Fact]
    public void Artifact_BonusSumsAttributePowers()
    {
        var a = new Artifact { Powers = { new("attribute", "prowess", 3), new("attribute", "prowess", 2), new("ward", null, 0) } };
        Assert.Equal(5, a.Bonus("prowess"));
        Assert.Equal(0, a.Bonus("lore"));
    }

    [Fact]
    public void Invariants_CatchBrokenPeopleAndArtifacts()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var other = TestPeople.Add(world, 1, 40);
        lord.SpouseId = other.Id;
        var relic = TestPeople.GiveArtifact(world, other);
        other.Died = world.Date;
        world.Settlements[2].LordId = other.Id;
        relic.Fame = 120;

        var errors = Invariants.Check(world, new EventLog());

        Assert.Contains(errors, e => e.Contains("not married to them"));
        Assert.Contains(errors, e => e.Contains("with a dead or faded holder"));
        Assert.Contains(errors, e => e.Contains("not a living lord"));
        Assert.Contains(errors, e => e.Contains("fame 120"));
    }

    [Fact]
    public void Snapshot_RoundTripsPeople()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, Sex.Female, Role.Lord, traits: new[] { Traits.Kind });
        lord.Memories.Add(new Memory(0, 0, 10, 10));
        lord.Died = null;
        TestPeople.GiveArtifact(world, lord, new ArtifactPower("attribute", "lore", 2));
        var queue = new EventQueue();
        var json = JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options);
        var copy = JsonSerializer.Deserialize<WorldSnapshot>(json, SimJson.Options)!.ToWorld();

        Assert.Equal(json, JsonSerializer.Serialize(WorldSnapshot.Capture(copy, queue), SimJson.Options));
        Assert.Equal(Traits.Kind, copy.Characters[lord.Id].Traits.Single());
        Assert.Equal(2, copy.Artifacts[1].Bonus("lore"));
        Assert.Equal(lord.Id, copy.Settlements[1].LordId);
        Assert.Equal("Westfolk", copy.Cultures[1].Name);
    }
}

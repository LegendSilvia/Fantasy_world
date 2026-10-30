using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class ArtifactSystemTests
{
    private static Simulator Make(World world) =>
        new(world, new EventLog(), new EventQueue(), new ISimSystem[] { new ArtifactSystem() }, checkInvariants: true);

    [Fact]
    public void Curse_ComesToLight_AndMarksTheHolder()
    {
        var world = TestPeople.World();
        var holder = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Trusting });
        var cursed = new Artifact
        {
            Id = world.Ids.Take(Artifact.Kind),
            Name = "The Whispering Ring",
            Curse = "paranoia",
            Place = ArtifactPlace.Held,
            HolderId = holder.Id,
            Powers = { new("attribute", "intrigue", 2) },
        };
        world.Artifacts[cursed.Id] = cursed;
        var sim = Make(world);

        sim.AdvanceDays(360 * 30);

        Assert.True(cursed.CurseRevealed);
        var revealed = Assert.Single(sim.Log.All, e => e.Type == "ArtifactCurseRevealed");
        Assert.Equal("paranoia", revealed.Payload["curse"]);
        Assert.Contains(Traits.Paranoid, holder.Traits);
        Assert.DoesNotContain(Traits.Trusting, holder.Traits);
    }

    [Fact]
    public void LostArtifacts_FadeFromMemory()
    {
        var world = TestPeople.World();
        var relic = new Artifact { Id = world.Ids.Take(Artifact.Kind), Name = "The Drowned Crown", Fame = 40, Place = ArtifactPlace.Lost, RegionId = 2 };
        world.Artifacts[relic.Id] = relic;
        Make(world).AdvanceDays(360 * 10);
        Assert.Equal(30, relic.Fame);
    }

    [Fact]
    public void RenownedHolders_MakeArtifactsFamous()
    {
        var world = TestPeople.World();
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 60;
        var blade = TestPeople.GiveArtifact(world, hero);
        Make(world).AdvanceDays(360 * 10);
        Assert.Equal(10, blade.Fame);
    }

    [Fact]
    public void Lord_ClaimsTheTreasury()
    {
        var world = TestPeople.World();
        var lord = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var crown = new Artifact { Id = world.Ids.Take(Artifact.Kind), Name = "The Oak Crown", Place = ArtifactPlace.Treasury, SettlementId = 1 };
        world.Artifacts[crown.Id] = crown;
        var sim = Make(world);
        sim.AdvanceDays(360);
        Assert.Equal(lord.Id, crown.HolderId);
        Assert.Equal("claimed", crown.Provenance.Single().How);
    }
}

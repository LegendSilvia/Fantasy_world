using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class RelationsSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new RelationsSystem() }, checkInvariants: true);

    private static readonly string[] Virtuous = { Traits.Kind, Traits.Honest, Traits.Generous, Traits.Calm };
    private static readonly string[] Vicious = { Traits.Cruel, Traits.Deceitful, Traits.Greedy, Traits.Wrathful };

    [Fact]
    public void LikeMinds_BecomeFriends()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var b = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.Contains(sim.Log.All, e => e.Type == "FriendshipFormed");
        Assert.True(Relations.Opinion(a, b) >= Relations.FriendAt);
        Assert.DoesNotContain(sim.Log.All, e => e.Type == "RivalryBegan");
    }

    [Fact]
    public void Opposites_BecomeRivals()
    {
        var world = TestPeople.World();
        var a = TestPeople.Add(world, 1, 30, traits: Virtuous);
        var b = TestPeople.Add(world, 1, 30, traits: Vicious);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        var rivalry = sim.Log.All.First(e => e.Type == "RivalryBegan");
        Assert.Contains(" vs ", rivalry.Payload["over"]);
        Assert.True(Relations.Opinion(a, b) <= Relations.RivalAt);
    }

    [Fact]
    public void Strangers_InDifferentSettlements_NeverMeet()
    {
        var world = TestPeople.World();
        TestPeople.Add(world, 1, 30, traits: Virtuous);
        TestPeople.Add(world, 2, 30, traits: Virtuous);
        var sim = Make(world);
        sim.AdvanceDays(360 * 5);
        Assert.Empty(sim.Log.All);
    }

    [Fact]
    public void Memories_FadeAndAreForgotten()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ev = log.Append(world.Date, new EventDraft("Insult"));
        var c = TestPeople.Add(world, 1, 30);
        c.Memories.Add(new Memory(ev.Id, 0, -50, 10));
        var sim = Make(world, log);

        sim.AdvanceDays(360);
        Assert.Equal(8, c.Memories.Single().Weight, 6);

        sim.AdvanceDays(360 * 7);
        Assert.Empty(c.Memories);
    }
}

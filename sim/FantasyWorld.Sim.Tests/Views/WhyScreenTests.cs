using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

public class WhyScreenTests
{
    [Fact]
    public void TheWhyScreen_LayersCausesOnce_AndLabelsJointCauses()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ruler = TestPeople.Add(world, 1, 40);
        var drought = log.Append(new FantasyWorld.Sim.Core.WorldDate(10), new EventDraft("Drought", Place: world.Regions[1].Ref, Tags: new[] { "Regions" }));
        var shortage = log.Append(new FantasyWorld.Sim.Core.WorldDate(400), new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref,
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 1) }, Tags: new[] { "Economy" }));
        var insult = log.Append(new FantasyWorld.Sim.Core.WorldDate(500), new EventDraft("Insult", Actors: new Dictionary<string, EntityRef> { ["by"] = ruler.Ref },
            Payload: new Dictionary<string, string> { ["motive"] = "pride" }));
        var war = log.Append(new FantasyWorld.Sim.Core.WorldDate(800), new EventDraft("WarDeclared", Actors: new Dictionary<string, EntityRef> { ["attacker"] = ruler.Ref },
            Causes: new[] { new Cause(insult.Id, CauseKind.Motive, 0.9), new Cause(shortage.Id, CauseKind.Enabler, 0.5) }, Tags: new[] { "War" }));
        var peace = log.Append(new FantasyWorld.Sim.Core.WorldDate(900), new EventDraft("PeaceSigned", Causes: new[] { new Cause(war.Id, CauseKind.Trigger, 1) }));

        var why = WhyScreen.Build(world, log, war.Id);

        Assert.Equal(war.Id, why.Outcome.EventId);
        Assert.Equal(new[] { new[] { shortage.Id, insult.Id }, new[] { drought.Id } }, why.Layers.Select(l => l.Select(c => c.EventId).ToArray()));
        Assert.All(why.Edges.Where(e => e.To == war.Id), e => Assert.Equal("because, and", e.Label));
        Assert.Equal("because", why.Edges.Single(e => e.To == shortage.Id).Label);
        Assert.True(why.Layers[1].Single().IsRoot);
        Assert.Equal("pride", why.Layers[0].Single(c => c.EventId == insult.Id).Note);
        Assert.Equal("Traced back two years, through two root causes.", why.Summary);
        Assert.Equal(new[] { ruler.Ref }, why.People.Select(p => p.Ref));
        Assert.Equal(new[] { world.Regions[1].Ref }, why.Places.Select(p => p.Ref));
        Assert.Equal(new[] { Category.War, Category.Economy, Category.Characters, Category.Nature }.OrderBy(c => c), why.Systems);
        Assert.Equal(new[] { peace.Id }, why.Consequences.Select(c => c.EventId));

        var shallow = WhyScreen.Build(world, log, war.Id, depth: 1);
        Assert.True(shallow.Layers[0].Single(c => c.EventId == shortage.Id).HasMore);
    }
}

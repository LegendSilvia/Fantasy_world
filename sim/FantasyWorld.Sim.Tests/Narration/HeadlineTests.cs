using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Narration;

public class HeadlineTests
{
    [Fact]
    public void AHeadline_HasAShortTitle_AndABodyThatSaysWhatTheTitleDoesNot()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = new War { Id = world.Ids.Take(War.Kind), Name = "The War of the Ash", AttackerId = 1, DefenderId = 2 };
        world.Wars[war.Id] = war;
        var grudge = log.Append(world.Date, new EventDraft("RivalryDeclared",
            Actors: new Dictionary<string, EntityRef> { ["a"] = world.Realms[1].Ref, ["b"] = world.Realms[2].Ref }, Place: world.Settlements[1].Ref));
        var declared = log.Append(new WorldDate(400), new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = world.Realms[1].Ref, ["defender"] = world.Realms[2].Ref, ["war"] = war.Ref },
            Place: world.Settlements[1].Ref, Payload: new Dictionary<string, string> { ["casus belli"] = "border dispute" },
            Causes: new[] { new Cause(grudge.Id, CauseKind.Motive, 0.9) }));
        var narrator = new TemplateNarrator(world);

        Assert.Equal("The War of the Ash begins", narrator.Title(declared));
        var body = narrator.Body(declared, log);
        Assert.DoesNotContain(narrator.Title(declared), body);
        Assert.Contains("over a disputed border", body);
        Assert.Contains("Before it, in Year 1, the ", body);
        Assert.Contains("declared each other rivals", body);
    }

    [Fact]
    public void TheStoryOfWhy_RunsOldestFirst_AndEndsWithTheOutcome()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var drought = log.Append(new WorldDate(0), new EventDraft("Drought", Place: world.Regions[1].Ref));
        var shortage = log.Append(new WorldDate(400), new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref,
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 1) }));
        var revolt = log.Append(new WorldDate(800), new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Causes: new[] { new Cause(shortage.Id, CauseKind.Trigger, 1) }));
        var why = WhyScreen.Build(world, log, revolt.Id);
        var story = why.Story!.Text;
        Assert.StartsWith("In Year 1, ", story);
        Assert.Contains("In Year 2, food ran short", story);
        Assert.EndsWith($"And so, in Year 3, {world.Settlements[1].Name} rose in revolt.", story);
        Assert.Contains(why.Story.Links, l => l.Entity == world.Settlements[1].Ref);
    }

    [Fact]
    public void ABodysCause_ReadsWhateverItsSentenceOpensWith()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = new War { Id = world.Ids.Take(War.Kind), Name = "The War of the Ash", AttackerId = 1, DefenderId = 2 };
        world.Wars[war.Id] = war;
        var battle = log.Append(new WorldDate(0), new EventDraft("BattleFought",
            Actors: new Dictionary<string, EntityRef> { ["victor"] = world.Realms[1].Ref, ["vanquished"] = world.Realms[2].Ref, ["war"] = war.Ref }, Place: world.Regions[2].Ref));
        var fell = log.Append(new WorldDate(400), new EventDraft("RealmFell", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[2].Ref },
            Place: world.Settlements[2].Ref, Causes: new[] { new Cause(battle.Id, CauseKind.Trigger, 1) }));
        var body = new TemplateNarrator(world).Body(fell, log);
        Assert.DoesNotContain("after at", body);
        Assert.Contains("Before it, in Year 1, ", body);
        Assert.DoesNotContain(", The ", body);
    }
}

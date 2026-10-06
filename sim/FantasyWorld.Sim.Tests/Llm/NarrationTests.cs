using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Llm;

public class NarrationTests
{
    [Fact]
    public void Templates_NameThePeople_AndCarryMotivesAndAccounts()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = log.Append(new FantasyWorld.Sim.Core.WorldDate(360 * 4), new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = a.Ref, ["defender"] = b.Ref },
            Place: world.Settlements[1].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = "to first blood", ["motive"] = "an old insult", ["account"] = "It was quick." }));

        var text = new TemplateNarrator(world).Narrate(duel);

        Assert.Equal($"In year 5, {Names.Full(world, a)} fought {Names.Full(world, b)} to first blood at {world.Settlements[1].Name}. (an old insult) It was quick.", text);
    }

    [Fact]
    public void UnknownOrIncompleteEvents_ReadGenerically()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var a = TestPeople.Add(world, 1, 30);
        var odd = log.Append(world.Date, new EventDraft("StarFell", Actors: new Dictionary<string, EntityRef> { ["witness"] = a.Ref }));
        var partial = log.Append(world.Date, new EventDraft("MarriageWed", Actors: new Dictionary<string, EntityRef> { ["spouse"] = a.Ref }));
        var narrator = new TemplateNarrator(world);
        Assert.Equal($"In year 1, star fell involving {Names.Full(world, a)}.", narrator.Narrate(odd));
        Assert.Equal($"In year 1, marriage wed involving {Names.Full(world, a)}.", narrator.Narrate(partial));
    }

    [Fact]
    public void Chains_ReadAsBecause()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var shortage = log.Append(world.Date, new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref));
        var revolt = log.Append(world.Date, new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Causes: new[] { new Cause(shortage.Id, CauseKind.Trigger, 1.0) }));
        var text = new TemplateNarrator(world).NarrateChain(new HistoryQuery(log).WhyChain(revolt.Id));
        Assert.Equal($"In year 1, {world.Settlements[1].Name} rose in revolt. This happened because in year 1, food ran short in {world.Regions[1].Name}.", text);
    }

    [Fact]
    public void EveryEventOfARealHistory_Narrates()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 40);
        var narrator = new TemplateNarrator(world);
        foreach (var ev in sim.Log.All)
            Assert.StartsWith("In year ", narrator.Narrate(ev));
    }

    [Fact]
    public void TheLlmNarrator_IsCached_AndFallsBackToTemplates()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ev = log.Append(world.Date, new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Payload: new Dictionary<string, string> { ["lore"] = "Songs still blame the tax." }));
        var fake = new FakeLlmProvider(_ => "{\"text\":\"The village rose.\"}");
        var story = new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal()));
        var narrator = new LlmNarrator(story);

        Assert.Equal("The village rose.", narrator.Narrate(ev));
        Assert.Equal("The village rose.", narrator.Narrate(ev));
        var request = fake.Requests.Single();
        Assert.Equal(ModelTier.Narrate, request.Tier);
        Assert.Contains("Songs still blame the tax.", request.Prompt);

        var offline = new LlmNarrator(new StoryContext(world, log, LlmGateway.Offline()));
        Assert.Equal(new TemplateNarrator(world).Narrate(ev), offline.Narrate(ev));
    }
}

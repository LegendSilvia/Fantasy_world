using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Narration;

public class VarietyTests
{
    [Fact]
    public void NeighbouringEventsOfOneKind_ReadDifferently_AndAlwaysTheSame()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var weddings = Enumerable.Range(0, 3).Select(_ => log.Append(world.Date, new EventDraft("MarriageWed",
            Actors: new Dictionary<string, EntityRef> { ["spouse"] = a.Ref, ["partner"] = b.Ref }, Place: world.Settlements[1].Ref))).ToList();
        var narrator = new TemplateNarrator(world);
        var told = weddings.Select(narrator.Sentence).ToList();
        Assert.Equal(3, told.Distinct().Count());
        Assert.Equal(told, weddings.Select(new TemplateNarrator(world).Sentence));
    }

    [Fact]
    public void AChain_TellsEachCauseOnce()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var root = log.Append(world.Date, new EventDraft("Drought", Place: world.Regions[1].Ref));
        var shortage = log.Append(world.Date, new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref, Causes: new[] { new Cause(root.Id, CauseKind.Trigger, 1) }));
        var revolt = log.Append(world.Date, new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Causes: new[] { new Cause(shortage.Id, CauseKind.Trigger, 1), new Cause(root.Id, CauseKind.Enabler, 0.5) }));
        var text = new TemplateNarrator(world).NarrateChain(new HistoryQuery(log).WhyChain(revolt.Id, 5));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "drought|rain|withered", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [Fact]
    public void TheCommonEventsOfARealHistory_AllHaveTheirOwnWords()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 80);
        var narrator = new TemplateNarrator(world);
        var common = sim.Log.All.GroupBy(e => e.Type).Where(g => g.Count() >= 5).Select(g => g.First()).ToList();
        Assert.NotEmpty(common);
        // A generic line reads "<type in words> involving … at …"; every common event has a template instead.
        Assert.All(common, e => Assert.DoesNotMatch($"^{English.Capitalise(Briefs.Spaced(e.Type))}( involving | at |\\.$)", narrator.Sentence(e)));
    }

    [Fact]
    public void DeathsInTheWilds_RisingRealms_AndCountsOfPeople_ReadAsSentences()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var hunter = TestPeople.Add(world, 1, 30);
        var narrator = new TemplateNarrator(world);
        string Tell(string type, Dictionary<string, EntityRef> actors, Dictionary<string, string> payload, EntityRef? place = null) =>
            narrator.Sentence(log.Append(world.Date, new EventDraft(type, Actors: actors, Place: place ?? world.Regions[1].Ref, Payload: payload)));

        Assert.EndsWith("and was killed.", Tell("MonsterHuntFailed", new() { ["hunter"] = hunter.Ref }, new() { ["outcome"] = "failure_death" }));
        Assert.EndsWith("grew into a kingdom.", Tell("RealmRose", new() { ["realm"] = world.Realms[1].Ref }, new() { ["rank"] = "Kingdom" }, world.Settlements[1].Ref));
        Assert.StartsWith("One person left", Tell("Exodus", new() { ["to"] = world.Settlements[2].Ref }, new() { ["people"] = "1" }, world.Settlements[1].Ref));
        Assert.Contains("94 character born and 30 marriage wed",
            Tell("ChronicleCompacted", new(), new() { ["period"] = "Years 1–10", ["events"] = "124", ["most common"] = "94 CharacterBorn, 30 MarriageWed" }));
    }
}

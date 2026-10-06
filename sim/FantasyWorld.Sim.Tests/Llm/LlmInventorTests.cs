using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Llm;

public class LlmInventorTests
{
    private static readonly LlmSettings On = new() { Provider = "fake" };

    private sealed class Want(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[^1];
    }

    private static StoryContext Story(World world, EventLog log, Func<LlmRequest, string> answer) =>
        new(world, log, new LlmGateway(new FakeLlmProvider(answer), On, new LlmJournal()));

    [Fact]
    public void TheLlm_NamesThings_AndGivesThemLore()
    {
        var story = Story(TestPeople.World(), new EventLog(), _ => "{\"name\":\"\\\"Dawnbreaker\\\"\",\"lore\":\"Forged in the first light.\"}");
        var invention = new LlmInventor(story).Invent(new InventionBrief("artifact", "artifact#1", "A sword.", "The Blade of Ohm"));
        Assert.Equal("Dawnbreaker", invention.Name);
        Assert.Equal("Forged in the first light.", invention.Lore);
    }

    [Fact]
    public void OverlongOrEmptyNames_KeepTheEnginesName()
    {
        var story = Story(TestPeople.World(), new EventLog(), _ => $"{{\"name\":\"{new string('a', 80)}\",\"lore\":\"x\"}}");
        Assert.Equal(new Invention("The Blade of Ohm", null),
            new LlmInventor(story).Invent(new InventionBrief("artifact", "artifact#1", "A sword.", "The Blade of Ohm")));
    }

    [Fact]
    public void Wars_AreNamedByTheLlm_AndTheirDeclarationCarriesTheLore()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var raid = log.Append(world.Date, new EventDraft("BorderRaid"));
        world.Realms[1].Memories.Add(new RealmMemory(raid.Id, 2, -80, 90));
        var story = Story(world, log, _ => "{\"name\":\"The War of the Burnt Fords\",\"lore\":\"It began with smoke over the river.\"}");
        new Simulator(world, log, new EventQueue(), new ISimSystem[] { new DiplomacySystem(new Want("war"), new LlmInventor(story)) }, checkInvariants: true)
            .AdvanceDays(360);

        Assert.Equal("The War of the Burnt Fords", world.Wars.Values.Single().Name);
        var declared = log.All.Single(e => e.Type == "WarDeclared");
        Assert.Equal("The War of the Burnt Fords", declared.Payload["war"]);
        Assert.Equal("It began with smoke over the river.", declared.Payload["lore"]);
    }

    [Fact]
    public void ForgedArtifacts_AreNamedByTheLlm()
    {
        var world = TestPeople.World();
        world.Regions[1].LeyPower = 80;
        var mage = TestPeople.Add(world, 1, 40, role: Role.Mage);
        mage.Ambition = Ambitions.Magic;
        mage.Attributes.Lore = 20;
        var log = new EventLog();
        var story = Story(world, log, r => r.Prompt.Contains($"forged by {mage.Name}", StringComparison.Ordinal)
            ? "{\"name\":\"The Ember Crown\",\"lore\":\"It never cools.\"}" : "{}");
        new Simulator(world, log, new EventQueue(), new ISimSystem[] { new DeedsSystem(new Want("forge"), null, new LlmInventor(story)) }, checkInvariants: true)
            .AdvanceDays(360 * 6);

        var forged = log.All.First(e => e.Type == "ArtifactForged");
        var artifact = world.Artifacts[forged.Actors["artifact"].Id];
        Assert.Equal("The Ember Crown", artifact.Name);
        Assert.Equal("It never cools.", artifact.Lore);
    }
}

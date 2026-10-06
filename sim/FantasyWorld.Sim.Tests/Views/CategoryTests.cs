using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

public class CategoryTests
{
    private static WorldEvent Event(string type, params string[] tags) =>
        new EventLog().Append(default, new EventDraft(type, Tags: tags));

    [Fact]
    public void Events_FallIntoTheMostSpecificCategory()
    {
        Assert.Equal(Category.War, Categories.Of(Event("PeaceSigned", "Politics", "War")));
        Assert.Equal(Category.Structures, Categories.Of(Event("LabyrinthOpened", "Structures", "Magic")));
        Assert.Equal(Category.FaithAndMagic, Categories.Of(Event("HolyWarProclaimed", "Politics", "Faith")));
        Assert.Equal(Category.Politics, Categories.Of(Event("AllianceFormed", "Politics", "Diplomacy")));
        Assert.Equal(Category.Economy, Categories.Of(Event("RevoltBegan", "Settlements")));
        Assert.Equal(Category.Nature, Categories.Of(Event("Drought", "Regions")));
        Assert.Equal(Category.Characters, Categories.Of(Event("Rumour")));
        Assert.Equal("Faith & magic", Categories.Label(Category.FaithAndMagic));
        Assert.Equal("War ends", Categories.Kicker(Event("PeaceSigned")));
        Assert.Equal("Monsters slain", Categories.Kicker(Event("MonstersSlain")));
    }

    [Fact]
    public void NarratedText_LinksEveryNameItMentions()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var a = TestPeople.Add(world, 1, 30);
        var b = TestPeople.Add(world, 1, 30);
        var duel = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = a.Ref, ["defender"] = b.Ref },
            Place: world.Settlements[1].Ref, Payload: new Dictionary<string, string> { ["stakes"] = "to first blood" }));
        var text = new TemplateNarrator(world).Narrate(duel);

        var linked = LinkedText.Link(text, LinkedText.Mentions(duel), Names.Of(world));

        Assert.Equal(3, linked.Links.Count);
        foreach (var link in linked.Links)
            Assert.Equal(Names.Of(world)(link.Entity), text.Substring(link.Start, link.Length));
        Assert.Equal(new[] { a.Ref, b.Ref, world.Settlements[1].Ref }, linked.Links.Select(l => l.Entity));
    }

    [Fact]
    public void Links_PreferLongerNames_AndNeverOverlap()
    {
        var names = new Dictionary<EntityRef, string> { [new("house", 1)] = "Venn", [new("char", 2)] = "Maren of Venn" };
        var linked = LinkedText.Link("Maren of Venn rode to Venn.", names.Keys, e => names[e]);
        Assert.Equal(new[] { (0, 13, new EntityRef("char", 2)), (22, 4, new EntityRef("house", 1)) },
            linked.Links.Select(l => (l.Start, l.Length, l.Entity)));
    }
}

using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class CharacterScreenTests
{
    [Fact]
    public void TheCharacterPage_ShowsWhoTheyAre_TheirLife_AndTheirTies()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new FantasyWorld.Sim.Core.WorldDate(360 * 40);
        var log = new EventLog();
        var father = TestPeople.Add(world, 1, 60);
        var knight = TestPeople.Add(world, 1, 27, role: Role.Knight, fatherId: father.Id);
        knight.Traits.AddRange(new[] { Traits.Brave, Traits.Wrathful });
        knight.Ambition = Ambitions.Renown;
        var rival = TestPeople.Add(world, 1, 30);
        var born = log.Append(new FantasyWorld.Sim.Core.WorldDate(knight.BirthDay), new EventDraft("CharacterBorn",
            Actors: new Dictionary<string, EntityRef> { ["child"] = knight.Ref, ["father"] = father.Ref }, Place: world.Settlements[1].Ref, Importance: 20));
        var duel = log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = rival.Ref, ["defender"] = knight.Ref }, Place: world.Settlements[1].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = "to first blood" }, Importance: 40));
        log.Append(world.Date, new EventDraft("Gossip", Actors: new Dictionary<string, EntityRef> { ["subject"] = knight.Ref }, Importance: 5));
        knight.Memories.Add(new Memory(duel.Id, rival.Id, -90, 90));

        var page = CharacterScreen.Build(world, log, knight.Id, followed: true);

        Assert.Equal($"Knight of {world.Realms[1].Name} · age 27 · born Year {born.Date.Year} in {world.Settlements[1].Name}", page.Line);
        Assert.Equal(new[] { Traits.Brave, Traits.Wrathful }, page.Traits);
        Assert.Equal("Ambition: renown", page.Ambition);
        Assert.Equal($"Now at {world.Settlements[1].Name}", page.Location!.Label);
        Assert.Equal(new[] { born.Id, duel.Id }, page.Timeline.Select(r => r.EventId));
        Assert.True(page.Timeline.Single(r => r.EventId == duel.Id).Highlight);
        Assert.Equal(1, page.Folded);
        Assert.Contains(page.Ties, t => t.Relation == "Liege" && t.Ref == world.Characters[world.Settlements[1].LordId].Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Father" && t.Ref == father.Ref);
        Assert.Contains(page.Ties, t => t.Relation == "Rival" && t.Ref == rival.Ref && t.Tone == Tone.Hostile);
        Assert.True(page.Followed);

        var tree = FamilyTree.Build(world, knight.Id);
        Assert.Contains(tree.Edges, e => e.From == father.Ref && e.To == knight.Ref && e.Kind == "parent");
    }

    [Fact]
    public void EveryCharacterOfARealHistory_HasAPage()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var nameOf = Names.Of(world);
        foreach (var c in world.Characters.Values)
        {
            var page = CharacterScreen.Build(world, sim.Log, c.Id);
            Assert.All(page.Ties, t => Assert.NotNull(nameOf(t.Ref)));
            foreach (var row in page.Timeline)
                Assert.All(row.Text.Links, l => Assert.True(LinkedText.Reads(row.Text.Text.Substring(l.Start, l.Length), nameOf(l.Entity))));
            FamilyTree.Build(world, c.Id);
        }
    }

    [Fact]
    public void ACharactersPage_NamesTheirFaith_AndASaintIsCalledSo()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var holy = TestPeople.Add(world, 1, 60, role: Role.Priest);
        var page = CharacterScreen.Build(world, log, holy.Id);
        Assert.Equal(("The Way of Ohm", false), (page.Faith!.Name, page.Saint));
        holy.SaintEventId = log.Append(world.Date, new EventDraft("SaintDeclared")).Id;
        page = CharacterScreen.Build(world, log, holy.Id);
        Assert.True(page.Saint);
        Assert.StartsWith("Saint · ", page.Line);
    }
}

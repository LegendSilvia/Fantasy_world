using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Narration;

public class EnglishTests
{
    [Theory]
    [InlineData(0, "no")]
    [InlineData(7, "seven")]
    [InlineData(12, "twelve")]
    [InlineData(13, "13")]
    [InlineData(1428, "1,428")]
    public void Numbers_ReadAsWords_UpToTwelve(long n, string expected) => Assert.Equal(expected, English.Number(n));

    [Fact]
    public void Counts_Plurals_AndLists_ReadAsEnglish()
    {
        Assert.Equal(("one region", "six regions", "two cities", "three marches"),
            (English.Count(1, "region"), English.Count(6, "region"), English.Count(2, "city"), English.Count(3, "march")));
        Assert.Equal(("", "a", "a and b", "a, b and c"),
            (English.List(Array.Empty<string>()), English.List(new[] { "a" }), English.List(new[] { "a", "b" }), English.List(new[] { "a", "b", "c" })));
        Assert.Equal("six regions ceded and tribute", English.Terms("6 region(s) ceded; tribute"));
        Assert.Equal("the War of the Ash", English.InProse("The War of the Ash"));
        Assert.Equal("Wosmeim", English.InProse("Wosmeim"));
    }

    [Fact]
    public void RealmsTakeTheirArticle_AndDeathsAndWarsReadAsPhrases()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var (a, b) = (world.Realms[1], world.Realms[2]);
        var lord = world.Characters[world.Settlements[1].LordId];
        var war = new War { Id = world.Ids.Take(War.Kind), Name = "The War of the Ash", AttackerId = a.Id, DefenderId = b.Id };
        world.Wars[war.Id] = war;
        var narrator = new TemplateNarrator(world);
        WorldEvent Ev(string type, Dictionary<string, EntityRef> actors, Dictionary<string, string>? payload = null) =>
            log.Append(new WorldDate(0), new EventDraft(type, Actors: actors, Place: world.Settlements[1].Ref, Payload: payload));

        var declared = Ev("WarDeclared", new() { ["attacker"] = a.Ref, ["defender"] = b.Ref, ["war"] = war.Ref }, new() { ["casus belli"] = "border dispute" });
        Assert.Equal($"The {Statecraft.Title(a)} declared war on the {Statecraft.Title(b)} over a disputed border, and so began the War of the Ash.",
            narrator.Sentence(declared));
        var died = Ev("CharacterDied", new() { ["deceased"] = lord.Ref }, new() { ["cause"] = "battle", ["age"] = "9" });
        Assert.EndsWith(" fell in battle, aged 9.", narrator.Sentence(died));
        var infant = Ev("CharacterDied", new() { ["deceased"] = lord.Ref }, new() { ["cause"] = "illness", ["age"] = "0" });
        Assert.EndsWith(" died of an illness in infancy.", narrator.Sentence(infant));
        var old = Ev("CharacterDied", new() { ["deceased"] = lord.Ref }, new() { ["cause"] = "old age", ["age"] = "71" });
        Assert.Matches("(, aged| at the age of) 71\\.$", narrator.Sentence(old));
        var duel = Ev("DuelFought", new() { ["challenger"] = lord.Ref, ["defender"] = lord.Ref }, new() { ["stakes"] = "first blood" });
        Assert.Contains(" to first blood", narrator.Sentence(duel));
        var dreamer = Ev("ComingOfAge", new() { ["character"] = lord.Ref }, new() { ["ambition"] = "artifact" });
        Assert.Contains("a great treasure", narrator.Sentence(dreamer));
        var peace = Ev("PeaceSigned", new() { ["war"] = war.Ref, ["victor"] = a.Ref, ["vanquished"] = b.Ref }, new() { ["terms"] = "2 region(s) ceded; tribute", ["outcome"] = "attackers won" });
        Assert.Equal($"The War of the Ash ended in victory for the {Statecraft.Title(a)} over the {Statecraft.Title(b)}: two regions ceded and tribute.", narrator.Sentence(peace));
        var white = Ev("PeaceSigned", new() { ["war"] = war.Ref, ["victor"] = a.Ref, ["vanquished"] = b.Ref }, new() { ["terms"] = "status quo", ["outcome"] = "white peace" });
        Assert.Equal("The War of the Ash ended with neither side the victor.", narrator.Sentence(white));

        // Links still find a "The …" name that reads "the …" inside a sentence.
        var linked = LinkedText.Link(narrator.Sentence(declared), LinkedText.Mentions(declared), FantasyWorld.Sim.Query.Names.Of(world));
        Assert.Contains(linked.Links, l => l.Entity == war.Ref && linked.Text.Substring(l.Start, l.Length) == "the War of the Ash");
    }
}

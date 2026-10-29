using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

/// <summary>Characters changed by what befalls them, their abilities and talents, and what they are known for (the owner, 2026-10-29).</summary>
public class TransformTests
{
    private static readonly FantasyWorld.Sim.Engine.Simulator Aged =
        Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Medium), DateTimeOffset.UnixEpoch), 120);

    [Fact]
    public void Deeds_ChangeThoseTheyTouch_EachCausedByItsDeed_AtMostOnceInFiveYears()
    {
        var changes = Aged.Log.All.Where(e => e.Type == "CharacterTransformed").ToList();
        Assert.NotEmpty(changes);
        Assert.All(changes, e =>
        {
            var cause = Assert.Single(e.Causes);
            Assert.Contains(Aged.Log.Get(cause.EventId).Type, new[] { "SecretRevealed", "MonsterSlain", "MonsterHuntFailed", "DuelFought", "ChampionsClashed" });
            Assert.Contains(e.Payload["kind"], new[] { "awakening", "art", "vengeance", "desire" });
        });
        foreach (var who in changes.GroupBy(e => e.Actors["who"].Id))
            Assert.All(who.Zip(who.Skip(1)), p => Assert.True(p.Second.Date.Day - p.First.Date.Day >= 5 * 360));
        // Each change is told in its own words.
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(Aged.World);
        Assert.All(changes, e => Assert.DoesNotContain("{", narrator.Sentence(e)));
    }

    [Fact]
    public void AnAwakenedClass_StandsWhereItsBaseStood_AndFightsATenthStronger()
    {
        var k = Classes.Awaken(Classes.Mage, "Stormcaller");
        Assert.Equal("mage:Stormcaller", k);
        Assert.Equal(Classes.Mage, Classes.Base(k));
        Assert.Equal("Stormcaller", Classes.Title(k));
        Assert.True(Classes.Counters(k, Classes.Warrior));
        Assert.Equal(Classes.CounterEdge * Classes.AwakenedEdge, Classes.Edge(k, Classes.Warrior), 6);
        Assert.Equal(Classes.AwakenedEdge, Classes.Edge(k, Classes.Paladin), 6);
        Assert.Equal(1, Classes.Edge(Classes.Paladin, k), 6);
    }

    [Fact]
    public void EveryFighter_HasASignatureAbility_AndTalents_AndTheBestAreKnownForIt()
    {
        var world = Aged.World;
        var fighters = world.Characters.Values.Where(c => c.IsActive && c.Class.Length > 0).ToList();
        Assert.All(fighters, c => Assert.NotEmpty(Abilities.Of(world, c)));
        Assert.Equal(Abilities.Signature(world, fighters[0]), Abilities.Signature(world, fighters[0]));
        var strong = new Character { Id = 1, Name = "A", Attributes = { Prowess = 19, Intrigue = 5, Martial = 8, Lore = 4 } };
        Assert.Equal("strong", Talents.Of(strong)[0].Name);
        var known = Reputation.Of(world, Aged.Log);
        var archer = fighters.Where(c => Classes.Base(c.Class) == Classes.Ranger).ToList();
        if (archer.Count > 0) Assert.Contains(archer, c => known.Of(c.Id).Contains("the finest archer living"));
        // A fight's brief carries them all, for the arbiter.
        var brief = FantasyWorld.Sim.Llm.Briefs.Character(new FantasyWorld.Sim.Llm.StoryContext(world, Aged.Log,
            FantasyWorld.Sim.Llm.LlmGateway.Offline(new FantasyWorld.Sim.Llm.LlmJournal())), fighters[0]);
        Assert.Contains("abilities: " + Abilities.Signature(world, fighters[0]).Name, brief);
        Assert.Contains("Fights: ", brief);
    }
}

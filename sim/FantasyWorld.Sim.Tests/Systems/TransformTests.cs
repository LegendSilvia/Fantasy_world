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
    public void SomeAwakenings_SleepForYears_ThenWake_AsNewsOfTheirOwn_CausedByTheOldDeed()
    {
        var woken = Aged.Log.All.Where(e => e.Type == "CharacterTransformed" && e.Payload.ContainsKey("slept")).ToList();
        Assert.NotEmpty(woken);
        Assert.All(woken, e =>
        {
            Assert.Equal("awakening", e.Payload["kind"]);
            var deed = Aged.Log.Get(Assert.Single(e.Causes).EventId);
            Assert.InRange(e.Date.Day - deed.Date.Day, 360, 5 * 360 + 31);
            Assert.StartsWith("What had slept within", new FantasyWorld.Sim.Narration.TemplateNarrator(Aged.World).Sentence(e));
        });
    }

    [Fact]
    public void EveryAwakenedPower_IsInTheLore_WithItsOrigin_WhatItDoes_AndItsCondition()
    {
        var world = Aged.World;
        Assert.NotEmpty(world.Callings);
        Assert.All(world.Callings, k =>
        {
            Assert.True(k.Origin.Length > 0 && k.Does.Length > 0 && k.Condition.Length > 0);
            Assert.Contains(k.Base, Classes.All);
            Assert.Equal("CharacterTransformed", Aged.Log.Get(k.EventId).Type);
        });
        Assert.Equal(world.Callings.Count, world.Callings.Select(k => k.Name).Distinct().Count());
        Assert.All(world.Characters.Values.Where(c => Classes.IsAwakened(c.Class)),
            c => Assert.Contains(world.Callings, k => Classes.Awaken(k.Base, k.Name) == c.Class));
    }

    [Fact]
    public void APowerBreakingOutMidFight_TurnsTheOdds_AndALoserMoreOftenEscapes()
    {
        var world = FantasyWorld.Sim.Tests.Support.TestPeople.World();
        var a = FantasyWorld.Sim.Tests.Support.TestPeople.Add(world, 1, 30);
        var d = FantasyWorld.Sim.Tests.Support.TestPeople.Add(world, 1, 30);
        a.Class = d.Class = Classes.Warrior;
        var plain = FantasyWorld.Sim.Agency.Encounters.DuelBetween(world, a, d, toTheDeath: true);
        var surged = FantasyWorld.Sim.Agency.Encounters.DuelBetween(world, a, d, toTheDeath: true, surged: d.Id);
        Assert.Equal(d.Id, surged.Surged);
        double Share(FantasyWorld.Sim.Agency.Encounter e, string key) => e.Outcomes.Single(o => o.Key == key).Probability;
        Assert.True(Share(surged, "attacker_escaped") > Share(plain, "attacker_escaped"));
        Assert.True(Share(surged, "attacker_kills") < Share(plain, "attacker_kills"));
        Assert.Equal(1, surged.Outcomes.Sum(o => o.Probability), 6);
    }

    [Fact]
    public void Companions_ComeToCharacters_BeastsWithTheirLore_SwornOnesBothWays()
    {
        var world = Aged.World;
        var joined = Aged.Log.All.Where(e => e.Type == "CompanionJoined").ToList();
        Assert.Contains(joined, e => e.Payload["kind"] == Companion.Beast);
        Assert.Contains(joined, e => e.Payload["kind"] == Companion.Sworn);
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(world);
        Assert.All(joined, e => Assert.DoesNotContain("{", narrator.Sentence(e)));
        foreach (var c in world.Characters.Values.Where(c => c.IsActive && c.Companion is not null))
            if (c.Companion!.Kind == Companion.Beast) Assert.True(c.Companion.Name.Length > 0 && c.Companion.Lore.Length > 0);
            else if (world.Characters.TryGetValue(c.Companion.CharacterId, out var friend) && friend.IsActive)
                Assert.Equal(c.Id, friend.Companion?.CharacterId);
        var withBeast = world.Characters.Values.First(c => c.IsActive && c.Companion is { Kind: Companion.Beast });
        Assert.Contains("at their side", Arms.Brief(world, withBeast));
    }

    [Fact]
    public void AFighterWields_TheirFamousWeaponRelic_ElseTheirCallingsOwn()
    {
        var world = Aged.World;
        var holder = world.Characters.Values.FirstOrDefault(c => c.IsActive && world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id && a.Type == ArtifactType.Weapon));
        if (holder is not null) Assert.True(Arms.Weapon(world, holder).ArtifactId != 0);
        var plain = world.Characters.Values.First(c => c.IsActive && c.Class.Length > 0 && !world.Artifacts.Values.Any(a => a.HolderId == c.Id && a.Type == ArtifactType.Weapon && a.Place == ArtifactPlace.Held));
        Assert.Equal(0, Arms.Weapon(world, plain).ArtifactId);
        Assert.StartsWith("wields ", Arms.Brief(world, plain));
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

    [Fact]
    public void AOneSidedFight_IsToldByTheStrongersAura_NotATwist()
    {
        Assert.Contains("one-sided: the attacker is far the stronger", FantasyWorld.Sim.Llm.FightAnswer.Instructions(10, 0.85));
        Assert.Contains("one-sided: the defender is far the stronger", FantasyWorld.Sim.Llm.FightAnswer.Instructions(10, 0.2));
        Assert.DoesNotContain("one-sided", FantasyWorld.Sim.Llm.FightAnswer.Instructions(10, 0.6));
        Assert.Contains("within a single day", FantasyWorld.Sim.Llm.FightAnswer.Instructions(10, 0.85));

        // The far stronger side winning ends it within a day; an upset, or an even fight, may last longer.
        using var three = System.Text.Json.JsonDocument.Parse("""{"days": 3}""");
        using var one = System.Text.Json.JsonDocument.Parse("""{"days": 1}""");
        Assert.NotNull(FantasyWorld.Sim.Llm.FightAnswer.Swift(three.RootElement, 0.85, attackerWins: true));
        Assert.NotNull(FantasyWorld.Sim.Llm.FightAnswer.Swift(three.RootElement, 0.15, attackerWins: false));
        Assert.Null(FantasyWorld.Sim.Llm.FightAnswer.Swift(one.RootElement, 0.85, attackerWins: true));
        Assert.Null(FantasyWorld.Sim.Llm.FightAnswer.Swift(three.RootElement, 0.85, attackerWins: false));
        Assert.Null(FantasyWorld.Sim.Llm.FightAnswer.Swift(three.RootElement, 0.6, attackerWins: true));
    }
}

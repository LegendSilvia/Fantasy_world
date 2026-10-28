using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class RaceTests
{
    private static readonly Lazy<World> Medium = new(() => WorldGenerator.Generate(new WorldGenOptions(5), DateTimeOffset.UnixEpoch));

    private sealed class Namer : IInventor
    {
        public Invention Invent(InventionBrief brief) => new($"The {brief.Fallback}folk", $"Lore of {brief.Subject}.");
    }

    [Fact]
    public void AWorld_HasOriginalRaces_EachWithAPeople_AndOldBonds()
    {
        var world = Medium.Value;
        Assert.Equal(RaceGenerator.CountFor(world.Regions.Count), world.Races.Count);
        Assert.All(world.Cultures.Values, c => Assert.Contains(c.RaceId, world.Races.Keys));
        Assert.All(world.Races.Values, r =>
        {
            Assert.Contains(world.Cultures.Values, c => c.RaceId == r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.Looks));
            Assert.StartsWith($"The {r.Name} are ", r.Nature);
            Assert.Contains(r.Temper, Race.Tempers);
            Assert.NotEmpty(r.Gifts);
        });
        Assert.Contains(world.Races.Values, r => r.Foes.Count > 0);
        Assert.All(world.Races.Values, r => Assert.All(r.Foes, f => Assert.Contains(r.Id, world.Races[f].Foes)));
        Assert.All(world.Characters.Values, c => Assert.NotNull(Peoples.Of(world, c)));
    }

    [Fact]
    public void ClaudeNamesTheRaces_AndTheirNature_KeepingTheRulesWordsWithoutAnAnswer()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5), DateTimeOffset.UnixEpoch);
        var old = world.Races.Values.Select(r => r.Name).ToList();
        RaceGenerator.Invent(world, new Namer());
        Assert.Equal(old.Select(n => $"The {n}folk"), world.Races.Values.Select(r => r.Name));
        Assert.All(world.Races.Values, r => Assert.Equal($"Lore of {r.Ref}.", r.Nature));

        var rules = WorldGenerator.Generate(new WorldGenOptions(5), DateTimeOffset.UnixEpoch);
        RaceGenerator.Invent(rules, new RulesInventor());
        Assert.Equal(old, rules.Races.Values.Select(r => r.Name));
    }

    [Fact]
    public void ALongLivedPeople_AgesSlowerOnceGrown()
    {
        Assert.Equal(10, LifeSystem.Aged(10, 4));
        Assert.Equal(40, LifeSystem.Aged(40, 1));
        Assert.Equal(Character.AdultAge + 30, LifeSystem.Aged(Character.AdultAge + 120, 4));
        Assert.True(LifeSystem.BaseMortality(LifeSystem.Aged(90, 2.5)) < LifeSystem.BaseMortality(90));
    }

    [Fact]
    public void OldFoes_LikeEachOtherLess_AndMayWarOverTheirHatred()
    {
        var world = TestRealms.TwoKingdoms();
        var (a, b) = (new Race { Id = 1, Name = "Vel" }, new Race { Id = 2, Name = "Orm" });
        world.Races[1] = a;
        world.Races[2] = b;
        var culture = world.Cultures.Values.First();
        culture.RaceId = 1;
        var other = new Culture { Id = 99, Name = "Ormfolk", RaceId = 2, FaithId = culture.FaithId };
        world.Cultures[99] = other;
        world.Realms[2].CultureId = 99;
        world.Settlements[world.Realms[2].CapitalId].LordId = 0; // without a ruler, a realm is its culture's race
        Assert.Equal(2, Peoples.Of(world, world.Realms[2])!.Id);
        Assert.Equal(1, Peoples.Of(world, world.Realms[1])!.Id);
        var neutral = DiplomacySystem.Opinion(world, world.Realms[1], world.Realms[2]);
        a.Foes.Add(2);
        b.Foes.Add(1);
        Assert.Equal(-1, Peoples.Bond(a, b));
        Assert.Equal(neutral - 20, DiplomacySystem.Opinion(world, world.Realms[1], world.Realms[2]));
    }
}

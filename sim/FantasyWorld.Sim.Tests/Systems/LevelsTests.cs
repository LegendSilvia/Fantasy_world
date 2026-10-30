using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

/// <summary>Levels for characters, beasts, labyrinths and towns, and mercenaries (the owner, 2026-10-30).</summary>
public class LevelsTests
{
    private static Simulator History(int years, int seed = 11)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions((ulong)seed, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * years);
        return sim;
    }

    [Fact]
    public void Levels_RunOneToTen_AndCostFarMore_PastTheSoftCap()
    {
        Assert.Equal(1, Levels.Of(0));
        Assert.Equal(Levels.Max, Levels.Of(1_000_000));
        var steps = Enumerable.Range(2, Levels.Max - 1).Select(l => Levels.NeededFor(l) - Levels.NeededFor(l - 1)).ToList();
        Assert.All(steps.Skip(Levels.SoftCap - 1), past => Assert.True(past > steps[Levels.SoftCap - 2] * 2));
        Assert.True(Levels.Power(10) > Levels.Power(5) && Levels.Power(5) > Levels.Power(1));
    }

    [Fact]
    public void ACenturyOfLives_MostlyEndsAtFourOrFive_AndFewPastIt()
    {
        var sim = History(120);
        var lived = sim.World.Characters.Values.Where(c => c.Died is not null || c.Age(sim.World.Date) >= Character.ElderAge).Select(Levels.Of).OrderBy(l => l).ToList();
        Assert.InRange(lived[lived.Count / 2], 3, 5);
        Assert.True(lived.Count(l => l > Levels.SoftCap) < lived.Count / 5);
        Assert.All(lived, l => Assert.InRange(l, 1, Levels.Max));
        Assert.All(sim.World.Monsters.Values, m => Assert.InRange(Levels.Of(m), 1, m.Elder ? Levels.Max : Levels.Max - 1));
        Assert.All(sim.World.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth), s => Assert.InRange(Levels.Required(s), 2, 5));
        Assert.All(sim.World.Settlements.Values, s => Assert.InRange(Levels.Of(sim.World, s), 1, Levels.TownMax));
    }

    [Fact]
    public void AWorldMadeBeforeLevels_IsLevelledFromItsHistory_Once()
    {
        var sim = History(40);
        var world = sim.World;
        foreach (var c in world.Characters.Values) (c.Level, c.Experience) = (0, 0);
        world.Levelled = false;
        Assert.True(Levels.Backfill(world, sim.Log) > 0);
        Assert.True(world.Levelled);
        Assert.All(world.Characters.Values, c => Assert.InRange(c.Level, 1, Levels.Max));
        // Deeds count: the one with the most deeds in the chronicle stands above the plain folk of their age.
        var doer = world.Characters.Values.OrderByDescending(c => sim.Log.Involving(c.Ref).Sum(e => e.Actors.Where(a => a.Value == c.Ref).Sum(a => Levels.For(e, a.Key)))).First();
        Assert.True(doer.Level >= 2);
        var before = world.Characters.Values.ToDictionary(c => c.Id, c => c.Level);
        Assert.Equal(0, Levels.Backfill(world, sim.Log));
        Assert.Equal(before, world.Characters.Values.ToDictionary(c => c.Id, c => c.Level));
    }

    [Fact]
    public void AnOldSavedWorld_LoadsLevelled()
    {
        var sim = History(10);
        sim.World.Levelled = false;
        foreach (var c in sim.World.Characters.Values) c.Level = 0;
        using var file = new TempFile();
        using (var store = WorldStore.Open(file.Path)) store.Save(sim.World, sim.Log, sim.Queue);
        using var reopened = WorldStore.Open(file.Path);
        var (world, _, _) = reopened.Load();
        Assert.True(world.Levelled);
        Assert.All(world.Characters.Values, c => Assert.True(c.Level >= 1));
    }

    [Fact]
    public void ATownsLevel_IsItsFortification_AtMostFive()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var town = world.Settlements.Values.First();
        town.Walls = 0;
        Assert.Equal(1, Levels.Of(world, town));
        town.Walls = 3;
        Assert.Equal(4, Levels.Of(world, town));
        world.Structures[999] = new Structure { Id = 999, Type = StructureKind.Fortification, State = StructureState.Standing, SettlementId = town.Id };
        Assert.Equal(Levels.TownMax, Levels.Of(world, town));
        Assert.True(Levels.Ward(5) < Levels.Ward(1));
        Assert.Equal(new[] { 0, 1, 2, 2, 3 }, Enumerable.Range(1, 5).Select(MilitarySystem.MercenariesFor));
    }

    [Fact]
    public void Mercenaries_ServeOnlyARealmTheyDoNotDislike_AndNoMoreThanThree()
    {
        var sim = History(120);
        var world = sim.World;
        var hired = sim.Log.All.Where(e => e.Type == "MercenaryHired").ToList();
        Assert.NotEmpty(hired);
        Assert.All(world.Armies.Values, a => Assert.True(a.Mercenaries.Count <= MilitarySystem.MostMercenaries));
        // A subject of a realm at war with another is its enemy: never at least neutral, so never its mercenary.
        foreach (var war in world.Wars.Values.Where(w => !w.Ended))
            foreach (var subject in world.ActiveCharacters.Where(c => world.Settlements.TryGetValue(c.SettlementId, out var s)
                                                                   && Realms.SovereignOf(world, s)?.Id == war.Attackers[0]))
                Assert.All(war.Defenders, d => Assert.Equal(-100, MilitarySystem.Toward(world, subject, world.Realms[d])));
        // Each hire went to someone the realm could count on at the time: a realm's own subject feels at least a little for it.
        Assert.All(world.ActiveCharacters.Where(c => c.Role != Role.Lord), c =>
            Assert.InRange(MilitarySystem.Toward(world, c, world.Realms.Values.First()), -100, 100));
    }

    [Fact]
    public void TheLower_AvoidTheHigher_AndSlipAwayWhenTheHigherComeForThem()
    {
        // Picking a fight: whole at one's own level or below, half for each level the other stands above.
        Assert.Equal(1, Levels.Wariness(5, 3));
        Assert.Equal(1, Levels.Wariness(4, 4));
        Assert.Equal(0.25, Levels.Wariness(3, 5));
        // Being sought out by a higher level: a fifth more chance to slip away for each level between, at most four in five.
        Assert.Equal(0, Levels.EvadeChance(6, 4));
        Assert.Equal(40, Levels.EvadeChance(3, 5));
        Assert.Equal(80, Levels.EvadeChance(1, 9));

        // They only know the other stands higher when the other's name goes before them.
        var low = new Character { Id = 1, Name = "Low", Level = 2 };
        var high = new Character { Id = 2, Name = "High", Level = 6, Renown = 10 };
        Assert.Equal(1, Levels.Wariness(low, high));
        Assert.Equal(2, Levels.Seen(low, high));
        high.Renown = Levels.KnownAt;
        Assert.Equal(6, Levels.Seen(low, high));
        Assert.Equal(Math.Pow(0.5, 4), Levels.Wariness(low, high));

        var sim = History(120);
        var avoided = sim.Log.All.Where(e => e.Type == "FightAvoided").ToList();
        Assert.NotEmpty(avoided);
        Assert.All(avoided, e => Assert.True(int.Parse(e.Payload["avoider level"]) < int.Parse(e.Payload["seeker level"])));
    }

    [Fact]
    public void MastersTakeApprentices_NameSuccessors_PassTheirMantle_AndSometimesFallOut()
    {
        var sim = History(150);
        var world = sim.World;
        EntityRef Of(WorldEvent e, string role) => e.Actors[role];
        var taken = sim.Log.All.Where(e => e.Type == "ApprenticeTaken").ToList();
        Assert.NotEmpty(taken);
        Assert.All(taken, e => Assert.True(Levels.Of(world.Characters[Of(e, "apprentice").Id]) >= 1));
        // Each apprentice has one master, and none teaches more than two at once.
        Assert.Equal(taken.Count, taken.Select(e => Of(e, "apprentice")).Distinct().Count());
        Assert.All(world.ActiveCharacters, m => Assert.True(world.ActiveCharacters.Count(a => a.MasterId == m.Id && !a.Estranged) <= MentorSystem.MostApprentices));
        // A named successor is the master's own apprentice, still loyal.
        Assert.All(world.ActiveCharacters.Where(m => m.SuccessorId != 0), m =>
            Assert.True(world.Characters[m.SuccessorId] is var s && s.MasterId == m.Id && !s.Estranged));
        Assert.Contains(sim.Log.All, e => e.Type == "SuccessorNamed");
        Assert.Contains(sim.Log.All, e => e.Type == "MantleInherited");
        // A falling-out leaves them enemies, whatever it was over.
        var fell = sim.Log.All.Where(e => e.Type == "FellOutWithMaster").ToList();
        Assert.NotEmpty(fell);
        Assert.All(fell, e => Assert.Contains(e.Payload["reason"], new[] { "hatred", "outgrown", "war" }));
        Assert.All(fell, e => Assert.True(world.Characters[Of(e, "apprentice").Id].Estranged));
        Assert.All(fell.Select(e => world.Characters[Of(e, "apprentice").Id]).Where(a => a.IsActive && world.Characters[a.MasterId].IsActive),
            a => Assert.False(Relations.Bound(a, world.Characters[a.MasterId])));
    }

    [Fact]
    public void CompanionsInFights_CanBeHurt_LaidUp_Lamed_Parted_OrKilled()
    {
        var sim = History(120);
        var fates = sim.Log.All.Where(e => e.Type == "FellowFate").ToList();
        Assert.NotEmpty(fates);
        Assert.All(fates, e => Assert.Contains(e.Payload["fate"], new[] { "injured", "incapacitated", "disabled", "separated", "dead" }));
        Assert.True(fates.Select(e => e.Payload["fate"]).Distinct().Count() >= 3);
        // A beast dead or parted is no longer at its keeper's side.
        foreach (var e in fates.Where(e => e.Payload["fate"] is "dead" or "separated" && !e.Actors.ContainsKey("fellow")))
            Assert.NotEqual(e.Payload["name"], sim.World.Characters[e.Actors["who"].Id].Companion?.Name);
        // Rolled by the rules, the losing side fares worse than the winning.
        var f = new FantasyWorld.Sim.Agency.Fellow("a1", true, 1, 0, "Grey", "a wolf");
        int Harmed(bool won) => Enumerable.Range(0, 2000).Count(m => FantasyWorld.Sim.Agency.Fellows.Roll(f, won, true, m) != FantasyWorld.Sim.Agency.FellowFate.Unharmed);
        Assert.True(Harmed(false) > Harmed(true) * 2);
    }
}

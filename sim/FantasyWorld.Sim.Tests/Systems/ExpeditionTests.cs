using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

/// <summary>Expeditions, the great labyrinth, its outbreaks and the elder dragon (the owner, 2026-10-30).</summary>
public class ExpeditionTests
{
    private static readonly Lazy<Simulator> Age = new(() =>
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants: true);
        sim.AdvanceDays(360 * 150);
        return sim;
    });

    private static IEnumerable<EntityRef> Company(WorldEvent e) =>
        e.Actors.Where(a => a.Key == "leader" || a.Key.StartsWith("companion ", StringComparison.Ordinal)).Select(a => a.Value);

    [Fact]
    public void ALabyrinth_IsDelvedByACompany_OverDays_AndOnlyByThoseOfItsLevel()
    {
        var sim = Age.Value;
        var begun = sim.Log.All.Where(e => e.Type == "ExpeditionBegun").ToList();
        Assert.NotEmpty(begun);
        Assert.Contains(begun, e => Company(e).Count() > 1);
        Assert.All(begun, e => Assert.InRange(Company(e).Count(), 1, DeedsSystem.MostInCompany));
        // Every member was of a level the labyrinth allows when they set out.
        foreach (var e in begun.Where(e => e.Actors.ContainsKey("structure")))
        {
            var maze = sim.World.Structures[e.Actors["structure"].Id];
            Assert.All(e.Payload["levels"].Split(',').Select(int.Parse), level => Assert.True(level >= Levels.Required(maze) - 1));
        }
        // Each level is a step of days, told with the company that went down.
        var delved = sim.Log.All.Where(e => e.Type == "LabyrinthDelved").ToList();
        Assert.NotEmpty(delved);
        Assert.All(delved, e => Assert.Contains(e.Causes, c => sim.Log.TryGet(c.EventId, out var b) && b.Type == "ExpeditionBegun"));
        Assert.Contains(sim.Log.All.Where(e => e.Type == "ExpeditionEnded"), e => int.Parse(e.Payload["days"]) > 1);
    }

    [Fact]
    public void TheGreatLabyrinth_TakesCompaniesOfThree_ManyExpeditions_AndYears()
    {
        var sim = Age.Value;
        var great = Assert.Single(sim.World.Structures.Values, s => s.Great);
        Assert.Equal(GreatEventsSystem.GreatLevels, great.Levels);
        Assert.Equal(GreatEventsSystem.GreatRequiredLevel, Levels.Required(great));
        var against = sim.Log.All.Where(e => e.Type == "ExpeditionBegun" && e.Actors.GetValueOrDefault("structure") == great.Ref).ToList();
        Assert.True(against.Count > 1);
        Assert.All(against, e => Assert.True(Company(e).Count() >= DeedsSystem.FewestForAWorldEvent));
        Assert.Contains(sim.Log.All, e => e.Type == "GreatOutbreak");
        if (great.ClearedEventId != 0)
            Assert.True(sim.Log.Get(great.ClearedEventId).Date.Year - sim.Log.Get(great.StartedEventId).Date.Year >= 2);
        // An outbreak ends only with its beasts, or the labyrinth.
        foreach (var end in sim.Log.All.Where(e => e.Type == "GreatOutbreakEnded"))
        {
            var outbreak = sim.Log.All.Last(e => e.Type == "GreatOutbreak" && e.Id < end.Id);
            Assert.True(great.ClearedEventId is var c && c != 0 && c < end.Id
                        || sim.World.Monsters.Values.Where(m => m.RoseEventId == outbreak.Id).All(m => !m.Alive));
        }
    }

    [Fact]
    public void AnElderDragon_IsLevelTen_AndFallsOnlyToACompany_WornDownOverHunts()
    {
        var sim = Age.Value;
        var dragons = sim.World.Monsters.Values.Where(m => m.Elder).ToList();
        Assert.NotEmpty(dragons);
        Assert.All(dragons, d => Assert.Equal(Levels.Max, Levels.Of(d)));
        foreach (var slain in sim.Log.All.Where(e => e.Type == "ElderDragonSlain"))
        {
            Assert.True(Company(slain).Count() >= DeedsSystem.FewestForAWorldEvent);
            Assert.Contains(sim.Log.All, e => e.Type == "DragonWounded" && e.Actors["monster"] == slain.Actors["monster"] && e.Id < slain.Id);
        }
        // No one hunts an elder dragon alone.
        Assert.DoesNotContain(sim.Log.All, e => e.Type is "MonsterSlain" or "MonsterHuntFailed" && dragons.Any(d => e.Actors.GetValueOrDefault("monster") == d.Ref));
    }

    [Fact]
    public void TheWorldEventsTab_TellsTheGreatLabyrinthAndTheDragon()
    {
        var sim = Age.Value;
        var screen = WorldEventsScreen.Build(sim.World, sim.Log);
        Assert.Contains(screen.Great!, g => g.Kind == "great labyrinth" && g.Events.Count > 1);
        Assert.Contains(screen.Great!, g => g.Kind == "elder dragon" && g.Level == Levels.Max);
    }
}

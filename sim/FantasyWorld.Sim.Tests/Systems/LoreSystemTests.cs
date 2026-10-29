using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class LoreSystemTests
{
    [Fact]
    public void EveryQuarterCentury_TheSongsKeepTheAgesGreatDeeds_AndBeforeTheChronicleAirs_ForgetOldOnes()
    {
        var sim = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Medium), DateTimeOffset.UnixEpoch), 120);
        var world = sim.World;
        Assert.NotEmpty(world.Tales);
        Assert.All(world.Tales, t => Assert.Equal(0, t.SungFrom % LoreSystem.RevisionYears));
        Assert.All(world.Tales.GroupBy(t => t.SungFrom), g => Assert.InRange(g.Count(), 1, LoreSystem.TalesPerAge));
        Assert.Contains(sim.Log.All, e => e.Type == "LoreRevised");
        Assert.All(world.Tales.Where(t => t.Forgotten), t => Assert.True(t.ForgottenIn - t.Year >= LoreSystem.ForgetAfterYears));
        Assert.Equal(world.Tales.Count, world.Tales.Select(t => t.EventId).Distinct().Count());
    }

    [Fact]
    public void OnceTheChronicleAirs_NothingSungIsForgotten()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        world.LastBroadcastDate = "2026-10-29";
        var sim = Prehistory.Run(world, 120);
        Assert.NotEmpty(sim.World.Tales);
        Assert.DoesNotContain(sim.World.Tales, t => t.Forgotten);
    }

    [Fact]
    public void AForgottenTale_IsWhatTheBottomOfALabyrinthGivesBackFirst()
    {
        var world = TestPeople.World();
        world.Tales.Add(new Tale("The Song of Year 3", "They sang of a king who drowned.", 3, 1, 25, Forgotten: true, ForgottenIn: 75));
        Assert.Equal("The Song of Year 3", LoreSystem.Buried(world)!.Title);
        world.Tales[0] = world.Tales[0] with { Uncovered = true };
        Assert.Null(LoreSystem.Buried(world));
    }
}

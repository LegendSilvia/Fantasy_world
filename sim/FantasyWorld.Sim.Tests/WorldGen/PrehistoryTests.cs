using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class PrehistoryTests
{
    [Fact]
    public void Prehistory_GivesANewWorldAPast_WithoutTouchingTheRealClock()
    {
        var now = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
        var world = WorldGenerator.Generate(new WorldGenOptions(9, WorldSize.Tiny), now);
        var sim = Prehistory.Run(world, 15, checkInvariants: true);
        Assert.Equal(16, world.Date.Year);
        Assert.Contains(sim.Log.All, e => e.Type == "CharacterBorn");
        Assert.Contains(sim.Log.All, e => e.Type == "CharacterDied");
        Assert.Equal(now, world.LastRealUtc);
        Assert.Equal(0, world.LastSeenEventId);
        Assert.Throws<ArgumentOutOfRangeException>(() => Prehistory.Run(world, -1));
    }

    [Fact]
    public void New_RunsPrehistory_AndTheFirstDigestTellsTheStorySoFar()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        var t0 = DateTimeOffset.UnixEpoch;
        Assert.Equal(0, CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny", "--prehistory", "10" }, output, () => t0));
        Assert.Contains("10 years of prehistory", output.ToString());
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, log, _) = store.Load();
            Assert.Equal(11, world.Date.Year);
            Assert.NotEqual(0, log.Count);
        }
        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path }, output, () => t0);
        Assert.Contains("The chronicle remembers:", output.ToString());
        Assert.Equal(1, CliApp.Run(new[] { "new", file.Path + ".x", "--prehistory", "-5" }, new StringWriter(), () => t0));
        Assert.Equal(0, Prehistory.DefaultYearsFor(WorldSize.Tiny));
        Assert.Equal(Prehistory.DefaultYears, Prehistory.DefaultYearsFor(WorldSize.Medium));
    }
}

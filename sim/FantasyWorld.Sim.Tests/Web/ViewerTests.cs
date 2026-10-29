using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Client;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Web;

namespace FantasyWorld.Sim.Tests.Web;

/// <summary>The browser's answers come from the same views and geometry as the game's (sub-project 11 W2).</summary>
public class ViewerTests
{
    private static byte[] Bundle()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "20y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        using var store = WorldStore.Open(file.Path);
        return WebBundle.Of(store).ToGzip();
    }

    [Fact]
    public void TheViewer_AnswersTheGamesScreens_AndGeometry()
    {
        var bytes = Bundle();
        var (world, log) = WebBundle.FromGzip(bytes).Load();
        using var loaded = JsonDocument.Parse(Viewer.Load(bytes));
        Assert.Equal(world.Date.Year, loaded.RootElement.GetProperty("year").GetInt32());

        // Screens: the very JSON the game's views make, today and in a past year.
        Assert.Equal(ViewJson.Serialize(MapScreen.Build(world, log)), Viewer.Screen("map", ""));
        // No year before the chronicle's start (Year 100, or a younger world's own year) is shown: an earlier one is the first.
        var first = loaded.RootElement.GetProperty("firstYear").GetInt32();
        Assert.Equal(Math.Min(Viewer.ChronicleYear, world.Date.Year), first);
        Assert.Equal(ViewJson.Serialize(MapScreen.Build(world, log, new FantasyWorld.Sim.Core.WorldDate((long)Math.Max(5, first) * FantasyWorld.Sim.Core.WorldDate.DaysPerYear - 1))),
            Viewer.Screen("map", "5"));
        var realm = world.Realms.Keys.First();
        Assert.Equal(ViewJson.Serialize(RealmScreen.Build(world, log, realm)), Viewer.Screen("realm", realm.ToString()));
        Assert.Throws<ArgumentException>(() => Viewer.Screen("nothing", ""));

        // Geometry: the block's top packed as the page reads it.
        var top = BlockMesh.Top(world.Geography!.Terrain, 64);
        var packed = Viewer.Block("top", 64);
        Assert.Equal((top.Positions.Length, top.Indices.Length), (BitConverter.ToInt32(packed, 0), BitConverter.ToInt32(packed, 4)));
        Assert.Equal(12 + top.Positions.Length * 36 + top.Indices.Length * 4, packed.Length);
        Assert.Equal(top.Positions[1].X, BitConverter.ToSingle(packed, 12 + 12));
        Assert.Equal(0, Viewer.Trees().Length % 8);

        // The scene: houses in nines, each realm's standard and a line per border.
        using var scene = JsonDocument.Parse(Viewer.Scene(""));
        Assert.Equal(0, scene.RootElement.GetProperty("houses").GetArrayLength() % 9);
        Assert.Equal(world.Realms.Values.Count(r => !r.Destroyed && r.LiegeId == 0), scene.RootElement.GetProperty("realms").GetArrayLength());
        Assert.NotEqual(0, scene.RootElement.GetProperty("borders").GetArrayLength());
    }
}

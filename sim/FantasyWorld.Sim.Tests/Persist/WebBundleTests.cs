using System.Globalization;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Persist;

public class WebBundleTests
{
    [Fact]
    public void AWorldLoadedFromItsWebBundle_ShowsTheSameScreens_AndCarriesNoLlmJournal()
    {
        using var file = new TempFile();
        using var bundleFile = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "20y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "export-web", file.Path, bundleFile.Path }, output, () => DateTimeOffset.UnixEpoch));
        Assert.StartsWith($"Web bundle written to {bundleFile.Path}", output.ToString());

        using var store = WorldStore.Open(file.Path);
        var (world, log, _) = store.Load();
        var bytes = File.ReadAllBytes(bundleFile.Path);
        var (fromWeb, fromWebLog) = WebBundle.FromGzip(bytes).Load();

        Assert.Equal(ViewJson.Serialize(MapScreen.Build(world, log)), ViewJson.Serialize(MapScreen.Build(fromWeb, fromWebLog)));
        Assert.Equal(ViewJson.Serialize(MapScreen.Build(world, log, new FantasyWorld.Sim.Core.WorldDate(360 * 5))),
            ViewJson.Serialize(MapScreen.Build(fromWeb, fromWebLog, new FantasyWorld.Sim.Core.WorldDate(360 * 5))));
        var lord = world.Settlements.Values.First(s => s.LordId != 0).LordId;
        Assert.Equal(ViewJson.Serialize(CharacterScreen.Build(world, log, lord)), ViewJson.Serialize(CharacterScreen.Build(fromWeb, fromWebLog, lord)));
        var caused = log.All.Last(e => e.Causes.Count > 0).Id;
        Assert.Equal(ViewJson.Serialize(WhyScreen.Build(world, log, caused)), ViewJson.Serialize(WhyScreen.Build(fromWeb, fromWebLog, caused)));
        var realm = world.Realms.Keys.First();
        Assert.Equal(ViewJson.Serialize(RealmScreen.Build(world, log, realm)), ViewJson.Serialize(RealmScreen.Build(fromWeb, fromWebLog, realm)));
        Assert.Equal(world.Geography!.Terrain.Biomes, fromWeb.Geography!.Terrain.Biomes);

        // Only the world is published: no LLM journal, settings or provider names.
        var json = new StreamReader(new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress)).ReadToEnd();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(new[] { "Version", "Snapshot", "Geography", "Events" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("claude-", json); // no model name (base64 data never holds a hyphen)
        Assert.True(bytes.Length < 2_000_000, $"{bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes");
    }
}

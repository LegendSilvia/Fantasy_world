using System.Runtime.CompilerServices;
using System.Text.Json;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Views;

/// <summary>
/// The JSON contract (sub-project 2 §4, §6): each screen of a fixed tiny world after 20 years is pinned in a golden
/// file. Simulation changes alter them on purpose; regenerate with UPDATE_GOLDEN=1 and review the diff.
/// </summary>
public class ViewJsonTests
{
    private static string GoldenDir([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden");

    private static string Run(string path, params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "view", path }.Concat(args).ToArray(), output, () => DateTimeOffset.UnixEpoch));
        return output.ToString();
    }

    private static void Golden(string name, string json)
    {
        var file = Path.Combine(GoldenDir(), name + ".json");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1") File.WriteAllText(file, json);
        Assert.True(File.Exists(file), $"Missing golden file {file}; run with UPDATE_GOLDEN=1.");
        Assert.Equal(File.ReadAllText(file), json);
    }

    [Fact]
    public void Screens_MatchTheirGoldenJson()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "20y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        long duel, lord;
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, log, _) = store.Load();
            duel = log.All.Last(e => e.Type == "DuelFought" || e.Type == "CharacterDied" && e.Causes.Count > 0).Id;
            lord = world.Settlements.Values.First(s => s.LordId != 0).LordId;
        }

        Golden("digest", Run(file.Path, "digest"));
        Golden("map", Run(file.Path, "map"));
        Golden("why", Run(file.Path, "why", duel.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Golden("character", Run(file.Path, "character", lord.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ViewJson_UsesNamesAndRefsAClientCanRead_AndRoundTrips()
    {
        var link = new LinkedText("Maren rode.", new[] { new TextLink(0, 5, new EntityRef("char", 12)) });
        var json = ViewJson.Serialize(new Headline(1, Category.FaithAndMagic, "Labyrinth", link, LinkedText.Plain("x"), 7, true));
        Assert.Contains("\"category\": \"faithAndMagic\"", json);
        Assert.Contains("\"entity\": \"char#12\"", json);
        var back = JsonSerializer.Deserialize<Headline>(json, ViewJson.Options)!;
        Assert.Equal(json, ViewJson.Serialize(back));
    }

    [Fact]
    public void UnknownViews_AreErrors()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        var output = new StringWriter();
        Assert.Equal(1, CliApp.Run(new[] { "view", file.Path, "dreams" }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("error: Unknown view 'dreams'", output.ToString());
        Assert.Equal(1, CliApp.Run(new[] { "view", file.Path, "character" }, output, () => DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ViewArguments_AreReadAsTheCliPrintsThem_AndChecked()
    {
        using var file = new TempFile();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        CliApp.Run(new[] { "advance", file.Path, "3y" }, new StringWriter(), () => DateTimeOffset.UnixEpoch);
        long lord;
        using (var store = WorldStore.Open(file.Path)) lord = store.Load().World.Settlements.Values.First(s => s.LordId != 0).LordId;
        int Code(params string[] args) => CliApp.Run(new[] { "view", file.Path }.Concat(args).ToArray(), new StringWriter(), () => DateTimeOffset.UnixEpoch);
        string Error(params string[] args)
        {
            var output = new StringWriter();
            CliApp.Run(new[] { "view", file.Path }.Concat(args).ToArray(), output, () => DateTimeOffset.UnixEpoch);
            return output.ToString();
        }

        Assert.Equal(0, Code("character", $"char#{lord}", "--json"));
        Assert.Equal(0, Code("why", "--depth", "2", "1"));
        Assert.Contains("error: No character #999999", Error("family", "999999"));
        Assert.Contains("error: --as-of must be between 1 and 4", Error("map", "--as-of", "0"));
        Assert.Contains("error: --as-of needs a value", Error("map", "--as-of"));
        Assert.Contains("error: Unknown category '42'", Error("chronicle", "--category", "42"));
        Assert.Contains("error: Unknown option --bogus", Error("digest", "--bogus"));
    }
}

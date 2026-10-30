using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Cli;

public class CliAppTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(DateTimeOffset now, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => now);
        return (code, output.ToString());
    }

    private static void NewTiny(TempFile file) => Run(T0, "new", file.Path, "--seed", "3", "--size", "tiny");

    /// <summary>A tiny world whose first region starts a drought with empty, barren fields.</summary>
    private static (long RegionId, long DroughtId) StarvingWorld(TempFile file)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), T0);
        var region = world.Regions[1];
        var log = new EventLog();
        var drought = log.Append(world.Date, new EventDraft("Drought", Place: region.Ref));
        region.Conditions[Conditions.Drought] = drought.Id;
        region.Food = 0;
        region.Fertility = 0;
        region.Resources.Remove(ResourceKinds.Fish);
        using var store = WorldStore.Open(file.Path);
        store.Save(world, log, new EventQueue());
        return (region.Id, drought.Id);
    }

    [Fact]
    public void NoArguments_PrintsUsage()
    {
        var (code, output) = Run(T0);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void UnknownCommand_PrintsUsage()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "dance", file.Path);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void New_GeneratesAWorld()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--seed", "42", "--size", "tiny");
        Assert.Equal(0, code);
        Assert.Contains("seed 42, tiny:", output);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(42UL, world.Seed);
        Assert.NotEmpty(world.Regions);
        Assert.NotNull(world.Geography);
    }

    [Fact]
    public void New_RejectsUnknownSize()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--size", "enormous");
        Assert.Equal(1, code);
        Assert.Contains("error: Unknown size", output);
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public void New_RefusesToOverwrite()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "new", file.Path, "--seed", "2");
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void Advance_MovesTheWorldAndSaves()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "advance", file.Path, "2y");
        Assert.Equal(0, code);
        Assert.Contains("Y3 M1 D1", output);
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(720, store.Load().World.Date.Day);
    }

    [Fact]
    public void CatchUp_UsesTheClock()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0.AddHours(8), "catchup", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("Advanced 120 days", output);
    }

    [Fact]
    public void MissingWorldFile_IsAnError()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "log", file.Path);
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void NotAWorldFile_IsAnError()
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path, "these are not the chronicles you are looking for");
        var (code, output) = Run(T0, "log", file.Path);
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void OutOfRangeNumbers_AreErrors()
    {
        using var file = new TempFile();
        Assert.Equal(1, Run(T0, "new", file.Path, "--seed", "-1").Code);
        NewTiny(file);
        Assert.Equal(1, Run(T0, "region", file.Path, "99999999999999999999").Code);
    }

    [Fact]
    public void Why_PrintsTheCausalChain()
    {
        using var file = new TempFile();
        var (regionId, droughtId) = StarvingWorld(file);
        Run(T0, "advance", file.Path, "1mo");
        long shortageId;
        using (var store = WorldStore.Open(file.Path))
            shortageId = store.Load().Log.All.First(e => e.Type == "FoodShortageBegan" && e.Place!.Value.Id == regionId).Id;

        var (code, output) = Run(T0, "why", file.Path, shortageId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("FoodShortageBegan", lines[0]);
        Assert.StartsWith($"  because #{droughtId} ", lines[1]);
        Assert.Contains("Drought", lines[1]);
    }

    [Fact]
    public void Log_ListsEventsSinceId_WithNames()
    {
        using var file = new TempFile();
        StarvingWorld(file);
        Run(T0, "advance", file.Path, "1mo");
        string regionName;
        using (var store = WorldStore.Open(file.Path)) regionName = store.Load().World.Regions[1].Name;

        var (code, output) = Run(T0, "log", file.Path, "--since", "1");

        Assert.Equal(0, code);
        Assert.DoesNotContain("#1 ", output);
        Assert.Contains("#2 ", output);
        Assert.Contains($"{regionName} (region#1)", output);
    }

    [Fact]
    public void Map_DrawsTerrainAndSettlements()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "map", file.Path);
        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(33, lines.Length);
        Assert.All(lines.Take(32), l => Assert.Equal(64, l.Length));
        Assert.Contains('~', output);
        Assert.Contains('@', output);
    }

    [Fact]
    public void Regions_ListsEveryRegion()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "regions", file.Path);
        Assert.Equal(0, code);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(world.Regions.Count + 1, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(world.Regions[1].Name, output);
    }

    [Fact]
    public void Region_ShowsSettlementsAndNeighbours()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "region", file.Path, "1");
        Assert.Equal(0, code);
        Assert.Contains("(region#1)", output);
        Assert.Contains("Settlements:", output);
        Assert.Contains("neighbours:", output);
    }

    [Fact]
    public void Region_UnknownId_IsAnError()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "region", file.Path, "999");
        Assert.Equal(1, code);
        Assert.Contains("error: No region #999", output);
    }

    [Fact]
    public void Characters_ListsTheNotables()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "characters", file.Path);
        Assert.Equal(0, code);
        using var store = WorldStore.Open(file.Path);
        var world = store.Load().World;
        Assert.Equal(world.ActiveCharacters.Count() + 1, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Lord", output);
    }

    [Fact]
    public void Life_TellsABiography()
    {
        using var file = new TempFile();
        NewTiny(file);
        Run(T0, "advance", file.Path, "20y");
        long lordId;
        using (var store = WorldStore.Open(file.Path))
            lordId = store.Load().World.Characters.Values.First(c => c.FatherId == 0 && c.Children.Count > 0).Id;

        var (code, output) = Run(T0, "life", file.Path, lordId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, code);
        Assert.Contains($"(char#{lordId})", output);
        Assert.Contains("traits:", output);
        Assert.Contains("children:", output);
        Assert.Contains("Life:", output);
    }

    [Fact]
    public void Artifacts_ShowWhereaboutsAndProvenance()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "artifacts", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("held by", output);

        var (detailCode, detail) = Run(T0, "artifact", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("(artifact#1)", detail);
        Assert.Contains("powers:", detail);
        Assert.Contains("Made before recorded history.", detail);
    }

    [Fact]
    public void UnknownCharacter_IsAnError()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "life", file.Path, "99999");
        Assert.Equal(1, code);
        Assert.Contains("error: No character #99999", output);
    }

    [Fact]
    public void Realms_AndTheRealmsMap()
    {
        using var file = new TempFile();
        NewTiny(file);
        var (code, output) = Run(T0, "realms", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("Kingdom of", output);
        var (mapCode, map) = Run(T0, "map", file.Path, "--layer", "realms");
        Assert.Equal(0, mapCode);
        Assert.Contains("unclaimed", map);
        var (detailCode, detail) = Run(T0, "realm", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("(realm#1)", detail);
        Assert.Contains("succession", detail);
    }

    [Fact]
    public void Wars_ListAndDetail()
    {
        using var file = new TempFile();
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), T0);
        var log = new EventLog();
        var cb = log.Append(world.Date, new EventDraft("BorderRaid"));
        var declared = log.Append(world.Date, new EventDraft("WarDeclared"));
        world.Wars[1] = new War
        {
            Id = 1,
            Name = "The Test War",
            AttackerId = 1,
            DefenderId = 2,
            Attackers = { 1 },
            Defenders = { 2 },
            CasusBelli = CasusBelli.Revenge,
            CasusBelliEventId = cb.Id,
            DeclaredEventId = declared.Id,
        };
        using (var store = WorldStore.Open(file.Path)) store.Save(world, log, new EventQueue());

        var (code, output) = Run(T0, "wars", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("The Test War", output);
        var (detailCode, detail) = Run(T0, "war", file.Path, "1");
        Assert.Equal(0, detailCode);
        Assert.Contains("casus belli: #1", detail);
        Assert.Contains("attackers:", detail);
    }
}

using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    public const string Usage = """
        Usage:
          fw new <world-file> [--seed <n>] [--size tiny|medium|large]
          fw advance <world-file> <duration>        e.g. 30d, 4mo, 7y (debug fast-forward)
          fw catchup <world-file>                   simulate the real time that has passed
          fw log <world-file> [--since <event-id>]
          fw why <world-file> <event-id> [--depth <n>]
          fw map <world-file> [--layer biome|regions]
          fw regions <world-file>
          fw region <world-file> <region-id>
          fw characters <world-file> [--settlement <id>]
          fw life <world-file> <character-id>
          fw artifacts <world-file>
          fw artifact <world-file> <artifact-id>
        """;

    public static int Run(string[] args, TextWriter output, Func<DateTimeOffset> clock)
    {
        if (args.Length < 2)
            return UsageError(output);

        var path = args[1];
        var rest = args.Skip(2).ToArray();
        try
        {
            return args[0] switch
            {
                "new" => New(path, rest, output, clock),
                "advance" => Advance(path, rest, output),
                "catchup" => CatchUpCommand(path, output, clock),
                "log" => Log(path, rest, output),
                "why" => Why(path, rest, output),
                "map" => Map(path, rest, output),
                "regions" => Regions(path, output),
                "region" => RegionDetail(path, rest, output),
                "characters" => Characters(path, rest, output),
                "life" => Life(path, rest, output),
                "artifacts" => ArtifactList(path, output),
                "artifact" => ArtifactDetail(path, rest, output),
                _ => UsageError(output),
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
                                       or KeyNotFoundException or IOException or OverflowException
                                       or DbException or JsonException)
        {
            output.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int New(string path, string[] rest, TextWriter output, Func<DateTimeOffset> clock)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"{path} already exists.");
        var now = clock();
        var seedText = Option(rest, "--seed");
        var seed = seedText is null ? (ulong)now.UtcTicks : ulong.Parse(seedText, CultureInfo.InvariantCulture);
        var sizeText = Option(rest, "--size") ?? "medium";
        if (!Enum.TryParse<WorldSize>(sizeText, ignoreCase: true, out var size) || !Enum.IsDefined(size))
            throw new FormatException($"Unknown size '{sizeText}'. Use tiny, medium or large.");

        var world = WorldGenerator.Generate(new WorldGenOptions(seed, size), now);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var store = WorldStore.Open(path);
        store.Save(world, new EventLog(), new EventQueue());
        output.WriteLine($"Created world at {path} (seed {seed}, {size.ToString().ToLowerInvariant()}: " +
                         $"{world.Regions.Count} regions, {world.Settlements.Count} settlements).");
        return 0;
    }

    private static int Advance(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var days = DurationParser.ParseDays(rest[0]);
        using var store = OpenExisting(path);
        var sim = Load(store);
        var lastId = sim.Log.NextId - 1;
        sim.AdvanceDays(days);
        store.Save(sim.World, sim.Log, sim.Queue);
        output.WriteLine($"Advanced {days} days to {sim.World.Date}.");
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int CatchUpCommand(string path, TextWriter output, Func<DateTimeOffset> clock)
    {
        using var store = OpenExisting(path);
        var sim = Load(store);
        var lastId = sim.Log.NextId - 1;
        var result = CatchUp.Run(sim, clock());
        store.Save(sim.World, sim.Log, sim.Queue);
        output.WriteLine($"Advanced {result.DaysAdvanced} days: {result.From} -> {result.To}.");
        if (result.DaysRemaining > 0)
            output.WriteLine($"The world is still {result.DaysRemaining} days behind; run catchup again later.");
        PrintEvents(output, sim.World, sim.Log.Since(lastId));
        return 0;
    }

    private static int Log(string path, string[] rest, TextWriter output)
    {
        var since = long.Parse(Option(rest, "--since") ?? "0", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var sim = Load(store);
        PrintEvents(output, sim.World, sim.Log.Since(since));
        return 0;
    }

    private static int Why(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var eventId = long.Parse(rest[0], CultureInfo.InvariantCulture);
        var depth = int.Parse(Option(rest, "--depth") ?? "4", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var sim = Load(store);
        var chain = new HistoryQuery(sim.Log).WhyChain(eventId, depth);
        PrintChain(output, sim.World, chain, 0);
        return 0;
    }

    private static int Map(string path, string[] rest, TextWriter output)
    {
        var layer = Option(rest, "--layer") ?? "biome";
        if (layer is not ("biome" or "regions")) throw new FormatException($"Unknown layer '{layer}'. Use biome or regions.");
        using var store = OpenExisting(path);
        output.WriteLine(MapRenderer.Render(store.Load().World, layer));
        return 0;
    }

    private static int Regions(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("  id name               biome      climate      people  fert danger  ley   food  conditions");
        foreach (var r in world.Regions.Values)
        {
            var people = world.LivingSettlements(r).Sum(s => s.Population);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.Id,4} {r.Name,-18} {r.Biome,-10} {r.Climate,-10} {people,8} {r.Fertility,5:0} {r.Danger,6:0} {r.LeyPower,4:0} {FoodMonths(world, r),5:0.0}mo  {string.Join(",", r.Conditions.Keys)}"));
        }
        return 0;
    }

    private static int RegionDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var r = world.Regions.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No region #{id}.");

        var inv = CultureInfo.InvariantCulture;
        output.WriteLine($"{r.Name} (region#{r.Id}) — {r.Biome}, {r.Climate}{(r.Coastal ? ", coastal" : "")}{(r.HasRiver ? ", river" : "")}");
        output.WriteLine(string.Create(inv,
            $"  tiles {r.TileCount}, farmland {r.Farmland:0}, fertility {r.Fertility:0}/{r.BaseFertility:0}, danger {r.Danger:0}, ley {r.LeyPower:0}, food {FoodMonths(world, r):0.0} months"));
        if (world.Cultures.TryGetValue(r.CultureId, out var culture))
            output.WriteLine($"  culture {culture.Name}, faith {world.Faiths[r.FaithId].Name}");
        output.WriteLine("  resources: " + string.Join(", ", r.Resources.Select(p => string.Create(inv, $"{p.Key} {p.Value.Yield:0.#}/{p.Value.Capacity:0.#}"))));
        if (r.Conditions.Count > 0)
            output.WriteLine("  conditions: " + string.Join(", ", r.Conditions.Select(c => $"{c.Key} (#{c.Value})")));
        if (world.Geography is { } geography)
            output.WriteLine("  neighbours: " + string.Join(", ", geography.Graph.LinksOf(r.Id).Select(l =>
                string.Create(inv, $"{world.Regions[l.Other(r.Id)].Name} {l.TravelDays:0.#}d{(l.Kind == LinkKind.Sea ? " by sea" : "")}"))));
        output.WriteLine("  Settlements:");
        foreach (var s in r.Settlements.Select(sid => world.Settlements[sid]))
            output.WriteLine(string.Create(inv,
                $"    {s.Name} (settlement#{s.Id}) {(s.Abandoned ? "abandoned" : s.Tier.ToString())}{(world.Characters.TryGetValue(s.LordId, out var lord) ? $" of {FullName(world, lord)}" : "")}, {s.Population} people, unrest {s.Unrest:0}, wealth {s.Wealth:0}, {string.Join("/", s.Buildings)}"));
        if (r.Memory.Count > 0)
        {
            output.WriteLine("  Remembered for:");
            foreach (var ev in r.Memory.Select(log.Get)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
        }
        return 0;
    }

    private static double FoodMonths(World world, Region region)
    {
        var need = world.LivingSettlements(region).Sum(Demography.Consumption);
        return need > 0 ? region.Food / need : 0;
    }

    private static WorldStore OpenExisting(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"No world file at {path}. Create one with: fw new {path}");
        return WorldStore.Open(path);
    }

    private static Simulator Load(WorldStore store)
    {
        var (world, log, queue) = store.Load();
        return new Simulator(world, log, queue, DefaultSystems.Create());
    }

    private static Func<EntityRef, string?> NameOf(World world) => entity => entity.Kind switch
    {
        Region.Kind => world.Regions.TryGetValue(entity.Id, out var r) ? r.Name : null,
        Settlement.Kind => world.Settlements.TryGetValue(entity.Id, out var s) ? s.Name : null,
        Character.Kind => world.Characters.TryGetValue(entity.Id, out var c) ? FullName(world, c) : null,
        Artifact.Kind => world.Artifacts.TryGetValue(entity.Id, out var a) ? a.Name : null,
        House.Kind => world.Houses.TryGetValue(entity.Id, out var h) ? $"House {h.Name}" : null,
        _ => null,
    };

    private static void PrintEvents(TextWriter output, World world, IEnumerable<WorldEvent> events)
    {
        foreach (var ev in events) output.WriteLine(EventText.Describe(ev, NameOf(world)));
    }

    private static void PrintChain(TextWriter output, World world, ChainNode node, int indent)
    {
        var prefix = new string(' ', indent * 2) + (indent == 0 ? "" : "because ");
        output.WriteLine(prefix + EventText.Describe(node.Event, NameOf(world)));
        foreach (var cause in node.Causes) PrintChain(output, world, cause, indent + 1);
    }

    private static string? Option(string[] rest, string name)
    {
        var index = Array.IndexOf(rest, name);
        return index >= 0 && index + 1 < rest.Length ? rest[index + 1] : null;
    }

    private static int UsageError(TextWriter output)
    {
        output.WriteLine(Usage);
        return 2;
    }
}

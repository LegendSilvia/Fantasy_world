using System.Globalization;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

/// <summary>Commands about people and artifacts: characters, life, artifacts, artifact.</summary>
public static partial class CliApp
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Characters(string path, string[] rest, TextWriter output)
    {
        var settlementText = Option(rest, "--settlement");
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var people = world.ActiveCharacters;
        if (settlementText is not null)
        {
            var id = long.Parse(settlementText, Inv);
            if (!world.Settlements.ContainsKey(id)) throw new KeyNotFoundException($"No settlement #{id}.");
            people = people.Where(c => c.SettlementId == id);
        }
        output.WriteLine("    id name                           age role        renown ambition   home");
        foreach (var c in people)
            output.WriteLine(string.Create(Inv,
                $"{c.Id,6} {FullName(world, c),-30} {c.Age(world.Date),3} {c.Role,-10} {c.Renown,6:0} {c.Ambition,-10} {world.Settlements[c.SettlementId].Name}"));
        return 0;
    }

    private static int Life(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var c = world.Characters.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No character #{id}.");
        string Name(long who) => who != 0 && world.Characters.TryGetValue(who, out var k) ? $"{FullName(world, k)} (char#{who})" : "—";

        var born = new Sim.Core.WorldDate(c.BirthDay);
        var status = c.Died is { } died ? $"died {died} aged {c.Age(died)} of {c.DeathCause}" : $"aged {c.Age(world.Date)}";
        output.WriteLine($"{FullName(world, c)} (char#{c.Id}) — born {born}, {status}");
        output.WriteLine($"  {c.Sex}, {world.Cultures.GetValueOrDefault(c.CultureId)?.Name ?? "?"}, of {world.Faiths.GetValueOrDefault(c.FaithId)?.Name ?? "?"}; " +
                         $"{c.Role} at {world.Settlements[c.SettlementId].Name}{(c.Faded ? " (faded into the populace)" : "")}");
        output.WriteLine($"  traits: {string.Join(", ", c.Traits)}; ambition: {c.Ambition}; renown {c.Renown.ToString("0", Inv)}");
        var a = c.Attributes;
        output.WriteLine($"  martial {a.Martial}, diplomacy {a.Diplomacy}, stewardship {a.Stewardship}, intrigue {a.Intrigue}, lore {a.Lore}, prowess {a.Prowess}");
        if (c.Wounds.Count > 0) output.WriteLine($"  wounds: {string.Join(", ", c.Wounds)}");
        output.WriteLine($"  parents: {Name(c.FatherId)}, {Name(c.MotherId)}; spouse: {Name(c.SpouseId)}");
        if (c.Children.Count > 0) output.WriteLine($"  children: {string.Join(", ", c.Children.Select(Name))}");
        var held = world.Artifacts.Values.Where(x => x.Place == ArtifactPlace.Held && x.HolderId == c.Id).ToList();
        if (held.Count > 0) output.WriteLine($"  carries: {string.Join(", ", held.Select(x => $"{x.Name} (artifact#{x.Id})"))}");
        var feelings = c.Memories.Where(m => m.About != 0).GroupBy(m => m.About)
            .Select(g => (Who: g.Key, Opinion: Relations.Opinion(c, world.Characters[g.Key])))
            .Where(f => Math.Abs(f.Opinion) >= Relations.FriendAt).OrderBy(f => f.Who).ToList();
        if (feelings.Count > 0)
            output.WriteLine("  bonds: " + string.Join(", ", feelings.Select(f => $"{(f.Opinion > 0 ? "friend" : "rival")} {Name(f.Who)}")));
        output.WriteLine("  Life:");
        foreach (var ev in new HistoryQuery(log).Life(c.Ref)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
        return 0;
    }

    private static int ArtifactList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id name                                type     material     fame  where");
        foreach (var x in world.Artifacts.Values)
            output.WriteLine(string.Create(Inv, $"{x.Id,6} {x.Name,-35} {x.Type,-8} {x.Material,-12} {x.Fame,4:0}  {Whereabouts(world, x)}"));
        return 0;
    }

    private static int ArtifactDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var x = world.Artifacts.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No artifact #{id}.");
        output.WriteLine($"{x.Name} (artifact#{x.Id}) — {x.Type.ToString().ToLowerInvariant()} of {x.Material}, fame {x.Fame.ToString("0", Inv)}");
        output.WriteLine($"  {Whereabouts(world, x)}");
        output.WriteLine("  powers: " + string.Join(", ", x.Powers.Select(p => p.Kind == "attribute" ? $"+{p.Amount} {p.Attribute}" : p.Kind)));
        if (x.CurseRevealed) output.WriteLine($"  cursed: {x.Curse}");
        output.WriteLine(x.OriginEventId == 0 ? "  Made before recorded history." : "  Made: " + EventText.Describe(log.Get(x.OriginEventId), NameOf(world)));
        if (x.Provenance.Count > 0)
        {
            output.WriteLine("  Provenance:");
            foreach (var entry in x.Provenance)
                output.WriteLine($"    {entry.How}: {EventText.Describe(log.Get(entry.EventId), NameOf(world))}");
        }
        return 0;
    }

    private static string Whereabouts(World world, Artifact x) => x.Place switch
    {
        ArtifactPlace.Held => $"held by {FullName(world, world.Characters[x.HolderId])} (char#{x.HolderId})",
        ArtifactPlace.Treasury => $"in the treasury of {world.Settlements[x.SettlementId].Name}",
        ArtifactPlace.Lost => $"lost in {world.Regions[x.RegionId].Name}",
        _ => "destroyed",
    };

    private static string FullName(World world, Character c) =>
        world.Houses.TryGetValue(c.HouseId, out var house) ? $"{c.Name} of {house.Name}" : c.Name;
}

using System.Globalization;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Cli;

/// <summary>Commands about realms and wars: realms, realm, wars, war.</summary>
public static partial class CliApp
{
    private static int RealmList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id realm                               ruler                          regions legit stab  treasury");
        foreach (var r in world.Realms.Values.Where(r => !r.Destroyed).OrderBy(r => r.LiegeId == 0 ? 0 : 1).ThenBy(r => r.Id))
        {
            var ruler = Realms.RulerOf(world, r);
            var title = (r.LiegeId == 0 ? "" : "  ") + Statecraft.Title(r);
            output.WriteLine(string.Create(Inv,
                $"{r.Id,6} {title,-35} {(ruler is null ? "(vacant)" : FullName(world, ruler)),-30} {Realms.Territory(world, r).Count(),7} {r.Legitimacy,5:0} {r.Stability,4:0} {r.Treasury,9:0}"));
        }
        return 0;
    }

    private static int RealmDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var r = world.Realms.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No realm #{id}.");
        string Name(long who) => world.Characters.TryGetValue(who, out var c) ? $"{FullName(world, c)} (char#{who})" : "—";

        output.WriteLine($"{Statecraft.Title(r)} (realm#{r.Id}){(r.Destroyed ? " — fallen" : "")}");
        output.WriteLine($"  {r.Government}, {r.SuccessionLaw} succession, magic {r.MagicLaw.ToString().ToLowerInvariant()}; colours {string.Join(" and ", r.Colors)}; phase {r.Phase}");
        output.WriteLine($"  capital {world.Settlements[r.CapitalId].Name}; ruler {Name(Realms.RulerOf(world, r)?.Id ?? 0)}");
        if (r.LiegeId != 0) output.WriteLine($"  vassal of {Statecraft.Title(world.Realms[r.LiegeId])} (realm#{r.LiegeId})");
        output.WriteLine(string.Create(Inv, $"  legitimacy {r.Legitimacy:0}, stability {r.Stability:0}, treasury {r.Treasury:0}, war weariness {r.WarWeariness:0}"));
        output.WriteLine($"  culture {world.Cultures.GetValueOrDefault(r.CultureId)?.Name ?? "?"}, faith {world.Faiths.GetValueOrDefault(r.FaithId)?.Name ?? "?"}");
        if (r.Council.Count > 0) output.WriteLine("  council: " + string.Join(", ", r.Council.Select(s => $"{s.Key} {Name(s.Value)}")));
        var vassals = Realms.VassalsOf(world, r).ToList();
        if (vassals.Count > 0) output.WriteLine("  vassals: " + string.Join(", ", vassals.Select(v => $"{Statecraft.Title(v)} (realm#{v.Id})")));
        if (r.Treaties.Count > 0)
            output.WriteLine("  treaties: " + string.Join(", ", r.Treaties.Select(t => $"{t.Kind.ToString().ToLowerInvariant()} with {world.Realms[t.With].Name}")));
        output.WriteLine("  regions: " + string.Join(", ", Realms.Domain(world, r).Select(x => x.Name)));
        var eras = new HistoryQuery(log).Eras(r.Ref);
        if (eras.Count > 0)
        {
            output.WriteLine("  Eras:");
            foreach (var era in eras) output.WriteLine($"    {era.Date}: {era.Payload["era"]}");
        }
        return 0;
    }

    private static int WarList(string path, TextWriter output)
    {
        using var store = OpenExisting(path);
        var world = store.Load().World;
        output.WriteLine("    id war                                      began         casus belli         score  status");
        foreach (var w in world.Wars.Values)
            output.WriteLine(string.Create(Inv,
                $"{w.Id,6} {w.Name,-40} {new Sim.Core.WorldDate(w.StartDay),-13} {w.CasusBelli,-18} {w.Score,6:0}  {(w.Ended ? "ended" : "raging")}"));
        return 0;
    }

    private static int WarDetail(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var id = long.Parse(rest[0], Inv);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var w = world.Wars.TryGetValue(id, out var found) ? found : throw new KeyNotFoundException($"No war #{id}.");
        string Side(IEnumerable<long> realms) => string.Join(", ", realms.Select(r => Statecraft.Title(world.Realms[r])));
        output.WriteLine($"{w.Name} (war#{w.Id}) — {w.CasusBelli}, score {w.Score.ToString("0", Inv)}, {w.Battles} battle(s){(w.Ended ? "" : ", raging")}");
        output.WriteLine($"  attackers: {Side(w.Attackers.DefaultIfEmpty(w.AttackerId))}");
        output.WriteLine($"  defenders: {Side(w.Defenders.DefaultIfEmpty(w.DefenderId))}");
        output.WriteLine("  casus belli: " + EventText.Describe(log.Get(w.CasusBelliEventId), NameOf(world)));
        output.WriteLine("  Course:");
        foreach (var ev in new HistoryQuery(log).Life(w.Ref)) output.WriteLine("    " + EventText.Describe(ev, NameOf(world)));
        return 0;
    }
}

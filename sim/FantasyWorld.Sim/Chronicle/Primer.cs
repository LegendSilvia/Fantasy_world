using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// Who is who, and why they fight (the owner, 2026-10-29: "in the first year I don't know what's going on, why they are
/// fighting, who they are"): the great realms and who rules them, the wars under way and what they are fought for, the
/// demon lord if one has risen, and the most renowned people living. Told in the first episode, after the legend.
/// </summary>
public static class Primer
{
    public const int Realms = 3, Wars = 3, Notables = 4;

    public static string? Of(World world, EventLog log)
    {
        var parts = new List<string>();
        var realms = Model.Realms.Independent(world)
            .Select(r => (Realm: r, Lands: world.Regions.Values.Count(g => g.OwnerRealmId == r.Id)))
            .Where(r => r.Lands > 0).OrderByDescending(r => r.Lands).ThenBy(r => r.Realm.Id).ToList();
        if (realms.Count > 0)
        {
            var named = realms.Take(Realms).Select(r =>
                $"the {Statecraft.Title(r.Realm)}{(Model.Realms.RulerOf(world, r.Realm) is { } ruler ? $", ruled by {ruler.Name}" : "")}, with {English.Count(r.Lands, "land")}")
                .ToList();
            var rest = realms.Count - named.Count;
            parts.Add($"The world is held by {English.List(named)}{(rest > 0 ? $", and {English.Count(rest, "lesser realm")} besides" : "")}.");
        }
        var wars = world.Wars.Values.Where(w => !w.Ended).OrderByDescending(w => w.StartDay).Take(Wars).ToList();
        foreach (var war in wars)
        {
            if (!world.Realms.TryGetValue(war.AttackerId, out var attacker) || !world.Realms.TryGetValue(war.DefenderId, out var defender)) continue;
            var why = war.CasusBelli.Length > 0 ? $", {English.CasusBelli(war.CasusBelli)}" : "";
            parts.Add($"The {Statecraft.Title(attacker)} is at war with the {Statecraft.Title(defender)}{why}: {war.Name}, begun in Year {new Core.WorldDate(war.StartDay).Year}.");
        }
        if (wars.Count == 0) parts.Add("No war is being fought, though old grudges smoulder.");
        if (world.Sagas.Values.FirstOrDefault(s => s.Active) is { } saga && world.Characters.TryGetValue(saga.DemonLordId, out var demon) && demon.IsActive)
            parts.Add($"In the dark places, the demon lord {demon.Name} has risen, and the people wait for a hero.");
        if (Legends.Beasts(world) is { } beasts) parts.Add(beasts);
        var notables = world.Characters.Values.Where(c => c.IsActive && c.Renown > 0).OrderByDescending(c => c.Renown).ThenBy(c => c.Id).Take(Notables).ToList();
        if (notables.Count > 0)
            parts.Add("Names to know: " + English.List(notables.Select(c => c.Class.Length > 0 ? $"{c.Name}, {English.WithArticle(Classes.IsAwakened(c.Class) ? Classes.Title(c.Class) : c.Class)}" : c.Name).ToList()) + ".");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}

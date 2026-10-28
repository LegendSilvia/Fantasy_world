using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Views;

/// <summary>Who held and who occupied a region at some date, and for which war.</summary>
public sealed record RegionState(long RegionId, long OwnerRealmId, long OccupierRealmId, long OccupyingWarId = 0);

/// <summary>The sides of a war at some date.</summary>
public sealed record WarSides(IReadOnlyList<long> Attackers, IReadOnlyList<long> Defenders);

/// <summary>The political world at a date: regions, who is whose vassal, which realms have fallen, and the wars' sides.</summary>
public sealed record PoliticalMap(
    bool Historical, IReadOnlyDictionary<long, RegionState> Regions, IReadOnlyDictionary<long, long> Lieges,
    IReadOnlySet<long> Fallen, IReadOnlyDictionary<long, WarSides> Wars)
{
    /// <summary>The independent realm at the top of a realm's chain of lieges at that date.</summary>
    public long TopOf(long realm)
    {
        for (var guard = 0; guard < 64 && Lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }
}

/// <summary>
/// History at a date (sub-project 2 §5): the political world replayed from the founding baseline through cessions,
/// occupations, liberations, peaces (with their vassalage terms), declarations of independence, realms' falls and
/// allies joining wars, revolutions. Today's answer is the live state. Worlds saved before the baseline existed cannot be replayed:
/// their map is the live state, marked not historical.
/// </summary>
public static class History
{
    public static PoliticalMap PoliticalMapAt(World world, EventLog log, WorldDate asOf)
    {
        if (asOf >= world.Date || world.FoundingOwners.Count == 0) return Live(world, historical: asOf >= world.Date);

        var owner = world.Regions.Keys.ToDictionary(id => id, id => world.FoundingOwners.GetValueOrDefault(id));
        var lieges = world.Realms.Keys.ToDictionary(id => id, id => world.FoundingLieges.GetValueOrDefault(id));
        var fallen = new HashSet<long>();
        var occupation = new Dictionary<long, (long Realm, long War)>();
        var sides = new Dictionary<long, (List<long> Attackers, List<long> Defenders)>();
        var over = new HashSet<long>();
        foreach (var ev in log.All)
        {
            if (ev.Date > asOf) break;
            var region = RegionOf(world, ev.Place);
            switch (ev.Type)
            {
                case "WarDeclared" when ev.Actors.TryGetValue("war", out var war):
                    sides[war.Id] = (new List<long> { ev.Actors["attacker"].Id }, new List<long> { ev.Actors["defender"].Id });
                    break;
                case "AllyJoinedWar" when ev.Actors.TryGetValue("war", out var joined) && sides.TryGetValue(joined.Id, out var s):
                    var ally = ev.Actors["ally"].Id;
                    var attacking = ev.Payload.TryGetValue("side", out var side)
                        ? side == "attackers"
                        : world.Wars.TryGetValue(joined.Id, out var known) && known.Attackers.Contains(ally);
                    (attacking ? s.Attackers : s.Defenders).Add(ally);
                    break;
                case "RegionCeded" when region != 0 && ev.Actors.TryGetValue("to", out var to):
                    owner[region] = to.Id;
                    occupation.Remove(region);
                    break;
                case "RegionOccupied" or "SiegeEnded" when region != 0 && ev.Actors.TryGetValue("realm", out var realm)
                                                           && ev.Actors.TryGetValue("war", out var at)
                                                           && (ev.Type == "RegionOccupied" || ev.Payload.GetValueOrDefault("outcome") == "fell"):
                    occupation[region] = (realm.Id, at.Id);
                    break;
                case "RegionLiberated" or "OccupationLapsed" when region != 0:
                    occupation.Remove(region);
                    break;
                case "PeaceSigned" when ev.Actors.TryGetValue("war", out var ended):
                    // As MilitarySystem.MakePeace: the war's occupations end, unless the occupier still fights the owner in another war.
                    over.Add(ended.Id);
                    var involved = sides.TryGetValue(ended.Id, out var e) ? e.Attackers.Concat(e.Defenders).ToHashSet() : new HashSet<long>();
                    foreach (var (held, occ) in occupation.ToList())
                    {
                        var holder = Top(lieges, owner[held]);
                        if (involved.Contains(occ.Realm) && (holder == 0 || involved.Contains(holder) || !AtWar(sides, over, occ.Realm, holder)))
                            occupation.Remove(held);
                    }
                    if (ev.Payload.GetValueOrDefault("terms") is { } terms && terms.Contains("vassalage", StringComparison.Ordinal)
                        && ev.Actors.TryGetValue("vanquished", out var vassal) && ev.Actors.TryGetValue("victor", out var overlord)
                        && !fallen.Contains(vassal.Id))
                        lieges[vassal.Id] = overlord.Id;
                    break;
                case "RevolutionBegan" when ev.Actors.TryGetValue("rebels", out var rising):
                    // As RevolutionSystem: the lands that rose pass to the rebels.
                    foreach (var id in (ev.Payload.GetValueOrDefault(Systems.RevolutionSystem.RegionIds) ?? "")
                                 .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse))
                    {
                        owner[id] = rising.Id;
                        occupation.Remove(id);
                    }
                    break;
                case "RevolutionEnded" when ev.Actors.TryGetValue("rebels", out var rebels) && ev.Actors.TryGetValue("realm", out var crown)
                                             && ev.Payload.GetValueOrDefault("outcome") is Systems.RevolutionSystem.Crushed or Systems.RevolutionSystem.Overthrown
                                             && !fallen.Contains(crown.Id):
                    // The rebel realm folds back into the crown.
                    fallen.Add(rebels.Id);
                    foreach (var id in owner.Where(o => o.Value == rebels.Id).Select(o => o.Key).ToList())
                    {
                        owner[id] = crown.Id;
                        occupation.Remove(id);
                    }
                    foreach (var id in occupation.Where(o => o.Value.Realm == rebels.Id).Select(o => o.Key).ToList()) occupation.Remove(id);
                    foreach (var (attackers, defenders) in sides.Values)
                    {
                        attackers.Remove(rebels.Id);
                        defenders.Remove(rebels.Id);
                    }
                    break;
                case "IndependenceDeclared" when ev.Actors.TryGetValue("realm", out var free):
                    lieges[free.Id] = 0;
                    break;
                case "RealmFell" when ev.Actors.TryGetValue("realm", out var gone):
                    fallen.Add(gone.Id);
                    foreach (var id in owner.Where(o => o.Value == gone.Id).Select(o => o.Key).ToList()) owner[id] = 0;
                    foreach (var id in occupation.Where(o => o.Value.Realm == gone.Id).Select(o => o.Key).ToList()) occupation.Remove(id);
                    foreach (var id in lieges.Where(l => l.Value == gone.Id).Select(l => l.Key).ToList()) lieges[id] = 0;
                    foreach (var (attackers, defenders) in sides.Values)
                    {
                        attackers.Remove(gone.Id);
                        defenders.Remove(gone.Id);
                    }
                    break;
            }
        }
        var regions = owner.ToDictionary(o => o.Key, o => occupation.TryGetValue(o.Key, out var occ)
            ? new RegionState(o.Key, o.Value, occ.Realm, occ.War) : new RegionState(o.Key, o.Value, 0));
        return new PoliticalMap(true, regions, lieges, fallen, sides.ToDictionary(s => s.Key, s => new WarSides(s.Value.Attackers, s.Value.Defenders)));
    }

    private static PoliticalMap Live(World world, bool historical) => new(historical,
        world.Regions.Values.ToDictionary(r => r.Id, r => new RegionState(r.Id, r.OwnerRealmId, r.OccupierRealmId,
            r.OccupierRealmId == 0 ? 0 : world.Wars.Values.Where(w => !w.Ended && w.Involves(r.OccupierRealmId)).Select(w => w.Id).FirstOrDefault())),
        world.Realms.Values.ToDictionary(r => r.Id, r => r.LiegeId),
        world.Realms.Values.Where(r => r.Destroyed).Select(r => r.Id).ToHashSet(),
        world.Wars.Values.ToDictionary(w => w.Id, w => new WarSides(w.Attackers.ToList(), w.Defenders.ToList())));

    private static long Top(Dictionary<long, long> lieges, long realm)
    {
        for (var guard = 0; guard < 64 && realm != 0 && lieges.TryGetValue(realm, out var liege) && liege != 0; guard++) realm = liege;
        return realm;
    }

    private static bool AtWar(Dictionary<long, (List<long> Attackers, List<long> Defenders)> sides, HashSet<long> over, long a, long b) =>
        sides.Any(w => !over.Contains(w.Key) && (w.Value.Attackers.Contains(a) && w.Value.Defenders.Contains(b) || w.Value.Attackers.Contains(b) && w.Value.Defenders.Contains(a)));

    private static long RegionOf(World world, EntityRef? place) => place switch
    {
        { Kind: Region.Kind } p => p.Id,
        { Kind: Settlement.Kind } p when world.Settlements.TryGetValue(p.Id, out var s) => s.RegionId,
        _ => 0,
    };
}

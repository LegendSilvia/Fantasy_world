using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Shared consequences for realms, used by the politics, diplomacy and military systems: moving a
/// capital, a realm's fall, ceding regions, and realm-level memories. A helper library, not a system.
/// </summary>
public static class Statecraft
{
    public const string LegitimacyCollapsed = "legitimacyCollapsed";
    public const string StabilityCollapsed = "stabilityCollapsed";

    public static string Title(Realm realm) => $"{realm.Rank} of {realm.Name}";

    public static void Remember(Realm realm, long eventId, long aboutRealm, double feeling, double weight) =>
        realm.Memories.Add(new RealmMemory(eventId, aboutRealm, feeling, weight));

    /// <summary>
    /// Keeps the capital inside the domain: moves it to the largest living settlement the realm still
    /// holds, or lets the realm fall if it holds none. Returns false if the realm fell.
    /// </summary>
    public static bool SecureCapital(SimContext ctx, Realm realm, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        var capital = world.Settlements[realm.CapitalId];
        if (!capital.Abandoned && world.Regions[capital.RegionId].OwnerRealmId == realm.Id) return true;
        var refuge = Realms.Domain(world, realm).SelectMany(world.LivingSettlements)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
        if (refuge is null)
        {
            Fall(ctx, realm, causes);
            return false;
        }
        realm.CapitalId = refuge.Id;
        ctx.Emit(new EventDraft("CapitalMoved",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["from"] = capital.Ref },
            Place: refuge.Ref, Causes: causes, Importance: 40, Tags: new[] { "Politics" }));
        return true;
    }

    /// <summary>
    /// A realm with no settlement left falls: land it still holds without one (mountains, wastes) passes to whoever
    /// occupies it, else to the realm holding most of the land around it; its vassals go free, its treaties lapse, its
    /// wars are left to others.
    /// </summary>
    public static void Fall(SimContext ctx, Realm realm, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        // A realm falls once: marked before its land is handed on, so the cessions below do not make it fall again.
        if (realm.Destroyed) return;
        realm.Destroyed = true;
        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id).OrderBy(r => r.Id).ToList())
        {
            var heir = world.Realms.TryGetValue(region.OccupierRealmId, out var occupier) && !occupier.Destroyed && occupier.Id != realm.Id
                ? occupier
                : world.Geography?.Graph.Neighbours(region.Id).Select(n => world.Regions[n].OwnerRealmId)
                    .Where(id => id != 0 && id != realm.Id && !world.Realms[id].Destroyed)
                    .GroupBy(id => id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => world.Realms[g.Key]).FirstOrDefault();
            if (heir is not null) Cede(ctx, region, realm, heir, causes);
        }
        var fell = ctx.Emit(new EventDraft("RealmFell",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: world.Settlements[realm.CapitalId].Ref, Causes: causes,
            Importance: realm.LiegeId == 0 ? 80 : 40, Tags: new[] { "Politics" }));
        realm.Phase = RealmPhase.Collapsed;
        realm.Council.Clear();
        foreach (var vassal in Realms.VassalsOf(world, realm).ToList()) vassal.LiegeId = 0;
        foreach (var other in world.Realms.Values) other.Treaties.RemoveAll(t => t.With == realm.Id);
        realm.Treaties.Clear();
        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id)) region.OwnerRealmId = 0;
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId == realm.Id)) region.OccupierRealmId = 0;
        foreach (var war in world.Wars.Values.Where(w => !w.Ended && w.Involves(realm.Id)))
        {
            war.Attackers.Remove(realm.Id);
            war.Defenders.Remove(realm.Id);
        }
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded && a.RealmId == realm.Id))
            Disband(world, army);
        realm.Memories.Clear();
    }

    /// <summary>Hands a region to another realm (war, inheritance): occupation and sieges end there.</summary>
    public static WorldEvent Cede(SimContext ctx, Region region, Realm from, Realm to, IReadOnlyList<Cause> causes)
    {
        region.OwnerRealmId = to.Id;
        region.OccupierRealmId = 0;
        region.SiegeProgress = 0;
        region.BesiegerArmyId = 0;
        var ceded = ctx.Emit(new EventDraft("RegionCeded",
            Actors: new Dictionary<string, EntityRef> { ["from"] = from.Ref, ["to"] = to.Ref },
            Place: region.Ref, Causes: causes, Importance: 45, Tags: new[] { "Politics", "War" }));
        Remember(from, ceded.Id, to.Id, -60, 80);
        if (!from.Destroyed) SecureCapital(ctx, from, new[] { new Cause(ceded.Id, CauseKind.Trigger, 1.0) });
        return ceded;
    }

    /// <summary>Sends the levies home (survivors return to their settlements' adults) and dissolves the army.</summary>
    public static void Disband(World world, Army army)
    {
        var raised = army.RaisedFrom.Values.Sum();
        var survival = raised > 0 ? Math.Clamp((double)army.Levies / raised, 0, 1) : 0;
        foreach (var (settlementId, levies) in army.RaisedFrom)
            if (world.Settlements.TryGetValue(settlementId, out var s) && !s.Abandoned)
                s.Adults += (int)(levies * survival);
        army.Disbanded = true;
        army.Levies = army.MenAtArms = army.Mages = 0;
        army.Knights.Clear();
        army.CommanderId = 0;
        foreach (var region in world.Regions.Values.Where(r => r.BesiegerArmyId == army.Id))
        {
            region.BesiegerArmyId = 0;
            region.SiegeProgress = 0;
        }
    }
}

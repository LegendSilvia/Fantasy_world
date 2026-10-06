using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckRealms(World world, EventLog log, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.OwnerRealmId != 0 && (!world.Realms.TryGetValue(region.OwnerRealmId, out var owner) || owner.Destroyed))
                errors.Add($"Region #{region.Id} is owned by missing or destroyed realm #{region.OwnerRealmId}.");
            if (region.OccupierRealmId != 0 && !world.Realms.ContainsKey(region.OccupierRealmId))
                errors.Add($"Region #{region.Id} is occupied by unknown realm #{region.OccupierRealmId}.");
            if (region.BesiegerArmyId != 0 && (!world.Armies.TryGetValue(region.BesiegerArmyId, out var besieger) || besieger.Disbanded))
                errors.Add($"Region #{region.Id} is besieged by a missing army #{region.BesiegerArmyId}.");
        }

        foreach (var (id, realm) in world.Realms)
        {
            var name = $"Realm #{id}";
            if (realm.Id != id) errors.Add($"{name} is stored under the wrong key ({realm.Id}).");
            if (realm.Destroyed) continue;
            if (!world.Settlements.TryGetValue(realm.CapitalId, out var capital))
                errors.Add($"{name} has unknown capital #{realm.CapitalId}.");
            else if (world.Regions[capital.RegionId].OwnerRealmId != id)
                errors.Add($"{name}'s capital #{capital.Id} lies outside its domain.");
            if (realm.LiegeId != 0 && (!world.Realms.TryGetValue(realm.LiegeId, out var liege) || liege.Destroyed))
                errors.Add($"{name} has a missing or destroyed liege #{realm.LiegeId}.");
            if (realm.LiegeId != 0 && Realms.IsBelow(world, world.Realms[realm.LiegeId], realm))
                errors.Add($"{name} is its own liege's liege (cycle).");
            if (!(realm.Legitimacy >= 0 && realm.Legitimacy <= 100) || !(realm.Stability >= 0 && realm.Stability <= 100))
                errors.Add($"{name} has legitimacy or stability outside 0–100.");
            foreach (var (seat, member) in realm.Council)
                if (!world.Characters.TryGetValue(member, out var c) || !c.IsActive)
                    errors.Add($"{name} has an inactive {seat} #{member}.");
            foreach (var treaty in realm.Treaties)
                if (!world.Realms.TryGetValue(treaty.With, out var other) || other.Destroyed)
                    errors.Add($"{name} has a treaty with missing or destroyed realm #{treaty.With}.");
            foreach (var condition in realm.Conditions.Values.Concat(realm.Memories.Select(m => m.EventId)))
                if (!log.TryGet(condition, out _)) errors.Add($"{name} cites missing event #{condition}.");
        }

        foreach (var (id, war) in world.Wars)
        {
            if (!log.TryGet(war.CasusBelliEventId, out _)) errors.Add($"War #{id} has no casus belli event.");
            if (!war.Ended && war.Attackers.Concat(war.Defenders).Any(r => !world.Realms.TryGetValue(r, out var realm) || realm.Destroyed))
                errors.Add($"War #{id} involves a missing or destroyed realm.");
        }

        foreach (var (id, army) in world.Armies)
        {
            if (army.Disbanded) continue;
            var name = $"Army #{id}";
            if (!world.Wars.TryGetValue(army.WarId, out var war) || war.Ended) errors.Add($"{name} serves a missing or ended war.");
            if (!world.Regions.ContainsKey(army.RegionId)) errors.Add($"{name} stands in unknown region #{army.RegionId}.");
            if (army.Levies < 0 || army.MenAtArms < 0 || army.Mages < 0) errors.Add($"{name} has negative troops.");
            if (army.CommanderId != 0 && (!world.Characters.TryGetValue(army.CommanderId, out var commander) || !commander.IsActive))
                errors.Add($"{name} is led by an inactive commander #{army.CommanderId}.");
            foreach (var knight in army.Knights)
                if (!world.Characters.TryGetValue(knight, out var k) || !k.IsActive) errors.Add($"{name} carries an inactive knight #{knight}.");
        }
    }
}

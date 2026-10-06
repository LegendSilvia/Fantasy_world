using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckPeople(World world, EventLog log, List<string> errors)
    {
        var seats = new HashSet<long>();
        foreach (var s in world.Settlements.Values)
        {
            if (s.LordId == 0) continue;
            if (!seats.Add(s.LordId)) errors.Add($"Character #{s.LordId} is lord of two settlements.");
            if (!world.Characters.TryGetValue(s.LordId, out var lord) || !lord.IsAlive || lord.Role != Role.Lord)
                errors.Add($"Settlement #{s.Id} names #{s.LordId} as lord, who is not a living lord.");
            else if (lord.SettlementId != s.Id) errors.Add($"Lord #{s.LordId} of settlement #{s.Id} lives elsewhere.");
            if (s.Abandoned) errors.Add($"Abandoned settlement #{s.Id} still has a lord.");
        }

        foreach (var (id, c) in world.Characters)
        {
            var name = $"Character #{id}";
            if (c.Id != id) errors.Add($"{name} is stored under the wrong key ({c.Id}).");
            if (c.BirthDay > world.Date.Day) errors.Add($"{name} is born in the future.");
            if (!c.IsAlive)
            {
                // The dead no longer change: only check that death released them.
                if (c.Role != Role.None) errors.Add($"{name} is dead but still holds the role {c.Role}.");
                if (c.SpouseId != 0) errors.Add($"{name} is dead but still married.");
                continue;
            }
            if (c.Faded && (c.Role != Role.None || c.SpouseId != 0)) errors.Add($"{name} faded but still holds a role or a marriage.");
            if (c.AmbitionTarget == id) errors.Add($"{name} seeks vengeance against themselves.");
            if (c.IsActive && world.Settlements.TryGetValue(c.SettlementId, out var home) && home.Abandoned)
                errors.Add($"{name} lives in abandoned settlement #{c.SettlementId}.");
            if (c.HouseId != 0 && !world.Houses.ContainsKey(c.HouseId)) errors.Add($"{name} has unknown house #{c.HouseId}.");
            if (!world.Settlements.ContainsKey(c.SettlementId)) errors.Add($"{name} lives in unknown settlement #{c.SettlementId}.");
            if (c.SpouseId != 0 && (!world.Characters.TryGetValue(c.SpouseId, out var spouse) || spouse.SpouseId != id))
                errors.Add($"{name} is married to #{c.SpouseId}, who is not married to them.");
            foreach (var parent in new[] { c.FatherId, c.MotherId })
                if (parent != 0 && (!world.Characters.TryGetValue(parent, out var p) || !p.Children.Contains(id)))
                    errors.Add($"{name} is not listed as a child of #{parent}.");
            foreach (var child in c.Children)
                if (!world.Characters.TryGetValue(child, out var k) || (k.FatherId != id && k.MotherId != id))
                    errors.Add($"{name} lists #{child} as a child, who does not name them as a parent.");
            foreach (var trait in c.Traits)
                if (Traits.OppositeOf(trait) is { } opposite && c.Traits.Contains(opposite))
                    errors.Add($"{name} is both {trait} and {opposite}.");
            if (c.Attributes.Martial is < 0 or > 20 || c.Attributes.Prowess is < 0 or > 20 || c.Attributes.Lore is < 0 or > 20)
                errors.Add($"{name} has attributes outside 0–20.");
            foreach (var memory in c.Memories)
                if (!log.TryGet(memory.EventId, out _)) errors.Add($"{name} remembers missing event #{memory.EventId}.");
            if (c.Role == Role.Lord && !seats.Contains(id)) errors.Add($"{name} is a lord without a seat.");
        }
    }
}

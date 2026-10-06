using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckStructures(World world, EventLog log, List<string> errors)
    {
        foreach (var s in world.Structures.Values)
        {
            if (!world.Regions.ContainsKey(s.RegionId)) errors.Add($"Structure #{s.Id} is in unknown region #{s.RegionId}.");
            if (s.SettlementId != 0 && (!world.Settlements.TryGetValue(s.SettlementId, out var home) || home.RegionId != s.RegionId))
                errors.Add($"Structure #{s.Id} names settlement #{s.SettlementId} outside its region.");
            if (s.BuilderId != 0 && !world.Characters.ContainsKey(s.BuilderId)) errors.Add($"Structure #{s.Id} has unknown builder #{s.BuilderId}.");
            if (!log.TryGet(s.StartedEventId, out _)) errors.Add($"Structure #{s.Id} cites missing event #{s.StartedEventId}.");
            if (s.State == StructureState.Standing && s.Type == StructureKind.Landmark && s.CompletedEventId == 0)
                errors.Add($"Landmark #{s.Id} stands without a completion event.");
            if (s.Progress is < 0 or > 100) errors.Add($"Structure #{s.Id} has progress {s.Progress}.");
        }

        foreach (var r in world.Routes.Values)
        {
            if (!world.Settlements.ContainsKey(r.FromId) || !world.Settlements.ContainsKey(r.ToId)) errors.Add($"Route #{r.Id} joins an unknown market.");
            else if (r.Regions.Count == 0 || r.Regions[0] != world.Settlements[r.FromId].RegionId || r.Regions[^1] != world.Settlements[r.ToId].RegionId)
                errors.Add($"Route #{r.Id} does not run from market to market.");
            if (r.Regions.Any(id => !world.Regions.ContainsKey(id))) errors.Add($"Route #{r.Id} crosses an unknown region.");
            if (!log.TryGet(r.OpenedEventId, out _)) errors.Add($"Route #{r.Id} cites missing event #{r.OpenedEventId}.");
            if (r.State != RouteState.Open && r.Volume != 0) errors.Add($"Route #{r.Id} carries trade while {r.State}.");
        }

        foreach (var m in world.Monsters.Values)
        {
            if (!world.Regions.ContainsKey(m.RegionId)) errors.Add($"Monster #{m.Id} lairs in unknown region #{m.RegionId}.");
            if (m.Might is < 1 or > 100) errors.Add($"Monster #{m.Id} has might {m.Might}.");
            if (!log.TryGet(m.RoseEventId, out _)) errors.Add($"Monster #{m.Id} cites missing event #{m.RoseEventId}.");
            if (m.State == MonsterState.Slain && m.SlainEventId == 0) errors.Add($"Monster #{m.Id} is slain without a slaying.");
        }
    }
}

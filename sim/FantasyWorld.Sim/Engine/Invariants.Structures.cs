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
    }
}

using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static partial class Invariants
{
    static partial void CheckArtifacts(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, a) in world.Artifacts)
        {
            var name = $"Artifact #{id}";
            if (a.Id != id) errors.Add($"{name} is stored under the wrong key ({a.Id}).");
            var located = a.Place switch
            {
                ArtifactPlace.Held => world.Characters.TryGetValue(a.HolderId, out var holder) && holder.IsActive,
                ArtifactPlace.Treasury => world.Settlements.ContainsKey(a.SettlementId),
                ArtifactPlace.Lost => world.Regions.ContainsKey(a.RegionId),
                _ => true,
            };
            if (!located) errors.Add($"{name} is {a.Place} somewhere that does not exist (or with a dead or faded holder).");
            if (a.Place != ArtifactPlace.Held && a.HolderId != 0) errors.Add($"{name} is {a.Place} but names holder #{a.HolderId}.");
            if (!(a.Fame >= 0 && a.Fame <= 100)) errors.Add($"{name} has fame {a.Fame} outside 0–100.");
            if (a.OriginEventId != 0 && !log.TryGet(a.OriginEventId, out _)) errors.Add($"{name} has missing origin event #{a.OriginEventId}.");
            foreach (var entry in a.Provenance)
                if (!log.TryGet(entry.EventId, out _)) errors.Add($"{name} provenance cites missing event #{entry.EventId}.");
        }
    }
}

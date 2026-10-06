using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Events;

public sealed record WorldEvent(
    long Id,
    WorldDate Date,
    string Type,
    SortedDictionary<string, EntityRef> Actors,
    EntityRef? Place,
    SortedDictionary<string, string> Payload,
    IReadOnlyList<Cause> Causes,
    int Importance,
    IReadOnlyList<string> Tags);

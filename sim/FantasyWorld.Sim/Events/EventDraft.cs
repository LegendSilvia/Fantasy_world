namespace FantasyWorld.Sim.Events;

/// <summary>What a system emits. The log assigns id and date.</summary>
public sealed record EventDraft(
    string Type,
    IReadOnlyDictionary<string, EntityRef>? Actors = null,
    EntityRef? Place = null,
    IReadOnlyDictionary<string, string>? Payload = null,
    IReadOnlyList<Cause>? Causes = null,
    int Importance = 10,
    IReadOnlyList<string>? Tags = null);

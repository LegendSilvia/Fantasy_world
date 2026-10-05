using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Engine;

/// <summary>Something that will happen on a future world day, handled by the system that owns Kind.</summary>
public sealed record ScheduledAction(
    WorldDate Date,
    int Priority,
    long Seq,
    string Kind,
    SortedDictionary<string, string> Data);

namespace FantasyWorld.Sim.Events;

public enum CauseKind { Trigger, Motive, Enabler }

public sealed record Cause(long EventId, CauseKind Kind, double Weight);

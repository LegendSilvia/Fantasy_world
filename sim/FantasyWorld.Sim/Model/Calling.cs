namespace FantasyWorld.Sim.Model;

/// <summary>
/// An awakened calling, as the world's lore records it (the owner, 2026-10-29: a power needs a background, what it does and
/// its condition): its name, the calling it grew from, where the power comes from, what it does, and what it asks or
/// where it fails; the first who bore it, the event of that awakening and its day.
/// </summary>
public sealed record Calling(string Name, string Base, string Origin, string Does, string Condition, long FirstBearerId, long EventId, long Day);

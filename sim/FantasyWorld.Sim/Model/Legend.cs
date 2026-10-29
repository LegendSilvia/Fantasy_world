namespace FantasyWorld.Sim.Model;

/// <summary>
/// The world's legend (the owner, 2026-10-29): how its people tell its making and its ages, in brief (a line or two the
/// episodes open with) and in full (told once, in the first episode that has it). <see cref="SinceEventId"/> is the last
/// event in the log when it was written. <see cref="Told"/>: an episode that told it in full has aired; until then, the
/// next episode tells it in full.
/// </summary>
public sealed record WorldLegend(string Summary, string Full, long SinceEventId = 0, bool Told = false);

/// <summary>
/// A secret of the world, uncovered by a deed (the owner, 2026-10-29: clearing a labyrinth reveals lore no one knew): its
/// title, what it reveals, the event that revealed it, and when and where.
/// </summary>
public sealed record Secret(long EventId, string Title, string Text, long Day, long RegionId, long RevealerId);

/// <summary>
/// A tale the world's songs tell (the owner, 2026-10-29: the lore is revised every 25 years, some remembered, some
/// forgotten, and the forgotten become secrets to uncover): a great deed of its age, sung from the revision after it.
/// Forgotten, it is no longer sung; uncovered again at the bottom of a labyrinth, it is a secret of the world.
/// </summary>
public sealed record Tale(string Title, string Text, int Year, long EventId, int SungFrom, bool Forgotten = false, int ForgottenIn = 0, bool Uncovered = false);

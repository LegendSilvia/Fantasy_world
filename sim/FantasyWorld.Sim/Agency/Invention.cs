namespace FantasyWorld.Sim.Agency;

/// <summary>
/// Something new needs a name (spec §8.1 "Invent"): a forged artifact, a war, an era. Facts describe it; Fallback
/// is the engine's own name, used offline. Subject identifies it (e.g. <c>artifact#12</c>).
/// </summary>
public sealed record InventionBrief(string Kind, string Subject, string Facts, string Fallback);

/// <summary>A name, and lore (a few sentences of legend) when an LLM invented it.</summary>
public sealed record Invention(string Name, string? Lore);

/// <summary>A calling awakened (the owner, 2026-10-29): who awakens, from what calling, and what befell them.</summary>
public sealed record CallingBrief(string Subject, string Facts, string Base, IReadOnlyList<string> Known);

/// <summary>An awakened calling's name and lore: where its power comes from, what it does, and its condition.</summary>
public sealed record CallingInvention(string Name, string Origin, string Does, string Condition);

/// <summary>Someone who could be behind a beginning: a person, a realm or a beast ("char#12", "realm#3", "monster#9").</summary>
public sealed record IncidentSuspect(string Ref, string Who);

/// <summary>A beginning the world allows this year: its kind, the situation, who could be behind it, and the cause the rules see.</summary>
public sealed record IncidentCandidate(int Index, string Kind, string Situation, IReadOnlyList<IncidentSuspect> Suspects, long SuggestedCause, string Title,
    string Subject = "");

/// <summary>An event of the world that may be named as a beginning's cause.</summary>
public sealed record HistoryLine(long Id, string Told);

/// <summary>The year's beginnings to choose (the owner, 2026-10-29): how many, from what, and the history to find causes in.</summary>
public sealed record IncidentBrief(int Year, int Count, IReadOnlyList<IncidentCandidate> Candidates, IReadOnlyList<HistoryLine> History);

/// <summary>
/// A beginning as chosen: the candidate; its cause (an event of the history, or 0 and a cause never told before); its
/// culprit (a suspect's reference, or empty for none) and whether their hand stays hidden; its title and lore.
/// </summary>
public sealed record IncidentChoice(int Candidate, long CauseEventId, string? CauseText, string Culprit, bool Hidden, string Title, string? Lore);

public interface IInventor
{
    Invention Invent(InventionBrief brief);

    /// <summary>An awakened calling, or null for the engine's own.</summary>
    CallingInvention? InventCalling(CallingBrief brief) => null;

    /// <summary>The year's beginnings as Claude makes them, or null for the rules' own.</summary>
    IReadOnlyList<IncidentChoice>? PlanIncidents(IncidentBrief brief) => null;
}

/// <summary>The engine's own names, no lore: the offline path and the test baseline.</summary>
public sealed class RulesInventor : IInventor
{
    public Invention Invent(InventionBrief brief) => new(brief.Fallback, null);
}

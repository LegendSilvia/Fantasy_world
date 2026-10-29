namespace FantasyWorld.Sim.Model;

/// <summary>
/// A beginning (the owner, 2026-10-29): one of the few events a year where a chain of history starts: a council deciding,
/// stores lost, conspirators meeting, a plot against a life, a labyrinth spilling its beasts. It has a cause (an event of the
/// world, or one never told before) and may have a culprit, known at once or hidden until they are unmasked; what follows
/// grows from it. Kept so a plot can be carried out and a hidden culprit found out in later years.
/// </summary>
public sealed record Incident(long EventId, string Kind, int Year, string Culprit = "", bool Hidden = false, string Victim = "",
    bool Unmasked = false, string Target = "", bool Resolved = false)
{
    public const string Council = "council", Loss = "loss", Meeting = "meeting", Plot = "plot", Outbreak = "outbreak";

    // The wider pool (the owner, 2026-10-29: ten to twenty scenarios).
    public const string Theft = "theft", Abduction = "abduction", Blight = "blight", Forgery = "forgery", Insult = "insult",
        Sedition = "sedition", Sacrilege = "sacrilege", Raid = "raid", Quest = "quest", Pretender = "pretender", Envoy = "envoy",
        Betrayal = "betrayal", Lovers = "lovers", Framed = "framed";

    public static readonly IReadOnlyList<string> Kinds =
        [Council, Loss, Meeting, Plot, Outbreak, Theft, Abduction, Blight, Forgery, Insult, Sedition, Sacrilege, Raid, Quest, Pretender, Envoy, Betrayal, Lovers, Framed];

    /// <summary>Beginnings that are someone's doing: one of the suspects is always behind them.</summary>
    public static readonly IReadOnlySet<string> Done = new HashSet<string> { Plot, Meeting, Insult, Sedition, Pretender, Quest, Betrayal, Framed };

    /// <summary>Beginnings done in the open: their doer is never hidden.</summary>
    public static readonly IReadOnlySet<string> Open = new HashSet<string> { Insult, Pretender, Quest, Betrayal };
}

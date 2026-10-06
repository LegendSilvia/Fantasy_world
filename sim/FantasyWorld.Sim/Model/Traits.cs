namespace FantasyWorld.Sim.Model;

/// <summary>
/// Personality traits. Opposed pairs never coexist; acquired traits come from events.
/// Traits modify opinions (shared +, opposed −) and the weights of rule-based decisions.
/// </summary>
public static class Traits
{
    public const string Brave = "brave", Craven = "craven";
    public const string Kind = "kind", Cruel = "cruel";
    public const string Generous = "generous", Greedy = "greedy";
    public const string Honest = "honest", Deceitful = "deceitful";
    public const string Calm = "calm", Wrathful = "wrathful";
    public const string Devout = "devout", Cynical = "cynical";
    public const string Ambitious = "ambitious", Content = "content";
    public const string Diligent = "diligent", Lazy = "lazy";
    public const string Trusting = "trusting", Paranoid = "paranoid";

    // Acquired through events.
    public const string Scarred = "scarred";
    public const string Maimed = "maimed";
    public const string Humiliated = "humiliated";
    public const string Renowned = "renowned";
    public const string Bereaved = "bereaved";

    public static readonly IReadOnlyList<(string A, string B)> Opposites = new[]
    {
        (Brave, Craven), (Kind, Cruel), (Generous, Greedy), (Honest, Deceitful), (Calm, Wrathful),
        (Devout, Cynical), (Ambitious, Content), (Diligent, Lazy), (Trusting, Paranoid),
    };

    public static string? OppositeOf(string trait)
    {
        foreach (var (a, b) in Opposites)
        {
            if (a == trait) return b;
            if (b == trait) return a;
        }
        return null;
    }

    /// <summary>+10 per shared personality trait, −15 per opposed pair.</summary>
    public static int Compatibility(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        var score = 0;
        foreach (var trait in a)
        {
            if (OppositeOf(trait) is not { } opposite) continue;
            if (b.Contains(trait)) score += 10;
            if (b.Contains(opposite)) score -= 15;
        }
        return score;
    }
}

/// <summary>What a notable is currently striving for. Drives monthly deeds.</summary>
public static class Ambitions
{
    public const string Renown = "renown";
    public const string Artifact = "artifact";
    public const string Magic = "magic";
    public const string Wealth = "wealth";
    public const string Vengeance = "vengeance";
    public const string Family = "family";
}

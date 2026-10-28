using System.Globalization;
using System.Text.RegularExpressions;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// The template voice's English (sub-project 5 §3): names that take an article mid-sentence, numbers in words up to
/// twelve, plurals and lists, and the phrases the simulation's terse payloads stand for (causes of death, causes of
/// war, peace terms).
/// </summary>
public static partial class English
{
    private static readonly string[] Words =
        { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    /// <summary>Zero to twelve in words ("no" for zero), larger numbers with thousands separators.</summary>
    public static string Number(long n) => n is >= 0 and <= 12 ? Words[n] : n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"one region", "six regions", "no regions".</summary>
    public static string Count(long n, string noun) => $"{Number(n)} {(n == 1 ? noun : Plural(noun))}";

    public static string Plural(string noun) =>
        noun.EndsWith('y') && noun.Length > 1 && !"aeiou".Contains(noun[^2]) ? noun[..^1] + "ies"
        : noun.EndsWith('s') || noun.EndsWith('x') || noun.EndsWith("ch", StringComparison.Ordinal) || noun.EndsWith("sh", StringComparison.Ordinal) ? noun + "es"
        : noun + "s";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>A name as it reads inside a sentence: "The War of the Ash" becomes "the War of the Ash".</summary>
    public static string InProse(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name[4..] : name;

    /// <summary>"a storm", "an ogre".</summary>
    public static string WithArticle(string word) => ("aeiou".Contains(char.ToLowerInvariant(word.FirstOrDefault())) ? "an " : "a ") + word;

    public static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>How a cause of death reads after the dead one's name.</summary>
    public static string Death(string cause) => cause switch
    {
        "old age" => "died of old age",
        "illness" => "died of an illness",
        "plague" => "died of the plague",
        "famine" => "starved to death",
        "childbirth" => "died in childbirth",
        "battle" => "fell in battle",
        "a duel" => "was killed in a duel",
        "burned for sorcery" => "was burned for sorcery",
        "executed" => "was executed",
        "old wounds" => "died of old wounds",
        _ => $"was killed by {InProse(cause)}",
    };

    /// <summary>What a notable dreams of, from their ambition.</summary>
    public static string Ambition(string ambition) => ambition switch
    {
        "artifact" => "a great treasure",
        "magic" => "mastery of magic",
        "family" => "a family of their own",
        _ => ambition,
    };

    /// <summary>Why a war was declared, as it reads after "declared war on them".</summary>
    public static string CasusBelli(string kind) => kind switch
    {
        "claim" => "to press a claim",
        "holy war" => "in a holy war",
        "revenge" => "for revenge",
        "reconquest" => "to win back lost land",
        "resource need" => "for want of food",
        "artifact recovery" => "to recover a stolen treasure",
        "independence" => "for its freedom",
        "border dispute" => "over a disputed border",
        "glory" => "for glory",
        "revolution" => "in revolution, to throw off its rule",
        "ancient hatred" => "out of an ancient hatred between their peoples",
        _ => $"for {kind}",
    };

    /// <summary>Peace terms ("6 region(s) ceded; tribute") as a list ("six regions ceded and tribute").</summary>
    public static string Terms(string terms) =>
        List(terms.Split("; ", StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Ceded().Replace(t, m => Count(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value)))
            .ToList());

    /// <summary>A payload value as prose: whole numbers in words or with separators, names lowered mid-sentence.</summary>
    public static string Value(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Number(n) : InProse(value);

    [GeneratedRegex(@"(\d+) (\w+)\(s\)")]
    private static partial Regex Ceded();
}

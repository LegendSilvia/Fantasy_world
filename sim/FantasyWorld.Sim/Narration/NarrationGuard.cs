using System.Globalization;
using System.Text.RegularExpressions;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// Guardrails for LLM narration (sub-project 5 §4): a narration may only use the facts in its brief. Every number
/// must appear in the brief (as digits or in words), and every capitalised word (a name) must be a word of the brief,
/// a title or one of the plain words a sentence may open with; possessives and hyphenated names are checked by their
/// parts. A narration that invents one is rejected, journaled as rejected (for evals) and the template prose is used.
/// </summary>
public static partial class NarrationGuard
{
    /// <summary>Titles and calendar words a chronicler may use without the brief naming them.</summary>
    private static readonly HashSet<string> Titles = new(StringComparer.Ordinal)
    {
        "I", "Year", "Years", "Spring", "Summer", "Autumn", "Winter", "God", "Gods", "Lord", "Lords", "Lady", "Ladies", "King", "Kings", "Queen",
        "Sir", "Dame", "Saint", "Saints", "Duke", "Duchess", "Count", "Countess", "Baron", "Baroness", "Emperor", "Empress", "Prince", "Princess",
        "Kingdom", "Duchy", "County", "Barony", "Empire", "House", "Host", "War", "Peace", "High", "Priest", "Mage", "Knight", "Knights",
    };

    /// <summary>Plain words a sentence may open with (a sentence's first word is checked too, so "Orrin wept." is caught).</summary>
    private static readonly HashSet<string> Openings = new(StringComparer.Ordinal)
    {
        "The", "A", "An", "In", "On", "At", "And", "But", "So", "Then", "When", "While", "After", "Before", "Once", "It", "He", "She", "They",
        "His", "Her", "Their", "Its", "This", "That", "These", "Those", "There", "Here", "Of", "For", "With", "From", "To", "By", "As", "No", "None",
        "Not", "All", "Few", "Many", "Some", "Only", "Even", "Yet", "Thus", "Still", "Since", "Until", "Though", "Although", "Where", "What",
        "Who", "Whom", "Whose", "Why", "How", "Each", "Every", "Both", "Neither", "Nor", "Or", "If", "Now", "Long", "Later", "Soon", "Few",
    };

    /// <summary>What the narration says that the brief does not: invented numbers and names.</summary>
    public static IReadOnlyList<string> Problems(string narration, string brief)
    {
        var briefWords = Words().Matches(brief).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var lowerBrief = briefWords.Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var briefNumbers = Number().Matches(brief).Select(m => m.Value.Replace(",", "", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (Match m in Number().Matches(narration))
        {
            var digits = m.Value.Replace(",", "", StringComparison.Ordinal);
            var inWords = long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? English.Number(n) : digits;
            if (!briefNumbers.Contains(digits) && !lowerBrief.Contains(inWords)) problems.Add($"number {digits}");
        }
        foreach (Match m in Capitalised().Matches(narration))
        {
            var opening = m.Index == 0 || Regex.IsMatch(narration[..m.Index], @"[.!?:;""“]\s*$");
            foreach (var part in Possessive().Replace(m.Value, "").Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length == 0 || !char.IsUpper(part[0]) || Titles.Contains(part) || briefWords.Contains(part)) continue;
                if (opening && (Openings.Contains(part) || lowerBrief.Contains(part.ToLowerInvariant()))) continue;
                problems.Add($"name {part}");
            }
        }
        return problems;
    }

    public static bool Faithful(string narration, string brief) => Problems(narration, brief).Count == 0;

    /// <summary>A whole number, its thousands separators inside it (a trailing comma is punctuation).</summary>
    [GeneratedRegex(@"(?<![\p{L}\d])\d(?:[\d,]*\d)?(?![\p{L}\d])")]
    private static partial Regex Number();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    /// <summary>A capitalised word, with its possessive and hyphenated parts.</summary>
    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Lu}[\p{L}'’-]*")]
    private static partial Regex Capitalised();

    [GeneratedRegex(@"['’]s$|['’]$")]
    private static partial Regex Possessive();
}

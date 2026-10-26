using System.Globalization;
using System.Text.RegularExpressions;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// Guardrails for LLM narration (sub-project 5 §4): a narration may only use the facts in its brief. Every number,
/// written in digits or in words ("four years", "ten thousand fell"), must appear in the brief (as digits or in words), and every capitalised word (a name) must be a word of the brief,
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
        var briefValues = NumberWords(brief).Select(n => n.Value).ToHashSet();
        foreach (var (value, text) in NumberWords(narration))
        {
            // "one" is as often a pronoun ("no one", "one of them") as a count.
            if (value == 1) continue;
            if (!briefNumbers.Contains(value.ToString(CultureInfo.InvariantCulture)) && !briefValues.Contains(value)) problems.Add($"number {text}");
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

    private static readonly Dictionary<string, long> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
        ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
        ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Dictionary<string, long> Scales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hundred"] = 100, ["thousand"] = 1_000, ["million"] = 1_000_000,
    };

    /// <summary>Numbers written in words ("four", "twenty-one", "two hundred and six", "ten thousand"), with their text.</summary>
    public static IReadOnlyList<(long Value, string Text)> NumberWords(string text)
    {
        var found = new List<(long, string)>();
        var words = Words().Matches(text).ToList();
        for (var i = 0; i < words.Count;)
        {
            if (!IsNumberWord(words[i].Value)) { i++; continue; }
            long total = 0, current = 0;
            var (start, end) = (words[i].Index, i);
            for (; end < words.Count; end++)
            {
                var word = words[end].Value;
                if (end > i)
                {
                    var gap = text[(words[end - 1].Index + words[end - 1].Length)..words[end].Index];
                    if (gap.Trim() is not ("" or "-")) break;
                }
                if (Units.TryGetValue(word, out var unit)) current += unit;
                else if (word.Equals("hundred", StringComparison.OrdinalIgnoreCase)) current = Math.Max(1, current) * 100;
                else if (Scales.TryGetValue(word, out var scale)) { total += Math.Max(1, current) * scale; current = 0; }
                // "and" joins inside a number ("two hundred and six"), not two numbers.
                else if (word.Equals("and", StringComparison.OrdinalIgnoreCase) && end + 1 < words.Count && IsNumberWord(words[end + 1].Value)
                         && current >= 100) continue;
                else break;
            }
            var last = words[end - 1];
            found.Add((total + current, text[start..(last.Index + last.Length)]));
            i = end;
        }
        return found;
    }

    private static bool IsNumberWord(string word) => Units.ContainsKey(word) || Scales.ContainsKey(word);

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

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// How the world's invented names are said (M3). The first time a name appears in a script, the chronicler's model is
/// asked, with the other new names of the day, for a plain English respelling ("Deithkialwold" → "Deeth-kyal-wold"), or the
/// word itself for an ordinary word. An answer is used only if it looks like the name (hyphenated syllables, the same first
/// letter, a similar length), so a respelling can only change how a name sounds, never what the script says. Answers are
/// kept in the manifest for good, so a name sounds the same every morning; a word without a usable answer is asked again,
/// up to <see cref="MaxTries"/> times, then said as written.
/// </summary>
public static partial class Pronouncer
{
    /// <summary>New words asked about a morning; the rest wait for the next.</summary>
    public const int MaxNewWords = 60;

    public const int MaxTries = 3;

    public const string System = "You help a text-to-speech narrator say invented fantasy names. Answer every word. If it is an invented "
                                 + "name, give a respelling in plain English letters with hyphens between syllables (no spaces), the way an "
                                 + "English reader would say the name as it looks. If it is an ordinary English word or title, give the word "
                                 + "itself, unchanged.";

    /// <summary>Capitalised words that are plainly English, never asked about.</summary>
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "The", "And", "But", "This", "That", "Then", "There", "Their", "They", "When", "While", "After", "Before", "With", "From", "Elsewhere",
        "How", "Thus", "Until", "Remember", "Year", "Years", "Spring", "Summer", "Autumn", "Winter", "Kingdom", "Duchy", "County", "Barony",
        "Empire", "House", "Host", "War", "Peace", "Age", "Great", "Hall", "Holy", "Long", "Lord", "Lady", "King", "Queen", "Saint", "Order",
        "Sir", "Dame", "Duke", "Count", "Baron", "Emperor", "Empress", "Prince", "Princess", "Mage", "Knight", "High", "Priest", "Old", "New",
    };

    /// <summary>
    /// The names of a script worth asking about, in order of first use: capitalised words (possessives folded into their
    /// name), longer than three letters, never written in lower case in the script, and not plainly English.
    /// </summary>
    public static IReadOnlyList<string> Words(string script)
    {
        var lower = LowerWords().Matches(script).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        return Capitalised().Matches(script).Select(m => Possessive().Replace(m.Value, ""))
            .Where(w => w.Length > 3 && !Ordinary.Contains(w) && !lower.Contains(w.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Asks about the names of <paramref name="script"/> not yet known; returns how many were settled.</summary>
    public static async Task<int> LearnAsync(ILlmProvider? provider, string model, Manifest manifest, string script, TextWriter log,
        CancellationToken ct = default)
    {
        var fresh = Words(script).Where(w => !manifest.Pronunciations.ContainsKey(w)).Take(MaxNewWords).ToList();
        if (fresh.Count == 0 || provider is null) return 0;
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                respellings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new { word = new { type = "string" }, say = new { type = "string" } },
                        required = new[] { "word", "say" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "respellings" },
            additionalProperties = false,
        });
        var prompt = PromptText.Section("words", string.Join("\n", fresh.Select(w => PromptText.Data(w, 40))));
        var request = new LlmRequest(ModelTier.Narrate, "pronounce|" + string.Join(",", fresh), System, prompt, schema, MaxTokens: 2000);
        LlmResponse answer;
        try
        {
            answer = await provider.CompleteJsonAsync(request, model, ct).ConfigureAwait(false);
        }
        catch (LlmUnavailableException e)
        {
            log.WriteLine($"warning: no respellings today ({e.Message}); the names are asked about again tomorrow.");
            return 0;
        }

        var said = Parse(answer.Json, fresh) ?? new Dictionary<string, string>();
        var settled = 0;
        foreach (var word in fresh)
        {
            if (said.TryGetValue(word, out var say))
            {
                manifest.Pronunciations[word] = say;
                manifest.PronunciationTries.Remove(word);
                settled++;
            }
            else if ((manifest.PronunciationTries[word] = manifest.PronunciationTries.GetValueOrDefault(word) + 1) >= MaxTries)
            {
                manifest.Pronunciations[word] = word; // no usable answer after a few tries: said as written
                manifest.PronunciationTries.Remove(word);
                settled++;
            }
        }
        log.WriteLine($"Asked how to say {fresh.Count} new name(s): {said.Count(s => s.Key != s.Value)} respelled, "
                      + $"{said.Count(s => s.Key == s.Value)} said as written (${answer.CostUsd:0.0000}).");
        return settled;
    }

    /// <summary>
    /// The usable answers (a respelling that looks like its name, or the word itself), or null when the answer is not the
    /// expected shape at all. Curly apostrophes and trailing punctuation are tidied first.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Parse(string json, IReadOnlyCollection<string> asked)
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("respellings", out var list)
                || list.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("word", out var w) || !item.TryGetProperty("say", out var s)
                    || w.ValueKind != JsonValueKind.String || s.ValueKind != JsonValueKind.String) continue;
                var word = w.GetString()!;
                var say = s.GetString()!.Replace('’', '\'').Trim().TrimEnd('.', ',', ';', ':', '!', '?').Trim();
                if (asked.Contains(word) && (say == word || LooksLike(say, word))) said[word] = say;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return said;
    }

    /// <summary>A respelling of a name: hyphenated letters, the same first letter, about the same length.</summary>
    public static bool LooksLike(string say, string word)
    {
        if (!Respelling().IsMatch(say) || char.ToUpperInvariant(say[0]) != char.ToUpperInvariant(word[0])) return false;
        var (a, b) = (say.Count(char.IsLetter), word.Count(char.IsLetter));
        return a >= b * 0.6 && a <= b * 2.0;
    }

    /// <summary>The rules worth a dictionary: words said differently from how they are written.</summary>
    public static SortedDictionary<string, string> Rules(Manifest manifest) =>
        new(manifest.Pronunciations.Where(p => p.Key != p.Value).ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);

    /// <summary>
    /// The dictionary for today's speech: made once, then extended with the rules learned since (each extension is a new
    /// version); none while there are no rules.
    /// </summary>
    public static async Task<IReadOnlyList<DictionaryRef>> DictionaryAsync(ISpeech speech, Manifest manifest, string worldName, CancellationToken ct = default)
    {
        var rules = Rules(manifest);
        if (rules.Count == 0) return [];
        if (manifest.DictionaryId is null || manifest.DictionaryVersionId is null)
        {
            var made = await speech.CreateDictionaryAsync($"{worldName} names", rules, ct).ConfigureAwait(false);
            (manifest.DictionaryId, manifest.DictionaryVersionId) = (made.Id, made.VersionId);
            manifest.DictionaryWords = new SortedSet<string>(rules.Keys, StringComparer.Ordinal);
        }
        else if (rules.Where(r => !manifest.DictionaryWords.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value) is { Count: > 0 } added)
        {
            var extended = await speech.AddRulesAsync(manifest.DictionaryId, added, ct).ConfigureAwait(false);
            manifest.DictionaryVersionId = extended.VersionId;
            manifest.DictionaryWords.UnionWith(added.Keys);
        }
        return [new DictionaryRef(manifest.DictionaryId, manifest.DictionaryVersionId)];
    }

    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Lu}\p{L}+(?:['’-]\p{L}+)*")]
    private static partial Regex Capitalised();

    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Ll}+")]
    private static partial Regex LowerWords();

    [GeneratedRegex(@"['’]s$")]
    private static partial Regex Possessive();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z'-]*[A-Za-z]$")]
    private static partial Regex Respelling();
}

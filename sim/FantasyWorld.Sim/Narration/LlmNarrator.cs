using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// Narration by LLM (spec §8.7), on its own budget. The prompt carries the template prose plus the facts, motives,
/// accounts and lore stored on the events; answers are cached in the journal by event, so reading the same
/// passage twice costs nothing. Without an answer, or with one that invents names or numbers (see
/// <see cref="NarrationGuard"/>), the template narration is returned.
/// </summary>
public sealed class LlmNarrator(StoryContext story) : INarrator
{
    public const int MaxLength = 2000;

    private readonly TemplateNarrator _templates = new(story.World);

    /// <summary>
    /// The daily broadcast's spoken script (sub-project 6 §3.2), in the chronicler's voice, section by section (sub-project 7
    /// §6): one text per section, in order, so each stays a chapter. The brief is the template episode itself, so the guard
    /// holds the chronicler to its names and numbers; offline, over budget, refused, unfaithful, the wrong number of sections
    /// or the wrong length, the episode keeps its template script. Keyed by date and events, so a rebuilt episode asks anew.
    /// </summary>
    public Episode EpisodeScript(Episode episode, int maxCharacters)
    {
        var parts = episode.Parts;
        var task = $"Write the script of today's spoken episode of the chronicle, to be read aloud, as exactly {parts.Count} sections, one for "
                   + "each numbered section of the brief and in its order: the opening line, the headline told in full, each face-off as a "
                   + "commentator before a bout (both sides' strengths and weaknesses from the figures given, where each could win or lose it, "
                   + "and whom the odds favour, in words only: never a percentage, a chance or odds as a number; without deciding it), each other story "
                   + "briefly (where a section lists a war's marches, say each in a few words, how long and from where to where, naming no place between; "
                   + "where it gives a fight's days, tell the fight blow by blow through them, day by day, to its end), "
                   + "then (where the brief has a section asking what to make of it all) your own view of the year as its chronicler: "
                   + "what it meant, who rose and who fell, what to watch for, ending in a conclusion of your own; judge and wonder freely, "
                   + "but state no fact the brief does not give. Then a closing line from "
                   + $"{episode.Chronicler}. Each section is read as its own chapter, so each must stand alone. Plain prose for the ear: no "
                   + "headings, numbers of sections, lists, markup or stage directions, no dates written as digits other than the years given. "
                   + "Name no one and nothing the brief does not name, and give no number it does not give: work out no counts, ages or spans "
                   + $"of years. At most {maxCharacters} characters in all. "
                   + "Separately, in scenes, one entry per section in the same order: for each section the brief marks with a scene "
                   + "to paint, a still picture of the moment that section tells, for a painter, in one or two sentences (for a fight, "
                   + "who in what colours and the decisive clash; otherwise the place, the people in it as the story has them, the "
                   + "light and the mood), with no names and no words or letters in the picture; an empty string for every other "
                   + "section. The scenes are never read aloud.";
        // A script much shorter than the template has dropped stories: that is a failed answer, not a retelling.
        var minimum = episode.Script.Length * 3 / 5;
        // Names (some invented by the LLM earlier) enter the prompt only as data, section by section (spec §8.6).
        // A face-off's figures go with its section, for the commentary; they are facts the narration may use.
        // A war's moves go with its section likewise, in the order they were made.
        string Figures(EpisodeSection p) => (p.FaceOff is { } f ? $"\n(Face-off figures:\n{PromptText.Data(f.Figures(), 1200)})" : "")
                                            + (p.Moves is { Count: > 0 } m ? $"\n(The war's marches:\n{PromptText.Data(Campaign.Brief(m), 1600)})" : "")
                                            + (p.Beats is { Count: > 1 } b ? $"\n(The fight, day by day:\n{PromptText.Data(Fights.Brief(b), 2400)})" : "");
        // A scene to paint is marked with the template's picture of it, for the chronicler's own (the episode's stills).
        string Paint(EpisodeSection p) => p.Scene is { } scene ? $"\n(A scene to paint; as the facts show it: {PromptText.Data(scene, Stills.MaxCharacters)})" : "";
        var brief = string.Join("\n\n", parts.Select((p, i) => $"[{i + 1}] "
            + string.Join("\n\n", p.Text.Split("\n\n").Select(paragraph => PromptText.Data(paragraph, maxCharacters))) + Figures(p) + Paint(p)));
        var facts = Episode.ScriptOf(parts) + string.Concat(parts.Select(Figures));
        var key = $"narrate|episode-sections|{episode.Date}|{episode.FirstEventId}-{episode.LastEventId}";
        if (!story.Gateway.MayAnswer(ModelTier.Narrate, key)) return episode;
        var prompt = PromptText.Section("brief", brief) + $"\n\n{task} Write as the world's chronicler; use only the facts given.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            // The count is checked on the answer: the API's structured output takes no item counts above one.
            properties = new
            {
                sections = new { type = "array", items = new { type = "string" } },
                scenes = new { type = "array", items = new { type = "string" } },
            },
            required = new[] { "sections", "scenes" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Narrate, key, story.Bible, prompt, schema, MaxTokens: Math.Max(1500, maxCharacters / 2));
        string? Why(string json) => Sections(json, parts.Count) is not { } texts ? $"not {parts.Count} sections"
            : string.Join("\n\n", texts).Length is var n && (n < minimum || n > maxCharacters) ? $"{n} characters, not {minimum} to {maxCharacters}"
            : string.Join(", ", NarrationGuard.Problems(string.Join("\n\n", texts), facts, task).Distinct().Take(8)) is { Length: > 0 } problems ? problems
            : null;
        var answer = story.Gateway.Ask(request, story.World.Date.Day, json => Why(json) is null, whyInvalid: json => Why(json) ?? "");
        if (answer is null || Sections(answer, parts.Count) is not { } written) return episode;
        // The chronicler's picture of each marked scene replaces the template's; a missing or overlong one keeps it.
        var scenes = Scenes(answer, parts.Count);
        var sections = parts.Select((p, i) => p with
        {
            Text = written[i],
            Scene = p.Scene is not null && scenes?[i] is { Length: > 0 and <= Stills.MaxCharacters } painted ? painted : p.Scene,
        }).ToList();
        return episode with { Script = Episode.ScriptOf(sections), Sections = sections, Writer = Episode.ChroniclerWriter };
    }

    /// <summary>The answer's scenes, one a section (empty where none), if it has exactly <paramref name="count"/>.</summary>
    private static IReadOnlyList<string>? Scenes(string json, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("scenes", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == count
                ? list.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString()!.Trim() : "").ToList()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The answer's section texts, if it has exactly <paramref name="count"/> non-empty ones.</summary>
    private static IReadOnlyList<string>? Sections(string json, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("sections", out var list)
                || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != count) return null;
            var texts = list.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString()!.Trim() : "").ToList();
            return texts.All(t => t.Length > 0) ? texts : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Narrate(WorldEvent ev) =>
        Ask($"narrate|event|{ev.Id}", ev.Date.Day, Facts(ev), "Retell this event in two or three sentences.") ?? _templates.Narrate(ev);

    public string NarrateChain(ChainNode chain)
    {
        var facts = new StringBuilder();
        var depth = 0;
        void Walk(ChainNode node, int level)
        {
            depth = Math.Max(depth, level);
            facts.AppendLine($"{new string(' ', level * 2)}{(level == 0 ? "" : "caused by: ")}{Facts(node.Event)}");
            foreach (var cause in node.Causes) Walk(cause, level + 1);
        }
        Walk(chain, 0);
        return Ask($"narrate|chain|{chain.Event.Id}|{depth}", chain.Event.Date.Day, facts.ToString(),
                   "Tell how this came to pass, from the earliest cause to the event itself, in one paragraph of at most six sentences.")
               ?? _templates.NarrateChain(chain);
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return _templates.Digest(digest);
        var facts = new StringBuilder($"From {digest.From} to {digest.To}, {digest.EventCount} events; the most important:\n");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) facts.AppendLine($"{group.Label}:");
            foreach (var entry in group.Entries) facts.AppendLine($"- {Facts(entry.Event)}");
        }
        var ids = string.Join(",", digest.Entries.Select(e => e.Event.Id));
        return Ask($"narrate|digest|{ids}", digest.To.Day, facts.ToString(),
                   "Write the morning digest for a reader returning to this world: what happened while they were away, most important first, in at most three short paragraphs.")
               ?? _templates.Digest(digest);
    }

    private string Facts(WorldEvent ev)
    {
        var line = new StringBuilder(PromptText.Data(_templates.Narrate(ev), 600));
        if (ev.Payload.TryGetValue("lore", out var lore)) line.Append($" [lore: {PromptText.Data(lore, 400)}]");
        return line.ToString();
    }

    private string? Ask(string key, long day, string facts, string task, int maxLength = MaxLength, int minLength = 1)
    {
        if (!story.Gateway.MayAnswer(ModelTier.Narrate, key)) return null;
        var prompt = PromptText.Section("brief", facts) + $"\n\n{task} Write as the world's chronicler; use only the facts given.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { text = new { type = "string" } },
            required = new[] { "text" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Narrate, key, story.Bible, prompt, schema, MaxTokens: Math.Max(1500, maxLength / 2));
        var json = story.Gateway.Ask(request, day, answer => Text(answer, maxLength, minLength) is { } text && NarrationGuard.Faithful(text, facts),
            whyInvalid: answer => Text(answer, maxLength, minLength) is { } text
                ? string.Join(", ", NarrationGuard.Problems(text, facts).Distinct().Take(8))
                : $"not a text of {minLength} to {maxLength} characters");
        return json is null ? null : Text(json, maxLength, minLength);
    }

    private static string? Text(string json, int maxLength, int minLength)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("text", out var t)
                || t.ValueKind != JsonValueKind.String) return null;
            var text = t.GetString()!.Trim();
            return text.Length >= Math.Max(1, minLength) && text.Length <= maxLength ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

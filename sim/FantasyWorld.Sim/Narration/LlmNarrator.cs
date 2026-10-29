using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
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
    public Episode EpisodeScript(Episode episode, int maxCharacters, string? recent = null)
    {
        var parts = episode.Parts;
        var task = $"Write the script of today's spoken episode of the chronicle, to be read aloud, as exactly {parts.Count} sections, one for "
                   + "each numbered section of the brief and in its order: the opening line, the headline told in full, each face-off as a "
                   + "commentator before a bout (both sides' strengths and weaknesses from the figures given, where each could win or lose it, "
                   + "and whom the odds favour, in words only: never a percentage, a chance or odds as a number; without deciding it), each other story "
                   + "briefly (where a section lists a war's marches, say each in a few words, how long and from where to where, naming no place between; "
                   + "where it gives a fight's days, tell the fight blow by blow through them, day by day, to its end: what each fighter did, how "
                   + "the other answered, each blow shaped by how that fighter fights, the abilities they used and the twist that turned it; but "
                   + "where the face-off figures show one side far the stronger and the stronger won, no twist: tell their aura and menace, "
                   + "the weaker one's dread, the gap between them), "
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
        // A duel's fighters come with their abilities and what they are known for, so the fight is told as their deeds.
        var reputation = Reputation.Of(story.World, story.Log);
        string Fighters(FaceOff f) => f.Kind != FaceOff.Duel ? "" : string.Concat(new[] { f.A, f.B }.Select(side =>
            side.Ref.Split('#') is [_, var idText] && long.TryParse(idText, out var cid) && story.World.Characters.TryGetValue(cid, out var c)
                ? $"\n{side.Name}: fights {Talents.Brief(c)}; abilities {Abilities.Brief(story.World, c)}; {Arms.Brief(story.World, c)}" + (reputation.Line(c.Id) is { } known ? $"; {known}" : "")
                : ""));
        string Figures(EpisodeSection p) => (p.FaceOff is { } f ? $"\n(Face-off figures:\n{PromptText.Data(f.Figures() + Fighters(f), 2000)})" : "")
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
        var chronicled = episode with { Script = Episode.ScriptOf(sections), Sections = sections, Writer = Episode.ChroniclerWriter };
        return Review(chronicled, brief, facts, minimum, maxCharacters, task, recent);
    }

    /// <summary>
    /// The editor's pass (the owner, 2026-10-29), before the script is voiced: Claude reads the whole transcript as a viewer
    /// would hear it, beside the brief and why each thing it tells came to be (each section's chain of causes, the world's
    /// legend and secrets), and changes or adds only what the viewer needs: a fact the brief contradicts, a cause left
    /// unexplained, a story that depends on another told later, a repetition. Held to the same guard as the chronicler; a
    /// review that changes nothing, or fails it, leaves the chronicler's script as it was. The recent episodes' transcripts
    /// (<paramref name="recent"/>) show what the audience has already heard; the editor's feedback on the world itself, for
    /// its maker, is kept on the episode.
    /// </summary>
    private Episode Review(Episode written, string brief, string facts, int minimum, int maxCharacters, string rules, string? recent)
    {
        var parts = written.Parts;
        var key = $"narrate|episode-review|{written.Date}|{written.FirstEventId}-{written.LastEventId}";
        if (!story.Gateway.MayAnswer(ModelTier.Narrate, key)) return written;
        // Why each section's story came to be, so the editor knows what the chronicler took for granted.
        string Causes(EpisodeSection p) => p.EventId != 0 && story.Log.TryGet(p.EventId, out var ev)
            && Episode.Thread(story.Log, ev, 3).Skip(1).Select(c => _templates.Sentence(c)).ToList() is { Count: > 0 } why
            ? $"\n(Why it came to be, nearest cause first: {PromptText.Data(string.Join(" ", why), 900)})" : "";
        var why = string.Concat(parts.Select((p, i) => Causes(p) is { Length: > 0 } c ? $"\n[{i + 1}]{c}" : ""));
        var lore = (story.World.Legend is { } legend ? $"The world's legend: {PromptText.Data(legend.Summary, 400)}\n" : "")
                   + string.Concat(story.World.Secrets.Select(x => $"A secret uncovered: {PromptText.Data(x.Title)}: {PromptText.Data(x.Text, 500)}\n"));
        var transcript = string.Join("\n\n", parts.Select((p, i) => $"[{i + 1}] {PromptText.Data(p.Text, maxCharacters)}"));
        var prompt = PromptText.Section("brief", brief + (why.Length > 0 ? $"\n\nWhy things are as they are:{why}" : "") + (lore.Length > 0 ? $"\n\n{lore}" : ""))
            + (recent is { Length: > 0 } ? "\n\n" + PromptText.Section("recent_episodes", PromptText.Data(recent, RecentCharacters)) : "")
            + "\n\n" + PromptText.Section("transcript", transcript)
            + $"\n\nYou are the episode's editor. The transcript is what will be voiced, section by section, exactly as written. Read it whole, "
            + "as a viewer hearing it once would, and decide whether anything must change: a fact the brief contradicts; a cause a viewer needs "
            + "to follow a story, left untold (add it in a few words, from why things are as they are); a story that leans on another told "
            + "after it; a repetition, within the episode or of what the recent episodes already told (a story that goes on should say "
            + "so, not start afresh); a sentence hard to follow by ear. Keep the chronicler's voice and change nothing that works. "
            + $"The rules the chronicler wrote by still hold: {rules} "
            + $"Answer with changed (whether you changed anything), notes (what you changed and why, or why nothing), sections: the whole "
            + $"transcript, exactly {parts.Count} sections in the same order, each as it should be voiced; and feedback, for the world's "
            + "maker and never voiced: three to six short lines on the world itself as this episode and the recent ones show it (not the "
            + "writing): what felt repetitive, thin, implausible or confusing in what happens, what the audience would want more of, and "
            + "a feature or rule that might make the world's stories better.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                changed = new { type = "boolean" },
                notes = new { type = "string" },
                sections = new { type = "array", items = new { type = "string" } },
                feedback = new { type = "string" },
            },
            required = new[] { "changed", "notes", "sections", "feedback" },
            additionalProperties = false,
        });
        // The causes and the lore are facts the editor may use too.
        var known = facts + why + "\n" + lore;
        string? Why(string json) => Sections(json, parts.Count) is not { } texts ? $"not {parts.Count} sections"
            : string.Join("\n\n", texts).Length is var n && (n < minimum || n > maxCharacters) ? $"{n} characters, not {minimum} to {maxCharacters}"
            : string.Join(", ", NarrationGuard.Problems(string.Join("\n\n", texts), known, rules).Distinct().Take(8)) is { Length: > 0 } problems ? problems
            : null;
        var request = new LlmRequest(ModelTier.Narrate, key, story.Bible, prompt, schema, MaxTokens: Math.Max(2000, maxCharacters / 2));
        var answer = story.Gateway.Ask(request, story.World.Date.Day, json => Why(json) is null, whyInvalid: json => Why(json) ?? "");
        if (answer is null) return written;
        var reviewed = written with { Feedback = Feedback(answer) };
        if (!Changed(answer) || Sections(answer, parts.Count) is not { } edited) return reviewed;
        var sections = parts.Select((p, i) => p with { Text = edited[i] }).ToList();
        return reviewed with { Script = Episode.ScriptOf(sections), Sections = sections };
    }

    /// <summary>The most of the recent episodes' transcripts the editor is shown.</summary>
    public const int RecentCharacters = 9000;

    /// <summary>The editor's feedback on the world, if it gave any.</summary>
    private static string? Feedback(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("feedback", out var f) && f.ValueKind == JsonValueKind.String && f.GetString()!.Trim() is { Length: > 0 } text
                ? text.Length <= 3000 ? text : text[..3000] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the editor says it changed anything (an answer that does not say counts as a change only if it differs).</summary>
    private static bool Changed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return !doc.RootElement.TryGetProperty("changed", out var c) || c.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
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

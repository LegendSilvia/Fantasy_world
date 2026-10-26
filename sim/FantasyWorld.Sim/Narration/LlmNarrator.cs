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
    /// The daily broadcast's spoken script (sub-project 6 §3.2), in the chronicler's voice. The brief is the template
    /// episode itself, so the guard holds the chronicler to its names and numbers; offline, over budget, refused, unfaithful
    /// or the wrong length, the episode keeps its template script. Keyed by date and events, so a rebuilt episode asks anew.
    /// </summary>
    public Episode EpisodeScript(Episode episode, int maxCharacters)
    {
        var task = "Write the script of today's spoken episode of the chronicle, to be read aloud. Keep its order: the opening line, "
                   + "the headline told in full, the other stories briefly, how it came to this (the chain of causes, earliest first) "
                   + $"when the brief tells one, and a closing line from {episode.Chronicler}. Plain prose for the ear: no headings, lists, "
                   + $"markup or stage directions, no dates written as digits other than the years given. Name no one and nothing the brief does "
                   + $"not name, and give no number it does not give: work out no counts, ages or spans of years. At most {maxCharacters} characters.";
        // A script much shorter than the template has dropped stories: that is a failed answer, not a retelling.
        var minimum = episode.Script.Length * 3 / 5;
        // Names (some invented by the LLM earlier) enter the prompt only as data, paragraph by paragraph (spec §8.6).
        var brief = string.Join("\n\n", episode.Script.Split("\n\n").Select(p => PromptText.Data(p, maxCharacters)));
        var script = Ask($"narrate|episode|{episode.Date}|{episode.FirstEventId}-{episode.LastEventId}", story.World.Date.Day, brief,
            task, maxCharacters, minimum);
        return script is null ? episode : episode with { Script = script, Writer = Episode.ChroniclerWriter };
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

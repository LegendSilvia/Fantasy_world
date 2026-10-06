using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// Narration by LLM (spec §8.7), on its own budget. The prompt carries the template prose plus the facts, motives,
/// accounts and lore stored on the events; answers are cached in the journal by event, so reading the same
/// passage twice costs nothing. Without an answer, the template narration is returned.
/// </summary>
public sealed class LlmNarrator(StoryContext story) : INarrator
{
    public const int MaxLength = 2000;

    private readonly TemplateNarrator _templates = new(story.World);

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

    private string Facts(WorldEvent ev)
    {
        var line = new StringBuilder(PromptText.Data(_templates.Narrate(ev), 600));
        if (ev.Payload.TryGetValue("lore", out var lore)) line.Append($" [lore: {PromptText.Data(lore, 400)}]");
        return line.ToString();
    }

    private string? Ask(string key, long day, string facts, string task)
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
        var request = new LlmRequest(ModelTier.Narrate, key, story.Bible, prompt, schema, MaxTokens: 1500);
        var json = story.Gateway.Ask(request, day, answer => Text(answer) is not null);
        return json is null ? null : Text(json);
    }

    private static string? Text(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("text", out var t)
                || t.ValueKind != JsonValueKind.String) return null;
            var text = t.GetString()!.Trim();
            return text.Length is > 0 and <= MaxLength ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

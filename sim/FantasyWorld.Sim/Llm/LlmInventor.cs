using System.Text.Json;
using FantasyWorld.Sim.Agency;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Names and lore by LLM (spec §8.1 "Invent"): forged artifacts, wars and eras. The answer is journaled under the
/// subject, so a replay renames identically; without an answer the engine's own name stands.
/// </summary>
public sealed class LlmInventor(StoryContext story) : IInventor
{
    public const int MaxName = 60;

    public Invention Invent(InventionBrief brief)
    {
        var day = story.World.Date.Day;
        var key = $"invent|{brief.Kind}|{brief.Subject}|{day}";
        if (!story.Gateway.MayAnswer(ModelTier.Invent, key)) return new Invention(brief.Fallback, null);
        var prompt = PromptText.Section("brief",
                $"Date: {story.World.Date}\nSomething new needs a name: {brief.Kind}.\nFacts: {PromptText.Data(brief.Facts, 600)}\n"
                + $"The engine's working name: {PromptText.Data(brief.Fallback)}")
            + $"\n\nName this {brief.Kind} as the people of the world would (at most {MaxName} characters, no quotation marks), "
            + "and write its lore: two or three sentences of legend, as a chronicler would record it.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { name = new { type = "string" }, lore = new { type = "string" } },
            required = new[] { "name", "lore" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Invent, key, story.Bible, prompt, schema, MaxTokens: 1024);
        var json = story.Gateway.Ask(request, day, answer => Parse(answer) is not null);
        return json is not null && Parse(json) is { } invention ? invention : new Invention(brief.Fallback, null);
    }

    private static Invention? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) return null;
            var name = PromptText.Data(n.GetString()!.Replace("\"", "", StringComparison.Ordinal), MaxName + 1);
            if (name.Length == 0 || name.Length > MaxName) return null;
            var lore = root.TryGetProperty("lore", out var l) && l.ValueKind == JsonValueKind.String ? PromptText.Data(l.GetString(), 600) : "";
            return new Invention(name, lore.Length > 0 ? lore : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

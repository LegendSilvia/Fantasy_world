using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Model;

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

    /// <summary>
    /// The world's legend (the owner, 2026-10-29), as its people tell it: a line or two to open each episode with, and the
    /// full telling for the first; journaled under the world, so it is written once. Without an answer the rules' stands.
    /// </summary>
    public WorldLegend Legend(WorldLegend fallback)
    {
        var day = story.World.Date.Day;
        var key = $"legend|{story.World.Seed}";
        if (!story.Gateway.MayAnswer(ModelTier.Invent, key)) return fallback;
        var prompt = PromptText.Section("brief", $"Facts of the world:\n{PromptText.Data(Legends.Facts(story.World), 2400)}")
            + "\n\nWrite the world's legend as its people tell it: how the world was made, its first age, why its peoples distrust each "
            + "other, what the gods did and left undone, the labyrinths beneath and the shadow it fears. Use only the names the facts "
            + "give; invent no new person or place by name, but leave room for secrets yet to be found. In summary, one or two "
            + $"sentences (at most {Legends.MaxSummary} characters) to open a daily chronicle with; in full, three short paragraphs "
            + $"to be read aloud (at most {Legends.MaxFull} characters), plain prose for the ear.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { summary = new { type = "string" }, full = new { type = "string" } },
            required = new[] { "summary", "full" },
            additionalProperties = false,
        });
        WorldLegend? Read(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("summary", out var s) || !root.TryGetProperty("full", out var f)
                    || s.ValueKind != JsonValueKind.String || f.ValueKind != JsonValueKind.String) return null;
                var (summary, full) = (PromptText.Data(s.GetString(), Legends.MaxSummary + 1), PromptText.Data(f.GetString(), Legends.MaxFull + 1));
                return summary.Length is > 20 and <= Legends.MaxSummary && full.Length is > 200 and <= Legends.MaxFull
                    ? new WorldLegend(summary, full, fallback.SinceEventId) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var request = new LlmRequest(ModelTier.Invent, key, story.Bible, prompt, schema, MaxTokens: 1500);
        var json = story.Gateway.Ask(request, day, answer => Read(answer) is not null);
        return json is not null && Read(json) is { } legend ? legend : fallback;
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

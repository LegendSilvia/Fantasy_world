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

    /// <summary>
    /// An awakened calling (the owner, 2026-10-29): its name, where its power comes from, what it does in a fight, and its
    /// condition (what it costs, or when it fails); journaled under the subject. Null without an answer.
    /// </summary>
    public CallingInvention? InventCalling(CallingBrief brief)
    {
        var day = story.World.Date.Day;
        var key = $"invent|calling|{brief.Subject}|{day}";
        if (!story.Gateway.MayAnswer(ModelTier.Invent, key)) return null;
        var prompt = PromptText.Section("brief", $"Date: {story.World.Date}\n{PromptText.Data(brief.Facts, 900)}"
                + (brief.Known.Count > 0 ? $"\nCallings already awakened in the world, not to be repeated: {string.Join(", ", brief.Known.Select(k => PromptText.Data(k)))}" : ""))
            + $"\n\nThey awaken into a new calling grown from the {brief.Base}'s. Name it (one or two words, at most 30 characters, no quotation "
            + "marks), and record it as the world's lore would: origin, where the power comes from (one or two sentences, from what befell "
            + "them and the world as the brief tells it); does, what it lets them do in a fight (one sentence); condition, what it costs them "
            + "or when it fails them (one sentence).";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { name = new { type = "string" }, origin = new { type = "string" }, does = new { type = "string" }, condition = new { type = "string" } },
            required = new[] { "name", "origin", "does", "condition" },
            additionalProperties = false,
        });
        CallingInvention? Read(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? Field(string f, int max) => root.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String
                    && PromptText.Data(v.GetString()!.Replace("\"", "", StringComparison.Ordinal), max + 1) is { Length: > 0 } t && t.Length <= max ? t : null;
                return Field("name", 30) is { } name && !name.Contains(':') && Field("origin", 400) is { } origin && Field("does", 300) is { } does
                       && Field("condition", 300) is { } condition
                    ? new CallingInvention(name, origin, does, condition) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var request = new LlmRequest(ModelTier.Invent, key, story.Bible, prompt, schema, MaxTokens: 1024);
        var json = story.Gateway.Ask(request, day, answer => Read(answer) is not null);
        return json is null ? null : Read(json);
    }

    /// <summary>
    /// The year's beginnings (the owner, 2026-10-29: "let Claude cook this up"): from what the world allows, Claude chooses
    /// which happen, what caused each (any event of the history, or a cause never told before), who was behind it (one of
    /// those who could be, or no one) and whether their hand stays hidden, and tells each. Journaled under the year.
    /// </summary>
    public IReadOnlyList<IncidentChoice>? PlanIncidents(IncidentBrief brief)
    {
        var day = story.World.Date.Day;
        var key = $"incidents|{brief.Year}";
        if (!story.Gateway.MayAnswer(ModelTier.Invent, key)) return null;
        var candidates = string.Join("\n", brief.Candidates.Select(c =>
            $"[{c.Index}] {c.Kind}: {PromptText.Data(c.Situation, 300)}"
            + (c.Suspects.Count > 0 ? $" Could be behind it: {string.Join("; ", c.Suspects.Select(s => $"{s.Ref} ({PromptText.Data(s.Who, 120)})"))}." : " No one could be behind it but chance.")
            + (c.SuggestedCause != 0 ? $" The rules see its cause in event {c.SuggestedCause}." : "")));
        var history = string.Join("\n", brief.History.Select(h => $"{h.Id}: {PromptText.Data(h.Told, 240)}"));
        var prompt = PromptText.Section("brief", $"Year {brief.Year}.\nWhat could begin this year:\n{candidates}\n\nThe world's recent history:\n{history}")
            + $"\n\nChoose {brief.Count} of these beginnings to happen this year, the ones that make the richest story, each the root of what "
            + "will follow (a council deciding, a theft, an abduction, a betrayal at a feast, forbidden love, a false accusation, a plot against a life, and the like). For each give: "
            + "candidate, its number; cause, the number of the history event that set it off (any you like, near or far), or 0 with cause_text, "
            + "one sentence naming a cause never told before; culprit, the reference of the one behind it (only from those listed for it), or "
            + "an empty string for no one; hidden, whether their hand stays secret for now (it may be found out in a later year); title, a "
            + "name for it as the chronicle would give (at most 60 characters); lore, two or three sentences telling what happened, as the "
            + "chronicle would record it (if the culprit is hidden, do not name them). Use only the names the brief gives.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                incidents = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            candidate = new { type = "integer" }, cause = new { type = "integer" }, cause_text = new { type = "string" },
                            culprit = new { type = "string" }, hidden = new { type = "boolean" }, title = new { type = "string" }, lore = new { type = "string" },
                        },
                        required = new[] { "candidate", "cause", "cause_text", "culprit", "hidden", "title", "lore" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "incidents" },
            additionalProperties = false,
        });
        IReadOnlyList<IncidentChoice>? Read(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("incidents", out var list) || list.ValueKind != JsonValueKind.Array) return null;
                var choices = new List<IncidentChoice>();
                foreach (var item in list.EnumerateArray())
                {
                    string Text(string f, int max) => item.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String
                        ? PromptText.Data(v.GetString()!.Replace("\"", "", StringComparison.Ordinal), max) : "";
                    if (!item.TryGetProperty("candidate", out var c) || !c.TryGetInt32(out var candidate)
                        || !item.TryGetProperty("cause", out var k) || !k.TryGetInt64(out var cause)) return null;
                    var hidden = item.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;
                    var title = Text("title", MaxName + 1);
                    if (title.Length is 0 or > MaxName) return null;
                    var lore = Text("lore", 700);
                    choices.Add(new IncidentChoice(candidate, cause, Text("cause_text", 300) is { Length: > 0 } t && cause == 0 ? t : null,
                        Text("culprit", 40), hidden, title, lore.Length > 0 ? lore : null));
                }
                return choices.Count > 0 ? choices : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var request = new LlmRequest(ModelTier.Invent, key, story.Bible, prompt, schema, MaxTokens: 3000);
        var json = story.Gateway.Ask(request, day, answer => Read(answer) is not null);
        return json is null ? null : Read(json);
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

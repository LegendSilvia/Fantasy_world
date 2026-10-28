using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Decisions by LLM (spec §8.2). The rules choice is always drawn first, so the RNG stream is consumed the same
/// way online, offline and on replay; the LLM may then pick any legal option, and its reasoning becomes the
/// motive of the event the choice causes. No answer (offline, over budget, invalid) means the rules choice.
/// </summary>
public sealed class LlmDecider(StoryContext story, IDecider? rules = null) : IDecider
{
    private readonly IDecider _rules = rules ?? new RulesDecider();

    public DecisionOption Decide(DecisionBrief brief, Rng rng)
    {
        var fallback = _rules.Decide(brief, rng);
        var legal = brief.Options.Where(o => o.Weight > 0).ToList();
        if (legal.Count < 2 || !story.World.Characters.TryGetValue(brief.CharacterId, out var who)) return fallback;

        var day = story.World.Date.Day;
        var keys = legal.Select(o => o.Key).ToList();
        var key = $"decide|{brief.CharacterId}|{brief.Situation}|{day}|{string.Join(",", keys)}";
        if (!story.Gateway.MayAnswer(ModelTier.Decide, key)) return fallback;
        var total = legal.Sum(o => o.Weight);
        var options = new StringBuilder();
        foreach (var o in legal)
            options.AppendLine($"- {o.Key}: {PromptText.Data(o.Summary)} (inclination {(o.Weight / total * 100).ToString("0", CultureInfo.InvariantCulture)}%)");
        var name = PromptText.Data(Names.Full(story.World, who));
        var prompt = PromptText.Section("brief",
            $"Date: {story.World.Date}\n{Briefs.Character(story, who)}\n\nSituation: {Situation(brief.Situation, name)}\n\nOptions:\n{options}")
            + $"\n\nChoose what {name} does. Give your reasoning as {name}'s motive, in one or two sentences.";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                choice = new { type = "string", @enum = keys },
                reasoning = new { type = "string" },
                flavor = new { type = "string" },
            },
            required = new[] { "choice", "reasoning", "flavor" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Decide, key, story.Bible, prompt, schema);

        var json = story.Gateway.Ask(request, day, answer => Parse(answer, keys) is not null);
        if (json is null || Parse(json, keys) is not { } answer) return fallback;
        return legal.First(o => o.Key == answer.Choice) with { Reasoning = PromptText.Data(answer.Reasoning, 300) };
    }

    private sealed record Answer(string Choice, string Reasoning);

    private static Answer? Parse(string json, IReadOnlyList<string> keys)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("choice", out var choice) || choice.ValueKind != JsonValueKind.String
                || !keys.Contains(choice.GetString()!)) return null;
            var reasoning = root.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
            return new Answer(choice.GetString()!, reasoning);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Situation(string situation, string name) => situation switch
    {
        "courtship" => $"{name} is free to marry and weighs the matches nearby.",
        "war or peace" => $"{name} rules a realm at peace and weighs the grievances history offers against keeping the peace.",
        "sue for peace" => $"{name}'s realm is losing a war, and the victors offer terms.",
        "seal the demon lord" => $"{name}, a sage of great learning, can seal away the demon lord that terrorises the world, but the sealing will cost their life.",
        "after the revolution" => $"{name} led a revolution against the crown and has won its civil war, and must choose what comes of it.",
        "campaign" => $"{name} leads a host at war and chooses its next move this month: each move is a single march to a neighbouring land, or none.",
        _ => $"Driven by the ambition of {situation}, {name} considers what to do this season.",
    };
}

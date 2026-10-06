using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Arbitration by LLM (spec §8.3) for duels, hunts and delves; battle fates stay with the rules (too many per
/// battle for the budget). The rules roll is always drawn first for RNG parity. The LLM sees both sides and the
/// odds, and may pick only a plausible outcome (base probability at least the floor). Drama budget: when underdogs
/// won 4 of the last 20 arbitrations, only outcomes of 15% or more are offered until upsets thin out. The engine
/// applies the chosen outcome; the LLM's account is stored on the event.
/// </summary>
public sealed class LlmArbiter(StoryContext story, IArbiter? rules = null) : IArbiter
{
    public const int DramaWindow = 20;
    public const int DramaBudget = 4;
    public const double TightFloor = 0.15;

    private static readonly HashSet<string> Arbitrated = new(StringComparer.Ordinal) { Encounters.Duel, Encounters.Hunt, Encounters.Delve };

    private readonly IArbiter _rules = rules ?? new RulesArbiter();

    public EncounterOutcome Resolve(Encounter encounter, Rng rng)
    {
        var fallback = _rules.Resolve(encounter, rng);
        var world = story.World;
        if (!Arbitrated.Contains(encounter.Kind) || !world.Characters.TryGetValue(encounter.AttackerId, out var attacker)) return fallback;

        var key = $"arbitrate|{encounter.Kind}|{encounter.AttackerId}|{encounter.DefenderId}|{world.Date.Day}";
        if (!story.Gateway.MayAnswer(ModelTier.Arbitrate, key)) return fallback;
        // A journaled answer is judged by the plausibility floor alone: the drama budget only shapes new questions,
        // so a replay accepts exactly what the live run accepted.
        var tight = !story.Gateway.Knows(ModelTier.Arbitrate, key) && DramaSpent();
        var allowed = encounter.Outcomes.Where(o => encounter.IsPlausible(o) && (!tight || o.Probability >= TightFloor)).ToList();
        if (allowed.Count < 2) return fallback;

        var day = world.Date.Day;
        var keys = allowed.Select(o => o.Key).ToList();
        var odds = encounter.AttackerOdds;
        var sides = new StringBuilder();
        sides.AppendLine($"Attacker:\n{Briefs.Character(story, attacker)}");
        sides.AppendLine(world.Characters.TryGetValue(encounter.DefenderId, out var defender)
            ? $"\nDefender:\n{Briefs.Character(story, defender)}"
            : $"\nAgainst: the perils of the wilds ({(encounter.Kind == Encounters.Hunt ? "monsters in their lairs" : "a labyrinth's guardians and traps")})");
        var outcomes = new StringBuilder();
        foreach (var o in allowed)
            outcomes.AppendLine($"- {o.Key}: attacker {(o.AttackerWins ? "prevails" : "fails")}; attacker {o.AttackerFate.ToString().ToLowerInvariant()}, "
                                + $"defender {o.DefenderFate.ToString().ToLowerInvariant()} (chance {(o.Probability * 100).ToString("0", CultureInfo.InvariantCulture)}%)");
        var name = PromptText.Data(Names.Full(world, attacker));
        var prompt = PromptText.Section("brief",
                $"Date: {world.Date}\nEncounter: {encounter.Kind}{(encounter.ToTheDeath ? ", to the death" : ", to first blood or yielding")}\n"
                + $"{sides}\nThe attacker's odds of prevailing: {(odds * 100).ToString("0", CultureInfo.InvariantCulture)}%\n\nPossible outcomes:\n{outcomes}")
            + (tight ? "\n\nUpsets have been frequent of late; let the likelier outcome stand unless the brief makes it ring false." : "")
            + $"\n\nDecide how {name}'s {encounter.Kind} ends. Name its turning point and give a short account (two or three sentences).";
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                outcome = new { type = "string", @enum = keys },
                turning_point = new { type = "string" },
                account = new { type = "string" },
            },
            required = new[] { "outcome", "turning_point", "account" },
            additionalProperties = false,
        });
        var request = new LlmRequest(ModelTier.Arbitrate, key, story.Bible, prompt, schema);

        bool Upset(EncounterOutcome o) => o.AttackerWins != (odds >= 0.5);
        var json = story.Gateway.Ask(request, day, answer => Parse(answer, keys) is not null,
            answer => Upset(allowed.First(o => o.Key == Parse(answer, keys)!.Outcome)) ? "upset" : "");
        if (json is null || Parse(json, keys) is not { } chosen) return fallback;
        var account = string.IsNullOrWhiteSpace(chosen.TurningPoint) ? chosen.Account : $"{chosen.Account} Turning point: {chosen.TurningPoint}";
        return allowed.First(o => o.Key == chosen.Outcome) with { Account = PromptText.Data(account, 500) };
    }

    /// <summary>Underdog wins among the latest answered arbitrations, read from the journal so it survives reloads.</summary>
    private bool DramaSpent() =>
        story.Gateway.Journal.All.Where(c => c.Tier == ModelTier.Arbitrate && c.Json.Length > 0 && c.Day <= story.World.Date.Day)
            .OrderByDescending(c => c.Day).ThenBy(c => c.Hash, StringComparer.Ordinal)
            .Take(DramaWindow).Count(c => c.Note == "upset") >= DramaBudget;

    private sealed record Answer(string Outcome, string TurningPoint, string Account);

    private static Answer? Parse(string json, IReadOnlyList<string> keys)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var outcome)
                || outcome.ValueKind != JsonValueKind.String || !keys.Contains(outcome.GetString()!)) return null;
            string Text(string property) => root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            return new Answer(outcome.GetString()!, Text("turning_point"), Text("account"));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

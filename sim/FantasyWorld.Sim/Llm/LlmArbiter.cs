using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Arbitration by LLM (spec §8.3) for duels, hunts and delves; battle fates stay with the rules (too many per
/// battle for the budget). The rules roll is always drawn first for RNG parity. The LLM sees both sides and the
/// odds, and may pick only a plausible outcome (base probability at least the floor). Drama budget: when underdogs
/// won 4 of the last 20 arbitrations, only outcomes of 15% or more are offered until upsets thin out. The engine
/// applies the chosen outcome; the LLM's account is stored on the event. A duel is decided whole (sub-project 8 §5): its
/// outcome, how many days it lasts (up to <see cref="MaxDuelDays"/>) and its course in dated chapters.
/// </summary>
public sealed partial class LlmArbiter(StoryContext story, IArbiter? rules = null) : IArbiter
{
    public const int DramaWindow = 20;
    public const int DramaBudget = 4;
    public const double TightFloor = 0.15;

    /// <summary>A duel may drag on through bouts and truces for two years; longer, and it stops being news.</summary>
    /// <summary>
    /// A duel is over in a day (the owner, 2026-10-29); only two truly mighty fighters (<see cref="Mighty"/>) may draw it out
    /// to two or three. A war between nations may run on for years; a duel never.
    /// </summary>
    public const int MaxDuelDays = 3;

    /// <summary>A fighter truly strong: awakened, or a renowned master of arms.</summary>
    public static bool Mighty(Character c) => Classes.IsAwakened(c.Class) || (c.Attributes.Prowess >= 16 && c.Renown >= 40);

    /// <summary>How long a duel between these two may last: three days for two mighty fighters, else one.</summary>
    public static int DuelDays(Character a, Character? d) => d is not null && Mighty(a) && Mighty(d) ? MaxDuelDays : 1;

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
        var brief = $"Date: {world.Date}\nEncounter: {encounter.Kind}{(encounter.ToTheDeath ? ", to the death" : ", to first blood or yielding")}\n"
                    + $"{sides}\nThe attacker's odds of prevailing: {(odds * 100).ToString("0", CultureInfo.InvariantCulture)}%\n\nPossible outcomes:\n{outcomes}";
        var whole = encounter.Kind == Encounters.Duel;
        // A hidden power breaking out mid-fight is this fight's twist (the owner, 2026-10-29).
        if (encounter.Surged != 0 && world.Characters.TryGetValue(encounter.Surged, out var surging)
            && world.Callings.FirstOrDefault(k => Classes.Awaken(k.Base, k.Name) == surging.Class) is { } power)
            brief += $"\nIn the middle of this fight, a power hidden within {PromptText.Data(surging.Name)} breaks out: they awaken as a "
                     + $"{PromptText.Data(power.Name)} ({PromptText.Data(power.Origin, 300)} It {PromptText.Data(power.Does, 200)}; but {PromptText.Data(power.Condition, 200)}). "
                     + "Make it the fight's turning point: it turns the tide, or, if they still lose, it is how they escape with their life.\n";
        // Those at their sides (the owner, 2026-10-30): Claude decides what becomes of each.
        var fellows = encounter.Fellows;
        if (fellows.Count > 0)
            brief += "\nAt their sides:\n" + string.Join("\n", fellows.Select(f =>
                $"- {f.Key}: {PromptText.Data(f.Name)}, {PromptText.Data(f.What, 200)} (with the {(f.WithAttacker ? "attacker" : "defender")})")) + "\n";
        var fellowKeys = fellows.Select(f => f.Key).ToList();
        var fates = Enum.GetValues<FellowFate>().Select(Fellows.Word).ToArray();
        var fellowAsk = fellows.Count == 0 ? ""
            : " Decide too what becomes of each one at their sides: unharmed, injured, incapacitated (out of the fight for a long while), "
              + "disabled (lasting harm), separated (lost from their side) or dead; those on the losing side fare worse, and none is safe.";
        var longest = DuelDays(attacker, defender);
        var prompt = PromptText.Section("brief", brief)
            + (tight ? "\n\nUpsets have been frequent of late; let the likelier outcome stand unless the brief makes it ring false." : "")
            + (whole
                ? $"\n\nDecide {name}'s duel whole: which of the possible outcomes it comes to, and how. {FightAnswer.Instructions(longest, odds)}"
                : $"\n\nDecide how {name}'s {encounter.Kind} ends. Name its turning point and give a short account (two or three sentences).")
            + fellowAsk;
        var fellowSchema = new
        {
            type = "array",
            items = new
            {
                type = "object",
                properties = new { key = new { type = "string", @enum = fellowKeys }, fate = new { type = "string", @enum = fates } },
                required = new[] { "key", "fate" },
                additionalProperties = false,
            },
        };
        var schema = JsonSerializer.Serialize((whole, fellows.Count > 0) switch
        {
            (true, false) => new
            {
                type = "object",
                properties = (object)new
                {
                    outcome = new { type = "string", @enum = keys },
                    days = FightAnswer.DaysSchema,
                    chapters = FightAnswer.ChaptersSchema,
                },
                required = new[] { "outcome", "days", "chapters" },
                additionalProperties = false,
            },
            (true, true) => new
            {
                type = "object",
                properties = (object)new
                {
                    outcome = new { type = "string", @enum = keys },
                    days = FightAnswer.DaysSchema,
                    chapters = FightAnswer.ChaptersSchema,
                    fellows = fellowSchema,
                },
                required = new[] { "outcome", "days", "chapters", "fellows" },
                additionalProperties = false,
            },
            (false, false) => new
            {
                type = "object",
                properties = (object)new
                {
                    outcome = new { type = "string", @enum = keys },
                    turning_point = new { type = "string" },
                    account = new { type = "string" },
                },
                required = new[] { "outcome", "turning_point", "account" },
                additionalProperties = false,
            },
            _ => new
            {
                type = "object",
                properties = (object)new
                {
                    outcome = new { type = "string", @enum = keys },
                    turning_point = new { type = "string" },
                    account = new { type = "string" },
                    fellows = fellowSchema,
                },
                required = new[] { "outcome", "turning_point", "account", "fellows" },
                additionalProperties = false,
            },
        });
        var request = new LlmRequest(ModelTier.Arbitrate, key, story.Bible, prompt, schema);

        bool Upset(EncounterOutcome o) => o.AttackerWins != (odds >= 0.5);
        bool AttackerWins(string key) => allowed.First(o => o.Key == key).AttackerWins;
        string Why(string answer) => string.Join("; ", Problems(answer, keys, whole, brief, odds, AttackerWins, longest));
        var json = story.Gateway.Ask(request, day, answer => Problems(answer, keys, whole, brief, odds, AttackerWins, longest).Count == 0,
            answer => Upset(allowed.First(o => o.Key == Parse(answer, keys)!.Outcome)) ? "upset" : "", Why);
        if (json is null || Parse(json, keys) is not { } chosen) return fallback;
        var fellowFates = FellowFates(json, fellowKeys);
        if (chosen.Chapters.Count > 0)
            return allowed.First(o => o.Key == chosen.Outcome) with
            {
                Days = Math.Clamp(chosen.Days, 1, longest),
                Chapters = chosen.Chapters,
                Account = PromptText.Data(string.Join(" ", chosen.Chapters.Select(c => c.Text)), 1500),
                FellowFates = fellowFates,
            };
        var account = string.IsNullOrWhiteSpace(chosen.TurningPoint) ? chosen.Account : $"{chosen.Account} Turning point: {chosen.TurningPoint}";
        return allowed.First(o => o.Key == chosen.Outcome) with { Account = PromptText.Data(account, 500), FellowFates = fellowFates };
    }

    /// <summary>Underdog wins among the latest answered arbitrations, read from the journal so it survives reloads.</summary>
    private bool DramaSpent() =>
        story.Gateway.Journal.All.Where(c => c.Tier == ModelTier.Arbitrate && c.Json.Length > 0 && c.Day <= story.World.Date.Day)
            .OrderByDescending(c => c.Day).ThenBy(c => c.Hash, StringComparer.Ordinal)
            .Take(DramaWindow).Count(c => c.Note == "upset") >= DramaBudget;

    private sealed record Answer(string Outcome, string TurningPoint, string Account, int Days, IReadOnlyList<FightChapter> Chapters);

    /// <summary>Why a new answer is refused: no legal outcome, or (for a whole fight) a length or course out of bounds.</summary>
    private static IReadOnlyList<string> Problems(string json, IReadOnlyList<string> keys, bool whole, string brief, double odds, Func<string, bool> attackerWins, int longest)
    {
        if (Parse(json, keys) is not { } answer) return ["the outcome must be one of the possible outcomes"];
        if (!whole) return [];
        using var doc = JsonDocument.Parse(json);
        var problems = FightAnswer.Problems(doc.RootElement, longest, brief).ToList();
        if (FightAnswer.Swift(doc.RootElement, odds, attackerWins(answer.Outcome)) is { } slow) problems.Add(slow);
        return problems;
    }

    /// <summary>The fates Claude chose for those at the fighters' sides; any it left out, or named wrongly, the rules roll.</summary>
    private static IReadOnlyDictionary<string, FellowFate> FellowFates(string json, IReadOnlyList<string> keys)
    {
        var fates = new Dictionary<string, FellowFate>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("fellows", out var list) || list.ValueKind != JsonValueKind.Array)
                return fates;
            foreach (var item in list.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String && keys.Contains(k.GetString()!)
                    && item.TryGetProperty("fate", out var f) && f.ValueKind == JsonValueKind.String && Fellows.Parse(f.GetString()!) is { } fate)
                    fates.TryAdd(k.GetString()!, fate);
        }
        catch (JsonException)
        {
        }
        return fates;
    }

    /// <summary>The answer as it is applied; lenient, so answers journaled before fights had a length still replay.</summary>
    private static Answer? Parse(string json, IReadOnlyList<string> keys)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var outcome)
                || outcome.ValueKind != JsonValueKind.String || !keys.Contains(outcome.GetString()!)) return null;
            string Text(string property) => root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            return new Answer(outcome.GetString()!, Text("turning_point"), Text("account"), FightAnswer.Days(root), FightAnswer.Chapters(root, 400));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

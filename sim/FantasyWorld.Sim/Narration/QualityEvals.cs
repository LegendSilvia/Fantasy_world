using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Narration;

/// <summary>One graded sample: what was graded, the scores (1–5) and the judge's note.</summary>
public sealed record EvalScore(string Subject, string Kind, IReadOnlyDictionary<string, int> Scores, string Note);

/// <summary>
/// A run of the quality evals: the suite, the worlds sampled, the scores, the means per criterion, any statistical
/// checks, what it cost and whether the cost cap stopped it early.
/// </summary>
public sealed record EvalReport(
    string Suite, IReadOnlyList<ulong> Seeds, IReadOnlyList<EvalScore> Scores, IReadOnlyDictionary<string, double> Means,
    IReadOnlyDictionary<string, string> Checks, int Fallbacks, decimal SpendUsd, bool Capped, DateTimeOffset RanUtc)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}

/// <summary>
/// Opt-in quality evals of the LLM voice (sub-project 5 §5, <c>fw eval</c>). Narration: events, chains and a digest
/// from fixed seeds are narrated live and graded by a judge model against a rubric (faithful, clear, in voice, varied).
/// Decisions: choices made for characters are graded for being in character, and arbitrations are checked
/// statistically against their odds over many draws. Every call spends one shared budget, and no call is made once the
/// cap is reached, so a run overshoots its cap by at most one call.
/// </summary>
public static class QualityEvals
{
    public static readonly IReadOnlyList<string> NarrationRubric = new[] { "faithful", "clear", "voice", "varied" };
    public static readonly IReadOnlyList<string> DecisionRubric = new[] { "in_character", "reasoning" };
    public static readonly IReadOnlyList<ulong> DefaultSeeds = new ulong[] { 1, 2, 3 };

    public static EvalReport Narration(ILlmProvider provider, LlmSettings settings, decimal capUsd, int samples, IReadOnlyList<ulong>? seeds = null, TimeProvider? time = null)
    {
        seeds ??= DefaultSeeds;
        var budget = new Budget(provider, capUsd);
        var scores = new List<EvalScore>();
        var fallbacks = 0;
        foreach (var seed in seeds)
        {
            // Each seed has its own gateways and journals: event ids and days repeat across seeds, and a shared journal
            // would replay one world's answer for another's question.
            var (gateway, judge) = Gateways(budget, settings, time);
            var (world, log) = Aged(seed);
            var story = new StoryContext(world, log, gateway);
            var narrator = new LlmNarrator(story);
            var templates = new TemplateNarrator(world);
            var picks = log.All.Where(e => e.Importance >= 30).GroupBy(e => e.Type)
                .Select(g => g.OrderByDescending(e => e.Importance).ThenBy(e => e.Id).First())
                .OrderByDescending(e => e.Importance).ThenBy(e => e.Id).Take(samples).ToList();
            foreach (var ev in picks)
            {
                if (budget.Spent) break;
                var before = gateway.Fallbacks;
                var text = narrator.Narrate(ev);
                if (gateway.Fallbacks > before) { fallbacks++; continue; }
                if (Grade(judge, story, $"seed {seed} event #{ev.Id}", "event", templates.Narrate(ev), text, NarrationRubric) is { } score) scores.Add(score);
            }
            // A chain: the latest war, else the latest important event with causes.
            if ((log.All.LastOrDefault(e => e.Type == "WarDeclared") ?? log.All.LastOrDefault(e => e.Importance >= 30 && e.Causes.Count > 0)) is { } war && !budget.Spent)
            {
                var chain = new HistoryQuery(log).WhyChain(war.Id, 4);
                var before = gateway.Fallbacks;
                var text = narrator.NarrateChain(chain);
                if (gateway.Fallbacks > before) fallbacks++;
                else if (Grade(judge, story, $"seed {seed} chain #{war.Id}", "chain", templates.NarrateChain(chain), text, NarrationRubric) is { } score) scores.Add(score);
            }
            // And the morning digest of the whole history.
            if (!budget.Spent)
            {
                var digest = Chronicle.Digest.Build(log, world, 0, new HashSet<EntityRef>());
                var before = gateway.Fallbacks;
                var text = narrator.Digest(digest);
                if (gateway.Fallbacks > before) fallbacks++;
                else if (Grade(judge, story, $"seed {seed} digest", "digest", templates.Digest(digest), text, NarrationRubric) is { } score) scores.Add(score);
            }
        }
        return Report("narration", seeds, scores, NarrationRubric, new Dictionary<string, string>(), fallbacks, budget, time);
    }

    public static EvalReport Decisions(ILlmProvider provider, LlmSettings settings, decimal capUsd, int draws, IReadOnlyList<ulong>? seeds = null, TimeProvider? time = null)
    {
        seeds ??= DefaultSeeds;
        var budget = new Budget(provider, capUsd);
        var scores = new List<EvalScore>();
        var checks = new Dictionary<string, string>();
        var fallbacks = 0;
        foreach (var seed in seeds)
        {
            var (gateway, judge) = Gateways(budget, settings, time);
            var (world, log) = Aged(seed);
            var story = new StoryContext(world, log, gateway);
            var people = world.ActiveCharacters.Where(c => c.Stage(world.Date) == LifeStage.Adult && c.Traits.Count > 0).OrderBy(c => c.Id).Take(2).ToList();
            if (people.Count < 2 || budget.Spent) continue;
            var (a, b) = (people[0], people[1]);

            // In character: a wrathful or calm, brave or craven notable answers an insult.
            var brief = new DecisionBrief(a.Id, "answering an insult from a rival",
                new[] { new DecisionOption("duel", 1, "challenge them to first blood"), new DecisionOption("forgive", 1, "let it pass"), new DecisionOption("scheme", 1, "plot quietly against them") });
            var choice = new LlmDecider(story).Decide(brief, new Rng(seed));
            // The decider's own question, found by its key (as LlmDecider forms it), not by being the latest call.
            var key = $"decide|{brief.CharacterId}|{brief.Situation}|{world.Date.Day}|{string.Join(",", brief.Options.Select(o => o.Key))}";
            if (!gateway.Journal.TryGet(LlmRequest.HashOf(ModelTier.Decide + "|" + key), out var call)) fallbacks++;
            else if (!budget.Spent
                     && Grade(judge, story, $"seed {seed} char#{a.Id}", "decision",
                         $"{Briefs.Character(story, a)}\nSituation: {brief.Situation}\nOptions: {string.Join("; ", brief.Options.Select(o => $"{o.Key} ({o.Summary})"))}",
                         $"Chose {choice.Key}. {call.Json}", DecisionRubric) is { } score)
                scores.Add(score);

            // Odds respected: many duels between the same two, each on its own day, against the rules' odds.
            var arbiter = new LlmArbiter(story);
            var (wins, expected, variance, n) = (0, 0.0, 0.0, 0);
            for (var i = 0; i < draws && !budget.Spent; i++)
            {
                world.Date = world.Date.AddDays(1);
                var encounter = Encounters.DuelBetween(world, a, b, toTheDeath: false);
                var outcome = arbiter.Resolve(encounter, new Rng(seed * 1000 + (ulong)i));
                var p = encounter.AttackerOdds;
                (wins, expected, variance, n) = (wins + (outcome.AttackerWins ? 1 : 0), expected + p, variance + p * (1 - p), n + 1);
            }
            if (n > 0)
            {
                var z = variance > 0 ? (wins - expected) / Math.Sqrt(variance) : 0;
                checks[$"seed {seed} odds"] = $"{wins} wins in {n} draws, {expected.ToString("0.0", CultureInfo.InvariantCulture)} expected, "
                                              + $"z = {z.ToString("0.00", CultureInfo.InvariantCulture)}: {(Math.Abs(z) <= 3 ? "respected" : "NOT respected")}";
            }
        }
        return Report("decisions", seeds, scores, DecisionRubric, checks, fallbacks, budget, time);
    }

    /// <summary>Writes the report as <c>&lt;suite&gt;-&lt;yyyyMMdd-HHmmss&gt;.json</c> under the directory and returns its path.</summary>
    public static string Save(EvalReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{report.Suite}-{report.RanUtc:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, report.ToJson());
        return path;
    }

    private static (World World, EventLog Log) Aged(ulong seed)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = Prehistory.Run(world, 60);
        return (world, sim.Log);
    }

    /// <summary>
    /// The eval's spending, shared by every gateway it makes: once the cap is reached no further call is made (the
    /// gateway then falls back for the rest of the run), so a run overshoots its cap by at most the one call in flight.
    /// </summary>
    private sealed class Budget(ILlmProvider inner, decimal capUsd) : ILlmProvider
    {
        public decimal SpentUsd { get; private set; }
        public decimal CapUsd => capUsd;
        public bool Spent => SpentUsd >= capUsd;
        public string Name => inner.Name;

        public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
        {
            if (Spent) throw new LlmUnavailableException($"The eval's cap of ${capUsd} is spent.");
            var response = await inner.CompleteJsonAsync(request, model, ct).ConfigureAwait(false);
            SpentUsd += response.CostUsd;
            return response;
        }
    }

    /// <summary>A gateway for the voice under test and one for the judge (its own journal), both spending the eval's budget.</summary>
    private static (LlmGateway Voice, LlmGateway Judge) Gateways(Budget budget, LlmSettings settings, TimeProvider? time)
    {
        var open = settings with { MonthlySpendCapUsd = decimal.MaxValue, StoryBeatsPerDay = int.MaxValue, NarrationsPerDay = int.MaxValue };
        return (new LlmGateway(budget, open, new LlmJournal(), time), new LlmGateway(budget, open, new LlmJournal(), time));
    }

    private static EvalScore? Grade(LlmGateway judge, StoryContext story, string subject, string kind, string brief, string answer, IReadOnlyList<string> rubric)
    {
        var prompt = PromptText.Section("brief", brief) + "\n" + PromptText.Section("answer", answer)
            + $"\n\nYou are grading the answer above against its brief. Score each of {string.Join(", ", rubric)} from 1 (poor) to 5 (excellent): "
            + "faithful means it uses only the brief's facts; clear means a reader follows it at once; voice means it keeps the chronicler's persona and style; "
            + "varied means it avoids stock phrases; in_character means the choice fits the character's traits, memories and situation; reasoning means the "
            + "stated reason is plausible and brief. Give a one-sentence note.";
        var properties = rubric.ToDictionary(r => r, _ => (object)new { type = "integer", minimum = 1, maximum = 5 });
        properties["note"] = new { type = "string" };
        var schema = JsonSerializer.Serialize(new { type = "object", properties, required = rubric.Append("note").ToArray(), additionalProperties = false });
        var request = new LlmRequest(ModelTier.Arbitrate, $"judge|{kind}|{subject}", story.Bible, prompt, schema, MaxTokens: 600);
        var json = judge.Ask(request, story.World.Date.Day, answer => Parse(answer, rubric) is not null);
        return json is null || Parse(json, rubric) is not { } parsed ? null : new EvalScore(subject, kind, parsed.Scores, parsed.Note);
    }

    private static (IReadOnlyDictionary<string, int> Scores, string Note)? Parse(string json, IReadOnlyList<string> rubric)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var scores = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in rubric)
            {
                if (!root.TryGetProperty(r, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var s) || s is < 1 or > 5) return null;
                scores[r] = s;
            }
            return (scores, root.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String ? note.GetString()! : "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EvalReport Report(string suite, IReadOnlyList<ulong> seeds, List<EvalScore> scores, IReadOnlyList<string> rubric,
        Dictionary<string, string> checks, int fallbacks, Budget budget, TimeProvider? time)
    {
        var means = rubric.ToDictionary(r => r, r => scores.Count == 0 ? 0 : Math.Round(scores.Average(s => s.Scores[r]), 2));
        return new EvalReport(suite, seeds, scores, new SortedDictionary<string, double>(means, StringComparer.Ordinal),
            new SortedDictionary<string, string>(checks, StringComparer.Ordinal), fallbacks, budget.SpentUsd, budget.Spent, (time ?? TimeProvider.System).GetUtcNow());
    }
}

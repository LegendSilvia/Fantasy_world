using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FantasyWorld.Cli;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Narration;

public class QualityEvalsTests
{
    /// <summary>
    /// A scripted model: narrates by retelling the brief's first line (faithful), judges every answer the same, decides
    /// the first option, and settles each encounter by a draw against the odds the prompt states (or always for the
    /// defender, to show the odds check catching a biased arbiter).
    /// </summary>
    private sealed class Scripted(bool biased = false, decimal cost = 0, string? narration = null) : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => "scripted";

        public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new LlmResponse(Answer(request), Name, model, 10, 10, 0, 0, cost));
        }

        private string Answer(LlmRequest request)
        {
            var properties = JsonDocument.Parse(request.Schema).RootElement.GetProperty("properties");
            if (request.Key.StartsWith("judge|", StringComparison.Ordinal))
                return string.Join(",", properties.EnumerateObject().Select(p => p.Name == "note" ? "\"note\":\"Fair.\"" : $"\"{p.Name}\":4")) is var body ? $"{{{body}}}" : "";
            if (properties.TryGetProperty("choice", out var choice))
                return $"{{\"choice\":\"{choice.GetProperty("enum")[0].GetString()}\",\"reasoning\":\"Pride.\",\"flavor\":\"\"}}";
            if (properties.TryGetProperty("outcome", out var outcome))
            {
                var keys = outcome.GetProperty("enum").EnumerateArray().Select(k => k.GetString()!).ToList();
                if (biased) return $"{{\"outcome\":\"{keys.First(k => k.StartsWith("defender", StringComparison.Ordinal))}\",\"turning_point\":\"\",\"account\":\"So.\"}}";
                var chances = Regex.Matches(request.Prompt, @"- (\w+): .*\(chance (\d+)%\)").ToDictionary(m => m.Groups[1].Value, m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                var u = Convert.ToUInt32(request.Hash[..8], 16) % 1000 / 1000.0 * chances.Values.Sum(); // a stable draw per question
                var pick = keys.First();
                foreach (var k in keys) { u -= chances.GetValueOrDefault(k); if (u < 0) { pick = k; break; } }
                return $"{{\"outcome\":\"{pick}\",\"turning_point\":\"\",\"account\":\"So.\"}}";
            }
            var brief = Regex.Match(request.Prompt, @"<brief>\n(.*)").Groups[1].Value;
            return JsonSerializer.Serialize(new { text = narration ?? brief.Trim() });
        }
    }

    [Fact]
    public void NarrationEvals_GradeLiveNarration_AndSaveTheReport()
    {
        using var dir = new TempDir();
        var report = QualityEvals.Narration(new Scripted(), new LlmSettings { Provider = "scripted" }, capUsd: 5, samples: 3, seeds: new ulong[] { 1 });
        Assert.NotEmpty(report.Scores);
        Assert.Equal(4.0, report.Means["faithful"]);
        Assert.Contains(report.Scores, s => s.Kind == "chain");
        Assert.Contains(report.Scores, s => s.Kind == "digest");
        var path = QualityEvals.Save(report, dir.Path);
        Assert.Equal("narration", JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("suite").GetString());
    }

    [Fact]
    public void InventingNarration_IsNotGraded_ButCountedAsAFallback()
    {
        var report = QualityEvals.Narration(new Scripted(narration: "Led by the smith Orrin, all rose."), new LlmSettings { Provider = "scripted" }, 5, 3, new ulong[] { 1 });
        Assert.Empty(report.Scores);
        Assert.True(report.Fallbacks > 0);
    }

    [Fact]
    public void AnEval_StopsAtItsCostCap()
    {
        var model = new Scripted(cost: 0.3m);
        var report = QualityEvals.Narration(model, new LlmSettings { Provider = "scripted" }, capUsd: 1, samples: 8, seeds: new ulong[] { 1, 2 });
        Assert.True(report.Capped);
        // No call is made once $1 is spent: four calls of $0.30, overshooting by less than one call.
        Assert.Equal(4, model.Calls);
        Assert.Equal(1.2m, report.SpendUsd);
    }

    [Fact]
    public void DecisionEvals_GradeChoices_AndCatchAnArbiterThatIgnoresTheOdds()
    {
        var fair = QualityEvals.Decisions(new Scripted(), new LlmSettings { Provider = "scripted" }, 5, draws: 60, seeds: new ulong[] { 1 });
        Assert.Contains(fair.Scores, s => s.Kind == "decision" && s.Scores["in_character"] == 4);
        Assert.EndsWith("respected", fair.Checks.Values.Single());
        Assert.DoesNotContain("NOT", fair.Checks.Values.Single());
        var biased = QualityEvals.Decisions(new Scripted(biased: true), new LlmSettings { Provider = "scripted" }, 5, draws: 60, seeds: new ulong[] { 1 });
        Assert.EndsWith("NOT respected", biased.Checks.Values.Single());
    }

    [Fact]
    public void EachSeed_IsAskedItsOwnQuestions()
    {
        // Ids and days repeat across seeds; a shared journal used to replay one world's answers for another's.
        var model = new Scripted();
        var one = QualityEvals.Narration(model, new LlmSettings { Provider = "scripted" }, 5, 3, new ulong[] { 1 });
        var once = model.Calls;
        var twice = QualityEvals.Narration(model, new LlmSettings { Provider = "scripted" }, 5, 3, new ulong[] { 1, 1 });
        Assert.Equal(2 * one.Scores.Count, twice.Scores.Count);
        Assert.Equal(3 * once, model.Calls);
    }

    [Fact]
    public void FwEval_RejectsBadOptions()
    {
        var output = new StringWriter();
        Assert.Equal(1, CliApp.Run(new[] { "eval", "narration", "--model", "judge=x" }, output, () => DateTimeOffset.UnixEpoch, _ => new Scripted()));
        Assert.Equal(1, CliApp.Run(new[] { "eval", "decisions", "--draws", "-3" }, output, () => DateTimeOffset.UnixEpoch, _ => new Scripted()));
    }

    [Fact]
    public void FwEval_NeedsALiveProvider_AndPrintsTheMeans()
    {
        using var dir = new TempDir();
        var output = new StringWriter();
        Assert.Equal(2, CliApp.Run(new[] { "eval", "narration" }, output, () => DateTimeOffset.UnixEpoch, _ => null));
        output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "eval", "narration", "--samples", "2", "--seeds", "1", "--out", dir.Path }, output, () => DateTimeOffset.UnixEpoch, _ => new Scripted()));
        Assert.Contains("faithful 4.00", output.ToString());
        Assert.Single(Directory.GetFiles(dir.Path, "narration-*.json"));
    }
}

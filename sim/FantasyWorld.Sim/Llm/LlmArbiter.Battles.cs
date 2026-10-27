using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// A battle decided whole (sub-project 8 §5): which side wins (one the odds make plausible, the drama budget applying),
/// how many days it lasts (up to <see cref="MaxBattleDays"/>), its course in dated chapters, each side's losses within the
/// rules' ranges and the fates of the commanders and knights present, each one the rules make plausible for their side.
/// </summary>
public sealed partial class LlmArbiter
{
    /// <summary>A battle may become a stand of a whole year; longer, and it is a siege or a war, not a battle.</summary>
    public const int MaxBattleDays = WorldDate.DaysPerYear;

    private static readonly string[] FateNames = ["unharmed", "wounded", "maimed", "slain"];

    public BattlePlan? PlanBattle(BattleStage stage)
    {
        var world = story.World;
        var day = world.Date.Day;
        var key = $"arbitrate|battle|{stage.WarId}|{stage.AttackerArmyId}|{stage.DefenderArmyId}|{day}";
        if (!story.Gateway.MayAnswer(ModelTier.Arbitrate, key)) return null;
        var odds = stage.AttackerOdds;
        var tight = !story.Gateway.Knows(ModelTier.Arbitrate, key) && DramaSpent();
        var floor = tight ? TightFloor : Encounter.PlausibilityFloor;
        var sides = new List<string>();
        if (odds >= floor) sides.Add("attacker_wins");
        if (1 - odds >= floor) sides.Add("defender_wins");
        if (sides.Count == 0) return null;

        var war = world.Wars[stage.WarId];
        var (attacker, defender) = (world.Armies[stage.AttackerArmyId], world.Armies[stage.DefenderArmyId]);
        var region = world.Regions[stage.RegionId];
        // The notables are named n1, n2 … in the brief; each one's fate must be plausible for the side the answer makes win.
        var notables = stage.AttackerNotables.Select(id => (Id: id, Attacker: true)).Concat(stage.DefenderNotables.Select(id => (Id: id, Attacker: false)))
            .Where(n => world.Characters.ContainsKey(n.Id)).Select((n, i) => (Key: $"n{i + 1}", n.Id, n.Attacker)).ToList();
        var present = new StringBuilder();
        foreach (var n in notables)
        {
            var c = world.Characters[n.Id];
            present.AppendLine($"- {n.Key}: {PromptText.Data(Names.Full(world, c))}, with the {(n.Attacker ? "attackers" : "defenders")}; "
                               + $"martial {c.Attributes.Martial}, prowess {c.Attributes.Prowess}{(c.Traits.Count > 0 ? $", {string.Join(", ", c.Traits)}" : "")}");
        }
        string Percent(double p) => (p * 100).ToString("0", CultureInfo.InvariantCulture);
        var brief = $"Date: {world.Date} ({world.Date.Season.ToString().ToLowerInvariant()})\nWar: {PromptText.Data(war.Name)}\n"
                    + $"Field: {PromptText.Data(region.Name)}, {region.Biome.ToString().ToLowerInvariant()}\n\n"
                    + $"Attackers:\n{Briefs.Army(story, attacker, stage.AttackerStrength)}\n\nDefenders:\n{Briefs.Army(story, defender, stage.DefenderStrength)}\n\n"
                    + $"The attackers' odds of winning: {Percent(odds)}%\n"
                    + $"Losses: the victors lose {Percent(MilitarySystem.WinnerLosses.Min)} to {Percent(MilitarySystem.WinnerLosses.Max)} in a hundred of their troops; "
                    + $"the vanquished {Percent(MilitarySystem.LoserLosses.Min)} to {Percent(MilitarySystem.LoserLosses.Max)}\n"
                    + (notables.Count > 0 ? $"\nPresent:\n{present}" + "Victors may come away unharmed or wounded; the vanquished unharmed, wounded, maimed or slain.\n" : "");
        var prompt = PromptText.Section("brief", brief)
            + (tight ? "\n\nUpsets have been frequent of late; let the likelier side win unless the brief makes it ring false." : "")
            + $"\n\nDecide this battle whole: {(sides.Count == 2 ? "which side wins" : "how the side that must win does so")}, each side's losses "
            + "(as how many in a hundred of its troops fell)" + (notables.Count > 0 ? ", the fate of each of those present" : "")
            + $", and how it went. {FightAnswer.Instructions(MaxBattleDays)}";
        var properties = new Dictionary<string, object>
        {
            ["outcome"] = new { type = "string", @enum = sides },
            ["days"] = FightAnswer.DaysSchema,
            ["chapters"] = FightAnswer.ChaptersSchema,
            ["victor_losses"] = new { type = "integer" },
            ["vanquished_losses"] = new { type = "integer" },
        };
        if (notables.Count > 0)
            properties["fates"] = new
            {
                type = "object",
                properties = notables.ToDictionary(n => n.Key, _ => (object)new { type = "string", @enum = FateNames }),
                required = notables.Select(n => n.Key).ToArray(),
                additionalProperties = false,
            };
        var schema = JsonSerializer.Serialize(new { type = "object", properties, required = properties.Keys.ToArray(), additionalProperties = false });
        var request = new LlmRequest(ModelTier.Arbitrate, key, story.Bible, prompt, schema);

        IReadOnlyList<string> Problems(string json)
        {
            if (Read(json, sides, notables) is not { } plan) return ["the outcome must be one of the allowed sides"];
            var problems = new List<string>();
            using (var doc = JsonDocument.Parse(json)) problems.AddRange(FightAnswer.Problems(doc.RootElement, MaxBattleDays, brief));
            if (!MilitarySystem.WinnerLosses.Holds(plan.WinnerLosses)) problems.Add("the victors' losses are out of their range");
            if (!MilitarySystem.LoserLosses.Holds(plan.LoserLosses)) problems.Add("the vanquished's losses are out of their range");
            foreach (var n in notables)
                if (!plan.Fates.TryGetValue(n.Id, out var fate)) problems.Add($"{n.Key} has no fate");
                else if (!MilitarySystem.BattleFate(world.Characters[n.Id], n.Attacker == plan.AttackerWins).Outcomes
                             .Any(o => o.AttackerFate == fate && o.Probability >= Encounter.PlausibilityFloor))
                    problems.Add($"{n.Key}'s fate is not one their side's lot allows");
            return problems;
        }
        bool Upset(BattlePlan plan) => plan.AttackerWins != (odds >= 0.5);
        var json = story.Gateway.Ask(request, day, answer => Problems(answer).Count == 0,
            answer => Upset(Read(answer, sides, notables)!) ? "upset" : "", answer => string.Join("; ", Problems(answer)));
        return json is null ? null : Read(json, sides, notables);
    }

    /// <summary>The answer as a plan, its losses as shares; null when it names no allowed side.</summary>
    private static BattlePlan? Read(string json, IReadOnlyList<string> sides, IReadOnlyList<(string Key, long Id, bool Attacker)> notables)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var outcome) || outcome.ValueKind != JsonValueKind.String
                || !sides.Contains(outcome.GetString()!)) return null;
            double Share(string property) =>
                root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n / 100.0 : -1;
            var fates = new Dictionary<long, Fate>();
            if (root.TryGetProperty("fates", out var given) && given.ValueKind == JsonValueKind.Object)
                foreach (var n in notables)
                    if (given.TryGetProperty(n.Key, out var f) && f.ValueKind == JsonValueKind.String && Array.IndexOf(FateNames, f.GetString()) is var i and >= 0)
                        fates[n.Id] = (Fate)i;
            return new BattlePlan(outcome.GetString() == "attacker_wins", FightAnswer.Days(root), FightAnswer.Chapters(root, 400),
                Share("victor_losses"), Share("vanquished_losses"), fates);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// The parts every whole-fight answer shares (sub-project 8 §5): how many days the fight lasts and its course in two to
/// five dated chapters, the last on its last day, each held by the narration guard to the names and numbers of the brief.
/// </summary>
public static class FightAnswer
{
    public const int MinChapters = 2;
    public const int MaxChapters = 5;

    /// <summary>The schema of the shared parts, to merge into a fight's answer schema (no bounds: the API rejects them; they are checked here).</summary>
    public static object DaysSchema => new { type = "integer" };

    public static object ChaptersSchema => new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new { day = new { type = "integer" }, text = new { type = "string" } },
            required = new[] { "day", "text" },
            additionalProperties = false,
        },
    };

    /// <summary>The instructions for the shared parts.</summary>
    public static string Instructions(int maxDays) =>
        $"Decide how long it lasts: a single day, or longer, up to {maxDays} days, as the two sides would make it. Tell its course in "
        + $"{MinChapters} to {MaxChapters} chapters, each on a day of the fight (day 1 is the first; the last chapter falls on its last day), "
        + "with at least one turning point. Name only those the brief names, and write no numbers in the chapters.";

    /// <summary>What is wrong with the shared parts of an answer, if anything.</summary>
    public static IReadOnlyList<string> Problems(JsonElement root, int maxDays, string brief)
    {
        var problems = new List<string>();
        if (!root.TryGetProperty("days", out var d) || d.ValueKind != JsonValueKind.Number || !d.TryGetInt32(out var days) || days < 1 || days > maxDays)
        {
            problems.Add($"days must be a whole number from 1 to {maxDays}");
            return problems;
        }
        var chapters = Chapters(root);
        if (chapters.Count is < MinChapters or > MaxChapters) problems.Add($"there must be {MinChapters} to {MaxChapters} chapters");
        for (var i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].Day < 1 || chapters[i].Day > days || i > 0 && chapters[i].Day < chapters[i - 1].Day)
                problems.Add("each chapter's day must fall within the fight, in order");
            if (string.IsNullOrWhiteSpace(chapters[i].Text)) problems.Add("a chapter has no text");
            problems.AddRange(NarrationGuard.Problems(chapters[i].Text, brief));
        }
        if (chapters.Count > 0 && chapters[^1].Day != days) problems.Add("the last chapter must fall on the fight's last day");
        return problems.Distinct().ToList();
    }

    /// <summary>The days, 1 when the answer has none (an answer journaled before fights had a length).</summary>
    public static int Days(JsonElement root) =>
        root.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var days) && days >= 1 ? days : 1;

    public static IReadOnlyList<FightChapter> Chapters(JsonElement root, int maxText = int.MaxValue)
    {
        if (!root.TryGetProperty("chapters", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object)
            .Select(c => new FightChapter(
                c.TryGetProperty("day", out var day) && day.ValueKind == JsonValueKind.Number && day.TryGetInt32(out var n) ? n : 0,
                c.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                    ? maxText == int.MaxValue ? text.GetString()! : PromptText.Data(text.GetString(), maxText) : ""))
            .ToList();
    }

}

using System.Globalization;
using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;

namespace FantasyWorld.Sim.Chronicle;

public sealed record CompactionResult(IReadOnlySet<long> Forgotten, IReadOnlyList<WorldEvent> Summaries);

/// <summary>
/// Spec §7 "Compaction": minor events older than 50 years are rolled into one summary event per decade, except any
/// event that a retained event cites as a cause or that the world's state still refers to (memories, provenance,
/// conditions, wars, treaties, scheduled actions…). History stays explainable; the log stops growing without bound.
/// Compaction runs inside the simulation (<see cref="ChronicleSystem"/>), a decade at a time, so a world is the same
/// however often it is loaded, saved or caught up.
/// </summary>
public static class Compactor
{
    public const int AgeYears = 50;
    public const int MinorBelow = 30;
    public const string SummaryType = "ChronicleCompacted";

    /// <summary>Compacts the minor events of years <paramref name="firstYear"/>–<paramref name="lastYear"/> into one summary.</summary>
    public static CompactionResult Compact(World world, EventLog log, EventQueue queue, int firstYear, int lastYear)
    {
        if (lastYear > world.Date.Year - AgeYears - 1)
            throw new ArgumentOutOfRangeException(nameof(lastYear), $"Year {lastYear} is not yet {AgeYears} years old.");
        var keep = References(world, queue);
        var forget = new HashSet<long>();
        // Newest first, so every retained event protects its causes before they are visited.
        for (var i = log.All.Count - 1; i >= 0; i--)
        {
            var ev = log.All[i];
            var year = ev.Date.Year;
            if (keep.Contains(ev.Id) || ev.Importance >= MinorBelow || year < firstYear || year > lastYear || ev.Type == SummaryType)
                foreach (var cause in ev.Causes) keep.Add(cause.EventId);
            else forget.Add(ev.Id);
        }
        if (forget.Count == 0) return new CompactionResult(forget, Array.Empty<WorldEvent>());

        var gone = log.All.Where(e => forget.Contains(e.Id)).ToList();
        log.Forget(forget);
        var summary = log.Append(world.Date, new EventDraft(SummaryType,
            Payload: new Dictionary<string, string>
            {
                ["period"] = $"Year {firstYear} to Year {lastYear}",
                ["events"] = gone.Count.ToString(CultureInfo.InvariantCulture),
                ["most common"] = string.Join(", ", gone.GroupBy(e => e.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Take(5).Select(g => $"{g.Count()} {g.Key}")),
            },
            Importance: 0, Tags: new[] { "Chronicle" }));
        return new CompactionResult(forget, new[] { summary });
    }

    /// <summary>Every event id the world's state refers to, found by walking its snapshot.</summary>
    public static HashSet<long> References(World world, EventQueue queue)
    {
        var ids = new HashSet<long>();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(WorldSnapshot.Capture(world, queue), SimJson.Options));
        Walk(doc.RootElement, null, ids);
        return ids;
    }

    private static void Walk(JsonElement element, string? name, HashSet<long> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "Memory" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        // Region.Memory: event ids the region remembers.
                        foreach (var item in property.Value.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Number) ids.Add(item.GetInt64());
                    }
                    else if (property.Name is "Conditions" or "Markers" && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var value in property.Value.EnumerateObject())
                            if (value.Value.TryGetInt64(out var id)) ids.Add(id);
                    }
                    else if (name == "Data" && property.Name == "event" && long.TryParse(property.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var queued))
                        ids.Add(queued);
                    else if (property.Name.EndsWith("EventId", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Number)
                        ids.Add(property.Value.GetInt64());
                    else Walk(property.Value, property.Name, ids);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, name, ids);
                break;
        }
    }
}

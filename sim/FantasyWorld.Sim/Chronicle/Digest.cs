using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Chronicle;

public sealed record DigestEntry(WorldEvent Event, int Score);

/// <summary>A labelled stretch of the digest ("Spring of Year 12"), or the whole span when it is short.</summary>
public sealed record DigestGroup(string Label, IReadOnlyList<DigestEntry> Entries);

public sealed record DigestView(WorldDate From, WorldDate To, int EventCount, IReadOnlyList<DigestGroup> Groups)
{
    public IEnumerable<DigestEntry> Entries => Groups.SelectMany(g => g.Entries);
}

/// <summary>
/// The morning digest (spec §7, §11 criterion 2): the most important events since the reader last looked, ranked
/// by <see cref="Importance"/>, and grouped by season when the absence spans more than a year.
/// </summary>
public static class Digest
{
    public const int DefaultTake = 8;

    public static DigestView Build(EventLog log, World world, long sinceEventId, IReadOnlySet<EntityRef> followed, int take = DefaultTake)
    {
        var events = log.Since(sinceEventId).ToList();
        if (events.Count == 0) return new DigestView(world.Date, world.Date, 0, Array.Empty<DigestGroup>());
        var from = events[0].Date;
        var to = events[^1].Date;
        var spansYears = to.Day - from.Day > WorldDate.DaysPerYear;
        var perGroup = spansYears ? Math.Max(1, take / 4) : take;

        var groups = events
            .Select(e => new DigestEntry(e, Importance.Score(e, world, followed)))
            .GroupBy(e => spansYears ? SeasonLabel(e.Event.Date) : "")
            .Select(g => (Label: g.Key, First: g.Min(e => e.Event.Id), Best: g.OrderByDescending(e => e.Score).ThenBy(e => e.Event.Id).Take(perGroup).ToList()))
            .ToList();
        if (spansYears)
        {
            // Keep the seasons that hold the most important events, in calendar order.
            var keep = groups.OrderByDescending(g => g.Best[0].Score).ThenBy(g => g.First).Take(Math.Max(1, take / perGroup))
                .Select(g => g.Label).ToHashSet(StringComparer.Ordinal);
            groups = groups.Where(g => keep.Contains(g.Label)).ToList();
        }
        return new DigestView(from, to, events.Count, groups
            .OrderBy(g => g.First)
            .Select(g => new DigestGroup(g.Label, g.Best.OrderBy(e => e.Event.Id).ToList()))
            .ToList());
    }

    public static string SeasonLabel(WorldDate date) => $"{date.Season} of Year {date.Year}";
}

using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Views;

public sealed record DigestPin(int Number, EntityRef Place, string Label, MapPoint Position, long EventId);

public sealed record Headline(int Number, Category Category, string Kicker, LinkedText Title, LinkedText Body, long EventId, bool CanExplain);

public sealed record DigestItem(int Number, Category Category, LinkedText Text, long EventId);

/// <summary>
/// The *Morning digest* screen (sub-project 2 §3.1): the digest's events numbered by importance (1 = the headline),
/// map pins for those with a place, a title and subtitle for the time away, and the catch-up status line.
/// </summary>
public sealed record DigestScreen(
    string WorldName, int Year, string Title, string Subtitle, long ElapsedDays, string Status,
    IReadOnlyList<DigestPin> Pins, Headline? Headline, IReadOnlyList<DigestItem> Items)
{
    private static readonly string[] Numbers = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    public static DigestScreen Build(World world, EventLog log, DigestView digest, WorldDate since, long daysBehind = 0, INarrator? narrator = null)
    {
        var templates = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        var name = DisplayName(world);
        var ranked = digest.Entries.OrderByDescending(e => e.Score).ThenBy(e => e.Event.Id).Select((e, i) => (Number: i + 1, e.Event)).ToList();
        LinkedText Link(string text, WorldEvent ev) => LinkedText.Link(text, LinkedText.Mentions(ev), nameOf);

        var pins = ranked.Where(r => r.Event.Place is { } p && MapPoint.Of(world, p) is not null)
            .DistinctBy(r => r.Event.Place!.Value).Take(5)
            .Select(r => new DigestPin(r.Number, r.Event.Place!.Value, nameOf(r.Event.Place!.Value) ?? r.Event.Place!.Value.ToString(),
                MapPoint.Of(world, r.Event.Place!.Value)!, r.Event.Id))
            .ToList();
        Headline? headline = null;
        if (ranked.Count > 0)
        {
            var top = ranked[0].Event;
            var body = narrator is null ? Note(top) : narrator.Narrate(top);
            headline = new Headline(1, Categories.Of(top), Categories.Kicker(top), Link(templates.Sentence(top), top), Link(body, top), top.Id, top.Causes.Count > 0);
        }
        var items = ranked.Skip(1).Select(r => new DigestItem(r.Number, Categories.Of(r.Event), Link(templates.Sentence(r.Event), r.Event), r.Event.Id)).ToList();

        var elapsed = Math.Max(0, world.Date.Day - since.Day);
        var subtitle = elapsed == 0
            ? $"Nothing has passed since you last looked at {name}."
            : $"While you were away, {Span(elapsed)} passed in {name}. {Seasons(since, world.Date)}, and {Count(digest.EventCount)} recorded.";
        return new DigestScreen(name, world.Date.Year, $"Year {world.Date.Year}.", subtitle, elapsed, StatusLine(world.Date, daysBehind), pins, headline, items);
    }

    /// <summary>"Caught up · Year 312, day 196", or "Catching up · 12 days behind" while real time is still owed.</summary>
    public static string StatusLine(WorldDate date, long daysBehind) => daysBehind > 0
        ? $"Catching up · {daysBehind.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} day{(daysBehind == 1 ? "" : "s")} behind"
        : $"Caught up · Year {date.Year}, day {(date.Month - 1) * WorldDate.DaysPerMonth + date.DayOfMonth}";

    /// <summary>The digest a returning reader sees: everything since they last looked, without marking it seen.</summary>
    public static DigestScreen ForReader(World world, EventLog log, IReadOnlySet<EntityRef> followed, long daysBehind = 0, INarrator? narrator = null)
    {
        var since = log.TryGet(world.LastSeenEventId, out var seen) ? seen.Date : new WorldDate(0);
        return Build(world, log, Digest.Build(log, world, world.LastSeenEventId, followed), since, daysBehind, narrator);
    }

    public static string DisplayName(World world) => world.Name.Length > 0 ? world.Name : "The World";

    /// <summary>What the engine knows of why or how: motive, account or lore, else when it happened.</summary>
    private static string Note(WorldEvent ev) =>
        ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("lore")
        ?? $"{Digest.SeasonLabel(ev.Date)}.";

    private static string Word(long n) => n < Numbers.Length ? Numbers[n] : n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Span(long days) =>
        days < WorldDate.DaysPerMonth ? Plural(days, "day")
        : days < WorldDate.DaysPerYear ? Plural(days / WorldDate.DaysPerMonth, "month")
        : Plural(days / WorldDate.DaysPerYear, "year");

    private static string Plural(long n, string unit) => $"{Word(n)} {unit}{(n == 1 ? "" : "s")}";

    private static string Count(int events) => events == 1 ? "one event was" : $"{Word(events)} events were";

    private static string Seasons(WorldDate from, WorldDate to) =>
        to.Day - from.Day >= WorldDate.DaysPerYear || from.Season == to.Season && to.Day - from.Day >= 90 ? "The seasons turned"
        : from.Season == to.Season ? $"It is still {to.Season.ToString().ToLowerInvariant()}"
        : $"{from.Season} gave way to {to.Season.ToString().ToLowerInvariant()}";
}

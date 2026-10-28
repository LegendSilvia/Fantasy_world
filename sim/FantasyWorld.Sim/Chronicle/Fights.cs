using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>One day of a fight as it was fought (the account Claude, or the rules, gave it), for the fight's section.</summary>
public sealed record FightBeat(int Day, string Text);

/// <summary>
/// A fight told as one story (sub-project 8 B3): a duel or a battle that lasted days is begun (DuelBegun, BattleJoined),
/// turns on its chapters' days (DuelTurned, BattleTurned) and ends (DuelFought, BattleFought, or called or broken off),
/// each later event caused by the first. An episode tells the fight once, in the section of whichever of its events it
/// tells, with every day's account it has.
/// </summary>
public static class Fights
{
    public static readonly IReadOnlySet<string> Starts = new HashSet<string>(StringComparer.Ordinal) { "DuelBegun", "BattleJoined" };

    public static readonly IReadOnlySet<string> Events = new HashSet<string>(StringComparer.Ordinal)
    {
        "DuelBegun", "DuelTurned", "DuelFought", "DuelCalledOff", "BattleJoined", "BattleTurned", "BattleFought", "BattleBrokenOff",
    };

    /// <summary>At most this many days of a fight are read aloud in the template's words; all go to the chronicler.</summary>
    public const int BeatsTold = 3;

    /// <summary>The event a fight began with (itself for a one-day fight); 0 if the event is not a fight's.</summary>
    public static long RootOf(EventLog log, WorldEvent ev)
    {
        if (!Events.Contains(ev.Type)) return 0;
        if (Starts.Contains(ev.Type)) return ev.Id;
        return ev.Causes.Where(c => c.Kind == CauseKind.Trigger).Select(c => log.TryGet(c.EventId, out var cause) ? cause : null)
            .FirstOrDefault(c => c is not null && Starts.Contains(c.Type))?.Id ?? ev.Id;
    }

    /// <summary>The fight's days as told so far, in order: its first day, each turn, its end; days without an account are left out.</summary>
    public static IReadOnlyList<FightBeat> Beats(EventLog log, WorldEvent ev)
    {
        var root = RootOf(log, ev);
        if (root == 0 || !log.TryGet(root, out var first)) return [];
        var events = new List<WorldEvent> { first };
        if (Starts.Contains(first.Type))
            events.AddRange(log.Since(root).Where(e => Events.Contains(e.Type) && e.Causes.Any(c => c.Kind == CauseKind.Trigger && c.EventId == root)));
        return events.Select(e => (Event: e, Account: e.Payload.GetValueOrDefault("account")))
            .Where(x => x.Account is { Length: > 0 })
            .Select(x => new FightBeat(Day(x.Event), x.Account!))
            .ToList();
    }

    private static int Day(WorldEvent ev) =>
        Starts.Contains(ev.Type) ? 1
        : int.TryParse(ev.Payload.GetValueOrDefault("day") ?? ev.Payload.GetValueOrDefault("days"), out var day) ? day
        : 1;

    /// <summary>A few of the fight's days as the template reads them: the first, the last, and the one between if there are more.</summary>
    public static string Tell(IReadOnlyList<FightBeat> beats)
    {
        if (beats.Count < 2) return "";
        var told = beats.Count <= BeatsTold ? beats : [beats[0], beats[beats.Count / 2], beats[^1]];
        return "How it went: " + string.Join(" ", told.Select(b => b.Text));
    }

    /// <summary>The fight's days as a brief lists them, one a line.</summary>
    public static string Brief(IReadOnlyList<FightBeat> beats) => string.Join("\n", beats.Select(b => $"Day {b.Day}: {b.Text}"));
}

using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// One move of a campaign as an episode shows it (sub-project 10 C3): the move's event, its line, where the host came from
/// and where it went (map points, 0–1), its side (attacker or defender, for the arrow's colour) and whether it crossed into
/// the enemy's land. Only a host's last move shown carries a line, its whole march in brief (the owner, 2026-10-29: how
/// long and from where to where, not every town on the way); the others are arrows alone.
/// </summary>
public sealed record CampaignMove(long EventId, string Line, MapPoint From, MapPoint To, string Side, bool Invasion);

/// <summary>A war's moves within an episode, for the section that tells the war.</summary>
public static class Campaign
{
    /// <summary>At most this many moves a war's section shows; crossings into enemy land first, then the latest.</summary>
    public const int MovesPerWar = 6;

    /// <summary>The war an event belongs to, if it names one.</summary>
    public static long WarOf(WorldEvent ev) => ev.Actors.TryGetValue("war", out var war) && war.Kind == War.Kind ? war.Id : 0;

    /// <summary>
    /// The war's moves among <paramref name="events"/>, in the order they were made; each host's last one shown carries its
    /// march in brief (<see cref="March"/>), counted over all its moves in the episode.
    /// </summary>
    public static IReadOnlyList<CampaignMove> Moves(World world, IEnumerable<WorldEvent> events, long warId, TemplateNarrator narrator)
    {
        if (warId == 0 || !world.Wars.TryGetValue(warId, out var war)) return [];
        var moves = events.Where(e => e.Type == "ArmyMoved" && WarOf(e) == warId).OrderBy(e => e.Id).ToList();
        var shown = moves.OrderByDescending(e => e.Importance > 20).ThenByDescending(e => e.Id).Take(MovesPerWar).OrderBy(e => e.Id).ToList();
        var lastShown = shown.GroupBy(HostOf).Select(g => g.Last().Id).ToHashSet();
        return shown.Select(e => Move(world, war, e, lastShown.Contains(e.Id) ? March(moves.Where(m => HostOf(m) == HostOf(e)).ToList(), narrator) : ""))
            .OfType<CampaignMove>().ToList();
    }

    private static long HostOf(WorldEvent ev) => ev.Actors.TryGetValue("army", out var army) ? army.Id : 0;

    private static CampaignMove? Move(World world, War war, WorldEvent ev, string line)
    {
        if (!ev.Actors.TryGetValue("from", out var from) || ev.Place is not { } to) return null;
        if (MapPoint.Of(world, from) is not { } a || MapPoint.Of(world, to) is not { } b) return null;
        var realm = ev.Actors.TryGetValue("realm", out var r) ? r.Id : 0;
        var side = war.Attackers.Contains(realm) || war.AttackerId == realm ? "attacker" : "defender";
        return new CampaignMove(ev.Id, line, a, b, side, ev.Payload.GetValueOrDefault("into") == "enemy" && ev.Importance > 20);
    }

    /// <summary>
    /// One host's march in a sentence: where it set out from, where it stands now and how long it took ("The Host of Vale
    /// marched for three months, from Kesburg into the Mire of Aul."), whatever it passed on the way.
    /// </summary>
    public static string March(IReadOnlyList<WorldEvent> moves, TemplateNarrator narrator)
    {
        if (moves.Count == 0 || !moves[0].Actors.TryGetValue("army", out var army) || !moves[0].Actors.TryGetValue("from", out var start)
            || moves[^1].Place is not { } end || narrator.NameOf(army) is not { } host) return "";
        var days = moves[^1].Date.Day - moves[0].Date.Day;
        var months = (int)Math.Round(days / (double)WorldDate.DaysPerMonth);
        var took = days < WorldDate.DaysPerMonth ? (days <= 1 ? "in a matter of days" : $"in {English.Count(days, "day")}") : $"for {English.Count(Math.Max(1, months), "month")}";
        var (from, to) = (narrator.NameOf(start), narrator.NameOf(end));
        var route = start == end || from == to ? $"out from {from} and back again" : $"from {from} {(moves[^1].Payload.GetValueOrDefault("into") == "enemy" ? "into" : "to")} {to}";
        return $"{English.Capitalise(host)} marched {took}, {route}.";
    }

    /// <summary>The marches as the template reads them: one sentence for each host on the move, in brief.</summary>
    public static string Tell(IReadOnlyList<CampaignMove> moves) => string.Join(" ", moves.Select(m => m.Line).Where(l => l.Length > 0));

    /// <summary>The marches as a brief lists them, one a line, for the chronicler (and the narration guard's facts).</summary>
    public static string Brief(IReadOnlyList<CampaignMove> moves) =>
        string.Join("\n", moves.Select(m => m.Line).Where(l => l.Length > 0).Select((l, i) => $"{i + 1}. {l}"));
}

using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// One move of a campaign as an episode tells it (sub-project 10 C3): the move's event, its line, where the host came from
/// and where it went (map points, 0–1), its side (attacker or defender, for the arrow's colour) and whether it crossed into
/// the enemy's land.
/// </summary>
public sealed record CampaignMove(long EventId, string Line, MapPoint From, MapPoint To, string Side, bool Invasion);

/// <summary>A war's moves within an episode, for the section that tells the war.</summary>
public static class Campaign
{
    /// <summary>At most this many moves a war's section shows; crossings into enemy land first, then the latest.</summary>
    public const int MovesPerWar = 6;

    /// <summary>At most this many of them are read aloud in the template's words.</summary>
    public const int MovesTold = 3;

    /// <summary>The war an event belongs to, if it names one.</summary>
    public static long WarOf(WorldEvent ev) => ev.Actors.TryGetValue("war", out var war) && war.Kind == War.Kind ? war.Id : 0;

    /// <summary>The war's moves among <paramref name="events"/>, in the order they were made.</summary>
    public static IReadOnlyList<CampaignMove> Moves(World world, IEnumerable<WorldEvent> events, long warId, TemplateNarrator narrator)
    {
        if (warId == 0 || !world.Wars.TryGetValue(warId, out var war)) return [];
        var moves = events.Where(e => e.Type == "ArmyMoved" && WarOf(e) == warId).ToList();
        return moves.OrderByDescending(e => e.Importance > 20).ThenByDescending(e => e.Id).Take(MovesPerWar).OrderBy(e => e.Id)
            .Select(e => Move(world, war, e, narrator)).OfType<CampaignMove>().ToList();
    }

    private static CampaignMove? Move(World world, War war, WorldEvent ev, TemplateNarrator narrator)
    {
        if (!ev.Actors.TryGetValue("from", out var from) || ev.Place is not { } to) return null;
        if (MapPoint.Of(world, from) is not { } a || MapPoint.Of(world, to) is not { } b) return null;
        var realm = ev.Actors.TryGetValue("realm", out var r) ? r.Id : 0;
        var side = war.Attackers.Contains(realm) || war.AttackerId == realm ? "attacker" : "defender";
        return new CampaignMove(ev.Id, narrator.Sentence(ev), a, b, side, ev.Payload.GetValueOrDefault("into") == "enemy" && ev.Importance > 20);
    }

    /// <summary>
    /// The moves as the template reads them: the crossings and the latest, a few at most, in order; never the section's own
    /// event (<paramref name="except"/>), which it has told already.
    /// </summary>
    public static string Tell(IReadOnlyList<CampaignMove> moves, long except = 0)
    {
        var told = moves.Where(m => m.EventId != except).OrderByDescending(m => m.Invasion).ThenByDescending(m => m.EventId).Take(MovesTold).OrderBy(m => m.EventId).ToList();
        return told.Count == 0 ? "" : "On the march: " + string.Join(" ", told.Select(m => m.Line));
    }

    /// <summary>The moves as a brief lists them, one a line, for the chronicler (and the narration guard's facts).</summary>
    public static string Brief(IReadOnlyList<CampaignMove> moves) => string.Join("\n", moves.Select((m, i) => $"{i + 1}. {m.Line}"));
}

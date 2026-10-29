using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// How much an event matters to the reader now (spec §7 "Importance and the digest"): the importance the
/// engine gave it, raised by the fame of those involved (prominent people: rulers, heirs, the awakened, the renowned; independent and high-ranked realms)
/// and above all by entities the player follows; 0–100.
/// </summary>
public static class Importance
{
    public const int FollowedBonus = 30;

    public static int Score(WorldEvent ev, World world, IReadOnlySet<EntityRef> followed)
    {
        double score = ev.Importance;
        var fame = 0.0;
        foreach (var actor in ev.Actors.Values)
        {
            if (actor.Kind == Character.Kind && world.Characters.TryGetValue(actor.Id, out var c))
                fame = Math.Max(fame, Systems.Prominence.Of(world, c) / 3.0);
            else if (actor.Kind == Realm.Kind && world.Realms.TryGetValue(actor.Id, out var realm))
                fame = Math.Max(fame, (realm.LiegeId == 0 ? 10 : 0) + (int)realm.Rank * 3);
        }
        score += Math.Min(20, fame);
        if (followed.Count > 0 && (ev.Actors.Values.Any(followed.Contains) || ev.Place is { } place && followed.Contains(place)))
            score += FollowedBonus;
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    /// <summary>Parses "char#12", as entities are printed.</summary>
    public static EntityRef ParseRef(string text)
    {
        var parts = text.Split('#');
        if (parts.Length != 2 || parts[0].Length == 0 || !long.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id))
            throw new FormatException($"'{text}' is not an entity like char#12 or realm#3.");
        return new EntityRef(parts[0], id);
    }
}

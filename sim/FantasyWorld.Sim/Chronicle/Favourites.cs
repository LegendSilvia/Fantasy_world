using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// The characters viewers care for (the owner, 2026-10-30: "future video must focus on a character that viewer interest"):
/// each living character's interest is their followers (thrice), the visitors who opened their page, and the blessings
/// given to their side of a face-off; the most interesting few become the world's followed, whose events rank higher in
/// each episode and who are told of even in a quiet year.
/// </summary>
public static class Favourites
{
    /// <summary>How many favourites an episode follows.</summary>
    public const int Count = 5;

    public const int PerFollower = 3, PerView = 1, PerBlessing = 1;

    /// <summary>Each living character's interest from the tally (only those with any).</summary>
    public static IReadOnlyDictionary<long, int> Interest(World world, EventLog log, Tally tally)
    {
        var interest = new Dictionary<long, int>();
        void Add(long id, int points)
        {
            if (points > 0 && world.Characters.TryGetValue(id, out var c) && c.IsActive) interest[id] = interest.GetValueOrDefault(id) + points;
        }
        foreach (var (id, n) in tally.FollowsOf) Add(id, n * PerFollower);
        foreach (var (id, n) in tally.ViewsOf) Add(id, n * PerView);
        foreach (var (eventId, (a, b)) in tally.Blessings)
            if (log.TryGet(eventId, out var ev) && FaceOffs.For(world, ev) is { } faceOff)
            {
                if (Importance.ParseRef(faceOff.A.Ref) is { Kind: Character.Kind } sideA) Add(sideA.Id, a * PerBlessing);
                if (Importance.ParseRef(faceOff.B.Ref) is { Kind: Character.Kind } sideB) Add(sideB.Id, b * PerBlessing);
            }
        return interest;
    }

    /// <summary>The favourites: the most interesting living characters, most first (ties to the elder id).</summary>
    public static IReadOnlyList<long> Of(World world, EventLog log, Tally tally, int count = Count) =>
        Interest(world, log, tally).OrderByDescending(i => i.Value).ThenBy(i => i.Key).Take(count).Select(i => i.Key).ToList();
}

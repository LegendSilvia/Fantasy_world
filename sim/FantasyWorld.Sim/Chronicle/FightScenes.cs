using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// The fights an episode shows as a still image (the owner, 2026-10-27): a duel fought or turning, an army clash. The
/// section telling one carries a description of the picture for the image API, never read aloud: the template's, from the
/// facts, which the chronicler (Claude) may replace with its own. Names are left out; a painter cannot draw a name.
/// </summary>
public static class FightScenes
{
    /// <summary>At most this many stills an episode, the first fights told.</summary>
    public const int PerEpisode = 2;

    public static readonly IReadOnlySet<string> Fights = new HashSet<string>(StringComparer.Ordinal)
    {
        "DuelFought", "DuelTurned", "BattleFought", "BattleJoined", "BattleTurned",
    };

    /// <summary>The longest description kept (the image API takes 2,048 characters).</summary>
    public const int MaxCharacters = 600;

    /// <summary>The picture of a fight, from the facts: who in what colours, the ground, the moment; null if not a fight.</summary>
    public static string? Describe(World world, WorldEvent ev)
    {
        if (!Fights.Contains(ev.Type)) return null;
        var ground = Ground(world, ev);
        if (ev.Type.StartsWith("Duel", StringComparison.Ordinal))
        {
            var (a, b) = (Dress(world, ev.Actors.GetValueOrDefault("challenger")), Dress(world, ev.Actors.GetValueOrDefault("defender")));
            var stakes = ev.Payload.GetValueOrDefault("stakes") is "to the death" ? "a fight to the death" : "a duel of honour";
            return $"two duellists crossing swords, one in {a}, the other in {b}, {stakes}, {ground}, onlookers at a distance, "
                   + "the decisive clash, dramatic light, medieval fantasy";
        }
        var realms = ev.Actors.Values.Where(r => r.Kind == Realm.Kind).Distinct().Take(2)
            .Select(r => world.Realms.TryGetValue(r.Id, out var realm) ? $"{realm.Colors[0]} and {realm.Colors.ElementAtOrDefault(1) ?? "white"}" : "grey")
            .ToList();
        var banners = realms.Count == 2 ? $"banners in {realms[0]} against banners in {realms[1]}" : "two hosts under rival banners";
        return $"two medieval armies clashing, {banners}, shield walls and a cavalry charge, spears and dust, {ground}, "
               + "the height of the battle, epic wide view, medieval fantasy";
    }

    private static string Dress(World world, EntityRef? who) =>
        who is { } r && world.Characters.TryGetValue(r.Id, out var c)
                     && world.Realms.Values.FirstOrDefault(realm => c.HouseId != 0 && Realms.RulerOf(world, realm)?.HouseId == c.HouseId) is { } realm
            ? $"{realm.Colors[0]} and {realm.Colors.ElementAtOrDefault(1) ?? "white"}"
            : "plain steel and leather";

    /// <summary>The land where it happened, by the region's biome.</summary>
    private static string Ground(World world, WorldEvent ev)
    {
        var region = ev.Place is { } place
            ? place.Kind == Region.Kind ? world.Regions.GetValueOrDefault(place.Id)
            : place.Kind == Settlement.Kind && world.Settlements.TryGetValue(place.Id, out var s) ? world.Regions.GetValueOrDefault(s.RegionId) : null
            : null;
        return region?.Biome switch
        {
            Biome.Tundra => "on frozen tundra under a pale sky",
            Biome.Taiga => "among snowy pines",
            Biome.Forest => "in a forest clearing",
            Biome.Swamp => "in misty marshland",
            Biome.Desert => "on desert dunes under a hard sun",
            Biome.Savanna => "on dry golden grassland",
            Biome.Jungle => "in a steaming jungle",
            Biome.Hills => "on rolling hills",
            Biome.Mountain => "on a rocky mountain pass",
            _ => "on an open green field",
        };
    }

    /// <summary>The sections that get a picture: the first fights told, each with the template's description.</summary>
    public static IReadOnlyList<EpisodeSection> Mark(World world, EventLog log, IReadOnlyList<EpisodeSection> sections)
    {
        var marked = 0;
        return sections.Select(s => marked < PerEpisode && s.Kind is EpisodeSection.Headline or EpisodeSection.Story
                                    && s.EventId != 0 && log.TryGet(s.EventId, out var ev) && Describe(world, ev) is { } scene
            ? Count(s with { Scene = scene })
            : s).ToList();

        EpisodeSection Count(EpisodeSection s)
        {
            marked++;
            return s;
        }
    }
}

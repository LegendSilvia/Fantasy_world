using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Companions (the owner, 2026-10-29): each year a beast may come to a character (tamed in the wilds, rangers and the wild
/// peoples most often, or the young of a beast they slew this past year), named by Claude with its lore; and two friends
/// living in one place may swear to stand by each other. A companion lost (a sworn one dead) is news too. A beast at a
/// fighter's side makes them stronger (<see cref="Arms.BeastEdge"/>).
/// </summary>
public sealed class CompanionSystem(IInventor? inventor = null) : ISimSystem
{
    public const double TameChance = 0.01, RangerTame = 0.06, CubChance = 0.15, SwearChance = 0.03;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "Companions";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        // Sworn companions who died are mourned.
        foreach (var c in world.Characters.Values.Where(c => c.IsActive && c.Companion is { Kind: Companion.Sworn }).OrderBy(c => c.Id).ToList())
            if (!world.Characters.TryGetValue(c.Companion!.CharacterId, out var friend) || !friend.IsAlive)
            {
                ctx.Emit(new EventDraft("CompanionLost",
                    Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref, ["companion"] = new(Character.Kind, c.Companion.CharacterId) },
                    Place: world.Settlements.GetValueOrDefault(c.SettlementId)?.Ref,
                    Causes: friend?.Died is not null && ctx.Log.Involving(friend.Ref).LastOrDefault(e => e.Type == "CharacterDied") is { } died
                        ? new[] { new Cause(died.Id, CauseKind.Trigger, 1.0) } : Array.Empty<Cause>(),
                    Importance: Fates.Importance(c, 20), Tags: new[] { "Characters" }));
                c.Companion = null;
            }

        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        var slain = ctx.Log.All.Where(e => e.Type == "MonsterSlain" && e.Date.Day >= since).ToList();
        foreach (var c in world.Characters.Values.Where(c => c.IsActive && c.Stage(ctx.Date) == LifeStage.Adult && c.Companion is null && c.Class.Length > 0)
                     .OrderBy(c => c.Id).ToList())
        {
            if (c.Companion is not null) continue;
            // The young of a beast they slew may follow them.
            var kill = slain.FirstOrDefault(e => e.Actors.TryGetValue("hunter", out var h) && h.Id == c.Id);
            if (kill is not null && kill.Actors.TryGetValue("monster", out var m) && world.Monsters.TryGetValue(m.Id, out var beast)
                && !Monster.SeaBreeds.Contains(beast.Breed) && ctx.Rng.Chance(CubChance))
            {
                Tame(ctx, c, $"the young of {beast.Name}, a {beast.Breed}", beast.Breed, kill);
                continue;
            }
            var wild = Classes.Base(c.Class) == Classes.Ranger || Peoples.Of(world, c)?.Temper == Race.Wild;
            if (ctx.Rng.Chance(wild ? RangerTame : TameChance))
            {
                var region = world.Settlements.TryGetValue(c.SettlementId, out var home) ? world.Regions.GetValueOrDefault(home.RegionId) : null;
                var breeds = Breeds(region?.Biome);
                var breed = breeds[ctx.Rng.NextInt(0, breeds.Length)];
                Tame(ctx, c, $"a {breed} of the {(region?.Biome.ToString().ToLowerInvariant() ?? "wilds")}", breed, null);
                continue;
            }
            // Two friends in one place may swear to stand by each other.
            if (world.Settlements.TryGetValue(c.SettlementId, out var place) && ctx.Rng.Chance(SwearChance)
                && world.PeopleOf(place).Where(o => o.Id != c.Id && o.IsActive && o.Companion is null && o.Stage(ctx.Date) == LifeStage.Adult
                        && Relations.Opinion(c, o) >= Relations.FriendAt && Relations.Opinion(o, c) >= Relations.FriendAt)
                    .OrderByDescending(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault() is { } friend)
            {
                var sworn = ctx.Emit(new EventDraft("CompanionJoined",
                    Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref, ["companion"] = friend.Ref }, Place: place.Ref,
                    Payload: new Dictionary<string, string> { ["kind"] = Companion.Sworn },
                    Importance: Math.Max(Fates.Importance(c, 15), Fates.Importance(friend, 15)), Tags: new[] { "Characters" }));
                c.Companion = new Companion(friend.Name, Companion.Sworn, "sworn companion", "", friend.Id, sworn.Id, ctx.Date.Day);
                friend.Companion = new Companion(c.Name, Companion.Sworn, "sworn companion", "", c.Id, sworn.Id, ctx.Date.Day);
            }
        }
    }

    private void Tame(SimContext ctx, Character c, string what, string breed, WorldEvent? cause)
    {
        var fallback = BeastName(c.Id, ctx.Date.Day);
        var named = _inventor.Invent(new InventionBrief("beast companion", $"{c.Ref}|companion|{ctx.Date.Day}",
            $"{what}, which has come to follow {c.Name}, a {Classes.Title(Classes.Of(ctx.World, c))}" + (cause is null ? ", tamed in the wilds." : ", after they slew its parent."),
            fallback));
        var lore = named.Lore ?? (cause is null ? Met(c.Name, c.Id + ctx.Date.Day) : $"It followed {c.Name} home from its parent's lair, and would not be driven off.");
        var ev = ctx.Emit(new EventDraft("CompanionJoined", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref },
            Place: ctx.World.Settlements.GetValueOrDefault(c.SettlementId)?.Ref,
            Payload: new Dictionary<string, string> { ["kind"] = Companion.Beast, ["name"] = named.Name, ["what"] = what, ["breed"] = breed },
            Causes: cause is null ? Array.Empty<Cause>() : new[] { new Cause(cause.Id, CauseKind.Trigger, 0.8) },
            Importance: Fates.Importance(c, 18), Tags: new[] { "Characters", "Nature" }));
        c.Companion = new Companion(named.Name, Companion.Beast, what, lore, 0, ev.Id, ctx.Date.Day);
    }

    private static string[] Breeds(Geo.Biome? biome) => biome switch
    {
        Geo.Biome.Forest or Geo.Biome.Jungle => ["great wolf", "black panther", "giant owl"],
        Geo.Biome.Taiga or Geo.Biome.Tundra => ["dire wolf", "snow bear", "white raven"],
        Geo.Biome.Mountain or Geo.Biome.Hills => ["mountain lion", "great eagle", "rock ram"],
        Geo.Biome.Desert or Geo.Biome.Savanna => ["sand lion", "desert hawk", "great lizard"],
        Geo.Biome.Swamp => ["marsh drake", "giant heron", "bog hound"],
        _ => ["war-hound", "grey wolf", "hunting falcon"],
    };

    // Without an inventor (the offline prehistory) a beast is named from two halves and its meeting told one of several
    // ways, so a world's companions are not all one Ashfang that came out of the wilds one winter (the owner, 2026-10-29).
    private static readonly string[] Firsts = ["Ash", "Grey", "Thorn", "Ember", "Salt", "Night", "Storm", "Iron", "Frost", "Red", "Moon", "Swift", "Old", "Black", "Gold"];
    private static readonly string[] Lasts = ["fang", "mane", "claw", "wing", "tail", "eye", "heart", "song", "bane", "coat"];

    public static string BeastName(long characterId, long day)
    {
        var n = (ulong)(characterId * 7919 + day);
        return Firsts[(int)(n % (ulong)Firsts.Length)] + Lasts[(int)(n / (ulong)Firsts.Length % (ulong)Lasts.Length)];
    }

    public static string Met(string who, long seed) => ((ulong)seed % 6) switch
    {
        0 => $"It came out of the wilds one winter and would not leave {who}'s side.",
        1 => $"{who} found it caught in a trapper's snare and cut it free; it has followed ever since.",
        2 => $"It was the runt of a litter left to die, and {who} raised it by hand.",
        3 => $"It fought {who} for a kill, lost, and stayed.",
        4 => $"It came to {who}'s fire on a night of storm and was still there at dawn.",
        _ => $"{who} won it in a wager from a hunter who could not hold it.",
    };
}

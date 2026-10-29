using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The world's own resources (<see cref="WorldResource"/>; the owner, 2026-10-29): made with the world, one to three, the
/// first always the seal against the dark and always hidden; named by Claude before the world's history; found when a secret
/// of the world is uncovered; and what each does to the realm that holds it.
/// </summary>
public static class Wonders
{
    /// <summary>What a found resource's land yields a year, and holds.</summary>
    public const double Yield = 40, Capacity = 4000;

    /// <summary>How much harder a realm holding a forge resource fights, how much gentler death comes where life is held.</summary>
    public const double ForgeArms = 1.25, LifeMortality = 0.7;

    /// <summary>Gold a unit of a wealth resource brings a treasury.</summary>
    public const double WealthGold = 3;

    private static readonly Dictionary<string, (string Name, string What)[]> Own = new(StringComparer.Ordinal)
    {
        [WorldResource.Seal] = [("Starsilver", "a pale metal that burns cold and that no dark power can pass"),
            ("Sunglass", "a glass that holds the light of the first dawn"), ("Wardstone", "a grey stone that hums against the touch of the old powers")],
        [WorldResource.Forge] = [("Emberiron", "an iron that stays warm long after the forge is cold"),
            ("Skysteel", "a metal fallen from the sky, light and harder than any steel"), ("Blackbronze", "a dark bronze that takes an edge and never loses it")],
        [WorldResource.Life] = [("Dawnbloom", "a white flower whose sap closes wounds and cools fevers"),
            ("Heartwater", "a spring whose water gives the old their strength again"), ("Lifemoss", "a green moss that keeps the sick from dying")],
        [WorldResource.Wealth] = [("Dragonamber", "a golden amber worth more than gold"),
            ("Glimmerpearl", "pearls that shine in the dark, prized by every court"), ("Kingsalt", "a rose-coloured salt that the rich pay anything for")],
    };

    private static long Seed(World world) => unchecked((long)world.Seed);

    /// <summary>The world's resources, made with it: the seal first and hidden; the others found or hidden by the world's seed.</summary>
    public static void Generate(World world)
    {
        var land = world.Regions.Values.Where(r => r.Biome != Geo.Biome.Ocean).OrderBy(r => r.Id).ToList();
        if (land.Count == 0) return;
        var count = 1 + Hash.Percent(Seed(world), 9200) % 3;
        var roles = new List<string> { WorldResource.Seal };
        roles.AddRange(WorldResource.Roles.Skip(1).OrderBy(r => Hash.Percent(Seed(world), 9201, r.Length * 31 + r[0])).Take(count - 1));
        for (var i = 0; i < roles.Count; i++)
        {
            var role = roles[i];
            var (name, what) = Own[role][Hash.Percent(Seed(world), 9202, i) % Own[role].Length];
            // The seal lies in a wild, dangerous land; the others anywhere.
            var places = role == WorldResource.Seal ? land.OrderByDescending(r => r.Danger).ThenBy(r => r.Id).Take(Math.Max(1, land.Count / 10)).ToList() : land;
            var region = places[Hash.Percent(Seed(world), 9203, i) * places.Count / 100];
            var hidden = role == WorldResource.Seal || Hash.Percent(Seed(world), 9204, i) < 50;
            var wonder = new WorldResource(name.ToLowerInvariant(), name, what, role, region.Id, hidden);
            world.Wonders.Add(wonder);
            if (!hidden) Lay(world, wonder);
        }
    }

    private static void Lay(World world, WorldResource wonder) =>
        world.Regions[wonder.RegionId].Resources[wonder.Kind] = new ResourceDeposit { Capacity = Capacity, Yield = Yield };

    /// <summary>Claude names each resource and tells what it is, before the world's history (the rules' own words stand without).</summary>
    public static void Invent(World world, IInventor inventor)
    {
        for (var i = 0; i < world.Wonders.Count; i++)
        {
            var w = world.Wonders[i];
            var region = world.Regions[w.RegionId];
            var invented = inventor.Invent(new InventionBrief("unique resource of the world", $"wonder#{i}",
                $"A resource found nowhere else in this world, in {region.Name} ({region.Biome.ToString().ToLowerInvariant()}). Its part in the story: {w.Power}. "
                + "Name it as one word or two, a substance (a metal, a stone, a plant, a water, a beast's leavings), and in the lore tell what it is and where it comes from.",
                w.Name));
            var name = invented.Name.Length is > 0 and <= 30 && world.Wonders.All(o => o == w || o.Name != invented.Name) ? invented.Name : w.Name;
            var renamed = w with { Name = name, Kind = name.ToLowerInvariant(), What = invented.Lore is { Length: > 0 } lore ? lore : w.What };
            if (region.Resources.Remove(w.Kind, out var deposit)) region.Resources[renamed.Kind] = deposit;
            world.Wonders[i] = renamed;
        }
    }

    /// <summary>A secret uncovered brings the first hidden resource to light, found by the one who uncovered it.</summary>
    public static WorldEvent? Discover(SimContext ctx, Character finder, long cause)
    {
        var world = ctx.World;
        var i = world.Wonders.FindIndex(w => w.Hidden);
        if (i < 0) return null;
        var w = world.Wonders[i];
        var region = world.Regions[w.RegionId];
        var found = ctx.Emit(new EventDraft("WonderFound", Actors: new Dictionary<string, EntityRef> { ["finder"] = finder.Ref }, Place: region.Ref,
            Payload: new Dictionary<string, string> { ["name"] = w.Name, ["what"] = w.What, ["power"] = w.Power, ["role"] = w.Role },
            Causes: new[] { new Cause(cause, CauseKind.Trigger, 1.0) }, Importance: Fates.Importance(finder, 75), Tags: new[] { "Characters", "Deeds", "Economy" }));
        world.Wonders[i] = w with { Hidden = false, FoundEventId = found.Id, FinderId = finder.Id };
        Lay(world, world.Wonders[i]);
        return found;
    }

    /// <summary>The found resources of a role.</summary>
    public static IEnumerable<WorldResource> Found(World world, string role) => world.Wonders.Where(w => !w.Hidden && w.Role == role);

    /// <summary>Whether a realm has a found resource of a role in its stores.</summary>
    public static bool Holds(World world, Realm realm, string role) => Found(world, role).Any(w => Stocks.Stock(realm, w.Kind) >= 1);
}

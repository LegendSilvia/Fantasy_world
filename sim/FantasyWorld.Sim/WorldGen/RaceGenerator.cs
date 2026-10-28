using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The world's races (sub-project 13 N2): two to six original peoples, each with a name, looks and a nature (the rules'
/// words; Claude may rewrite them when the world is made, <see cref="Agency.IInventor"/>), a temper, gifts, a lifespan and
/// the lands it favours; old friendships and hatreds between them. Every culture is given the race whose lands best fit
/// its homeland, each race at least one culture, and a continent's cultures lean to the races already there.
/// </summary>
public static class RaceGenerator
{
    private static readonly string[] Builds =
        ["tall and slender", "short and broad", "towering and heavy-boned", "small and quick", "lean and long-limbed", "stocky and thick-necked"];

    private static readonly string[] Skins =
    [
        "ashen grey skin", "bronze skin", "pale, near-translucent skin", "green-tinged skin", "sea-blue skin", "bark-brown skin",
        "copper skin flecked with gold", "dusky violet skin",
    ];

    private static readonly string[] Features =
    [
        "swept-back horns", "long pointed ears", "small tusks", "feathered crests", "fine scales along the neck and brow", "great dark eyes",
        "crowns of antler-like bone", "softly glowing eye markings", "fur-tufted ears and a long tail",
    ];

    private static readonly IReadOnlyDictionary<string, string> Natures = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Race.Warlike] = "a proud, war-loving people who count their worth in battles won",
        [Race.Pious] = "a devout people who see the will of their gods in all things",
        [Race.Arcane] = "a people steeped in old magic, who prize lore above gold",
        [Race.Mercantile] = "a shrewd, travelling people of markets and long roads",
        [Race.Wild] = "a free and restless people of the wilds, wary of walls and kings",
        [Race.Stoic] = "a patient, enduring people, slow to anger and slower to forget",
    };

    private static readonly IReadOnlyDictionary<string, (string Attribute, int Lean)[]> TemperGifts = new Dictionary<string, (string, int)[]>(StringComparer.Ordinal)
    {
        [Race.Warlike] = [("martial", 2), ("prowess", 2), ("diplomacy", -2)],
        [Race.Pious] = [("diplomacy", 2), ("lore", 1), ("intrigue", -1)],
        [Race.Arcane] = [("lore", 3), ("prowess", -2)],
        [Race.Mercantile] = [("stewardship", 3), ("diplomacy", 1), ("martial", -1)],
        [Race.Wild] = [("prowess", 3), ("stewardship", -2)],
        [Race.Stoic] = [("stewardship", 2), ("martial", 1), ("intrigue", -1)],
    };

    private static readonly double[] Spans = [0.75, 1, 1, 1.5, 2.5, 4];

    /// <summary>
    /// The races as the world's people know them (the owner: Claude names and describes them): each is put to the
    /// inventor with its looks, temper, span, lands and old bonds; its answer gives the race its name and its nature
    /// (lore). Without an answer the rules' words stand. Run on a newly made world, before its history.
    /// </summary>
    public static void Invent(World world, Agency.IInventor inventor)
    {
        foreach (var race in world.Races.Values.OrderBy(r => r.Id).ToList())
        {
            static string Or(string names) => names.Length > 0 ? names : "none";
            string Names(IEnumerable<long> ids) => string.Join(", ", ids.Select(id => world.Races.GetValueOrDefault(id)?.Name).OfType<string>());
            var lands = string.Join(" and ", race.Homelands.Select(b => b.ToString().ToLowerInvariant()));
            var facts = $"An original people of this world (not elves, dwarves or orcs): {race.Looks}. Temper: {race.Temper}. "
                        + $"They live {(race.Span >= 2 ? $"{race.Span:0.#} times as long as others" : race.Span < 1 ? "shorter lives than others" : "as long as others")}"
                        + $", and favour {lands}. Their old foes: {Or(Names(race.Foes))}; old friends: {Or(Names(race.Friends))}.";
            var invented = inventor.Invent(new Agency.InventionBrief("race", race.Ref.ToString(), facts, race.Name));
            if (invented.Name != race.Name && world.Races.Values.All(r => r.Name != invented.Name) && world.Cultures.Values.All(c => c.Name != invented.Name))
                race.Name = invented.Name;
            if (invented.Lore is { Length: > 0 } lore) race.Nature = lore;
        }
    }

    public static int CountFor(int regions) => regions switch { < 40 => 2, < 200 => 4, _ => 6 };

    public static void Generate(World world, Rng rng)
    {
        var cultures = world.Cultures.Values.OrderBy(c => c.Id).ToList();
        if (cultures.Count == 0) return;
        var count = Math.Min(CountFor(world.Regions.Count), cultures.Count);
        var biomes = world.Regions.Values.Where(r => r.Biome != Biome.Ocean).GroupBy(r => r.Biome)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        var tempers = Race.Tempers.OrderBy(_ => rng.NextULong()).ToList();
        var names = new NameGen(rng);
        var races = new List<Race>();
        for (var i = 0; i < count; i++)
        {
            var temper = tempers[i % tempers.Count];
            var style = NameStyle.Derive(rng);
            var name = new NameGen(rng, style).Proper();
            while (races.Any(r => r.Name == name) || world.Cultures.Values.Any(c => c.Name == name)) name = names.Proper();
            var first = biomes[(i + rng.NextInt(0, Math.Max(1, biomes.Count))) % biomes.Count];
            var second = biomes[rng.NextInt(0, biomes.Count)];
            var span = Spans[rng.NextInt(0, Spans.Length)];
            var looks = $"{Builds[rng.NextInt(0, Builds.Length)]}, with {Skins[rng.NextInt(0, Skins.Length)]} and {Features[rng.NextInt(0, Features.Length)]}";
            var lives = span >= 2 ? ", long-lived" : span < 1 ? ", short-lived and quick to grow" : "";
            races.Add(new Race
            {
                Id = world.Ids.Take(Race.Kind),
                Name = name,
                Looks = looks,
                Nature = $"The {name} are {Natures[temper]}{lives}.",
                Temper = temper,
                Style = style,
                Span = span,
                Homelands = new[] { first, second }.Distinct().ToList(),
                Gifts = new SortedDictionary<string, int>(TemperGifts[temper].ToDictionary(g => g.Attribute, g => g.Lean), StringComparer.Ordinal),
            });
        }

        // Old friendships and hatreds: the warlike quarrel more; at least one old hatred in a world of several races.
        foreach (var (a, b) in races.SelectMany((a, i) => races.Skip(i + 1).Select(b => (a, b))))
        {
            var hostile = a.Temper == Race.Warlike || b.Temper == Race.Warlike ? 0.4 : 0.2;
            if (rng.Chance(hostile)) { a.Foes.Add(b.Id); b.Foes.Add(a.Id); }
            else if (rng.Chance(0.25)) { a.Friends.Add(b.Id); b.Friends.Add(a.Id); }
        }
        if (races.Count > 1 && races.All(r => r.Foes.Count == 0))
        {
            var (a, b) = (races[0], races[^1]);
            a.Friends.Remove(b.Id);
            b.Friends.Remove(a.Id);
            a.Foes.Add(b.Id);
            b.Foes.Add(a.Id);
        }
        foreach (var race in races) world.Races[race.Id] = race;

        // Each race takes the culture whose homeland best fits it; the rest go where their land and continent fit best.
        Biome Home(Culture c) => world.Regions[c.HomeRegionId].Biome;
        long Continent(Culture c) => world.Regions[c.HomeRegionId].ContinentId;
        var unplaced = cultures.ToList();
        foreach (var race in races)
        {
            var culture = unplaced.OrderByDescending(c => race.Homelands.Contains(Home(c)) ? 1 : 0).ThenBy(_ => rng.NextULong()).First();
            culture.RaceId = race.Id;
            unplaced.Remove(culture);
        }
        foreach (var culture in unplaced)
        {
            var here = cultures.Where(c => c.RaceId != 0 && Continent(c) == Continent(culture)).Select(c => c.RaceId).ToHashSet();
            culture.RaceId = races.OrderByDescending(r => (r.Homelands.Contains(Home(culture)) ? 2 : 0) + (here.Contains(r.Id) ? 1.5 : 0) + rng.NextDouble())
                .First().Id;
        }
    }
}

using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// Character classes (the owner, 2026-10-28): six callings in two triangles, each beating one and beaten by another. Steel
/// and shadow: the warrior beats the rogue, the rogue the mage, the mage the warrior. Faith and the wild: the paladin
/// beats the warlock, the warlock the ranger, the ranger the paladin. A class that counters its foe's fights 15% stronger:
/// in a duel, in a champions' clash before the hosts, and a commander against the other's.
/// </summary>
public static class Classes
{
    public const string Warrior = "warrior", Rogue = "rogue", Mage = "mage", Paladin = "paladin", Warlock = "warlock", Ranger = "ranger";
    public static readonly IReadOnlyList<string> All = [Warrior, Rogue, Mage, Paladin, Warlock, Ranger];

    /// <summary>What a counter is worth in a fight.</summary>
    public const double CounterEdge = 1.15;

    private static readonly Dictionary<string, (string Beats, string Why)> Wheel = new(StringComparer.Ordinal)
    {
        [Warrior] = (Rogue, "armour shrugs off the dagger"),
        [Rogue] = (Mage, "the blade is quicker than the spell"),
        [Mage] = (Warrior, "spells pierce steel"),
        [Paladin] = (Warlock, "holy light burns the dark"),
        [Warlock] = (Ranger, "curses find the hunter in the wilds"),
        [Ranger] = (Paladin, "arrows from afar find slow plate"),
    };

    /// <summary>
    /// An awakened class (the owner, 2026-10-29): a calling a character grew into through what befell them, named by Claude,
    /// kept as "base:Name" (mage:Stormcaller); it stands where its base stands on the wheel, and fights a tenth stronger.
    /// </summary>
    public const double AwakenedEdge = 1.10;

    public static string Awaken(string baseClass, string name) => $"{Base(baseClass)}:{name}";

    /// <summary>The base calling of a class: the class itself, or what an awakened one grew from.</summary>
    public static string Base(string? k) => k is null ? "" : k.IndexOf(':') is var i and >= 0 ? k[..i] : k;

    public static bool IsAwakened(string? k) => k is not null && k.Contains(':');

    public static bool Counters(string? a, string? b) => a is not null && b is not null && Wheel.TryGetValue(Base(a), out var w) && w.Beats == Base(b);

    /// <summary>The edge of <paramref name="a"/> over <paramref name="b"/>: 1.15 if it counters, a tenth more if awakened.</summary>
    public static double Edge(string? a, string? b) => (Counters(a, b) ? CounterEdge : 1) * (IsAwakened(a) ? AwakenedEdge : 1);

    /// <summary>Why a class beats the one it counters, in a few words.</summary>
    public static string Why(string a) => Wheel.TryGetValue(Base(a), out var w) ? w.Why : "";

    /// <summary>A class as it reads: "Mage", or an awakened one's own name ("Stormcaller").</summary>
    public static string Title(string k) => k.IndexOf(':') is var i and >= 0 ? k[(i + 1)..] : k.Length == 0 ? k : char.ToUpperInvariant(k[0]) + k[1..];

    /// <summary>A character's class: the one they took, or for a child (who has none yet), the one they would take.</summary>
    public static string Of(World world, Character c) => c.Class is { Length: > 0 } k ? k : Choose(world, c);

    /// <summary>
    /// The calling a character's gifts lead to: the warrior from prowess and martial skill, the rogue from intrigue, the
    /// mage from lore where the ley runs strong, the paladin from the devout, the warlock from lore and intrigue in the
    /// ambitious and cruel, the ranger from prowess and stewardship in the wilds; a race's temper leans one way. A demon
    /// lord is a warlock. Ties are broken by the character's id, so a world always chooses alike.
    /// </summary>
    public static string Choose(World world, Character c)
    {
        if (SagaSystem.IsDemonLord(world, c)) return Warlock;
        var a = c.Attributes;
        var temper = Peoples.Of(world, c)?.Temper;
        var region = world.Settlements.TryGetValue(c.SettlementId, out var home) ? world.Regions.GetValueOrDefault(home.RegionId) : null;
        double If(bool yes, double bonus) => yes ? bonus : 0;
        var scores = new Dictionary<string, double>
        {
            [Warrior] = a.Prowess + a.Martial * 0.5 + If(temper == Race.Warlike, 3) + If(temper == Race.Stoic, 2) + If(c.Has(Traits.Brave), 1),
            [Rogue] = a.Intrigue * 1.2 + If(c.Has(Traits.Deceitful), 3) + If(temper == Race.Mercantile, 3),
            [Mage] = a.Lore * 1.1 + (region?.LeyPower ?? 0) / 20 + If(temper == Race.Arcane, 3),
            [Paladin] = a.Martial * 0.6 + a.Diplomacy * 0.4 + If(c.Has(Traits.Devout), 5) + If(temper == Race.Pious, 3) + If(c.Has(Traits.Honest), 1),
            [Warlock] = a.Lore * 0.6 + a.Intrigue * 0.5 + If(c.Has(Traits.Ambitious), 2) + If(c.Has(Traits.Cruel), 3) + If(c.Has(Traits.Cynical), 2),
            [Ranger] = a.Prowess * 0.6 + a.Stewardship * 0.6 + If(temper == Race.Wild, 3)
                       + If(region?.Biome is Biome.Forest or Biome.Taiga or Biome.Jungle or Biome.Hills, 3),
        };
        return scores.OrderByDescending(s => s.Value + Jitter(c.Id, s.Key)).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;
    }

    private static double Jitter(long id, string k)
    {
        var h = (ulong)id * 0x9E3779B97F4A7C15UL ^ (ulong)k.Length * 0xBF58476D1CE4E5B9UL ^ (ulong)k[0] * 0x94D049BB133111EBUL;
        h ^= h >> 31;
        return (h % 300) / 100.0 - 1.5;
    }
}

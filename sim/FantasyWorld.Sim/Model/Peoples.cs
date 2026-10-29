namespace FantasyWorld.Sim.Model;

/// <summary>Queries over races (sub-project 13 N2): whose race a character, culture, region or realm is.</summary>
public static class Peoples
{
    public static Race? Of(World world, Culture? culture) =>
        culture is not null && world.Races.TryGetValue(culture.RaceId, out var race) ? race : null;

    public static Race? Of(World world, Character c) => Of(world, world.Cultures.GetValueOrDefault(c.CultureId));

    public static Race? Of(World world, Region region) => Of(world, world.Cultures.GetValueOrDefault(region.CultureId));

    /// <summary>A realm's race: its ruler's, else its culture's.</summary>
    public static Race? Of(World world, Realm realm) =>
        (Realms.RulerOf(world, realm) is { } ruler ? Of(world, ruler) : null) ?? Of(world, world.Cultures.GetValueOrDefault(realm.CultureId));

    /// <summary>How the races of two peoples stand: 1 old friends, −1 old foes, 0 neither (or the same race, or none).</summary>
    public static int Bond(Race? a, Race? b) =>
        a is null || b is null || a.Id == b.Id ? 0 : a.Hates(b.Id) ? -1 : a.Likes(b.Id) ? 1 : 0;

    /// <summary>How long a character's people live against the common span.</summary>
    public static double SpanOf(World world, Character c) => Of(world, c)?.Span ?? 1;

    /// <summary>A common lifetime's age range: the old begin to sicken from the first, and none outlive the second.</summary>
    public const int CommonOld = 70, CommonLimit = 95;

    /// <summary>
    /// A people's age range (the owner, 2026-10-29: each race has an age limit): from when its old begin to fall ill to the
    /// age none outlive, the common range stretched by its span past adulthood (as <see cref="Systems.LifeSystem.Aged"/> ages them).
    /// </summary>
    public static (int Old, int Limit) Lifespan(double span) =>
        (Character.AdultAge + (int)Math.Round((CommonOld - Character.AdultAge) * span), Character.AdultAge + (int)Math.Round((CommonLimit - Character.AdultAge) * span));
}

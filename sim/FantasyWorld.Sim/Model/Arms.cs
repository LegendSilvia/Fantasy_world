namespace FantasyWorld.Sim.Model;

/// <summary>
/// A companion (the owner, 2026-10-29): a beast that follows a character, or another character sworn to them. A beast has
/// its own name, breed and lore; a sworn companion is the character (<see cref="CharacterId"/>). The event that joined
/// them, and its day.
/// </summary>
public sealed record Companion(string Name, string Kind, string What, string Lore, long CharacterId, long EventId, long Day)
{
    public const string Beast = "beast", Sworn = "sworn";
}

/// <summary>
/// What a character fights with (the owner, 2026-10-29: weapons and companions, with their lore): the most famous weapon
/// relic they hold, else the weapon their calling carries; and their companion.
/// </summary>
public static class Arms
{
    private static readonly Dictionary<string, string> Common = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] = "a heavy longsword and a battered shield",
        [Classes.Rogue] = "a pair of long knives",
        [Classes.Mage] = "a carved staff of old wood",
        [Classes.Paladin] = "a warhammer and a shield bearing their god's sign",
        [Classes.Warlock] = "a black-bladed dagger and a grimoire",
        [Classes.Ranger] = "a longbow of yew and a hunting knife",
    };

    /// <summary>The weapon: a held weapon relic (its name, what it is, its lore), else the calling's own.</summary>
    public static (string Name, string What, string Lore, long ArtifactId) Weapon(World world, Character c)
    {
        var relic = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id && a.Type == ArtifactType.Weapon)
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        return relic is not null
            ? (relic.Name, $"a weapon of {relic.Material}" + (relic.Powers.Count > 0 ? $" ({string.Join(", ", relic.Powers.Select(p => p.Attribute is null ? p.Kind : $"+{p.Amount} {p.Attribute}"))})" : ""),
                relic.Lore, relic.Id)
            : ("", Common.GetValueOrDefault(Classes.Base(Classes.Of(world, c))) ?? Common[Classes.Warrior], "", 0);
    }

    /// <summary>A beast companion fights at their side: this much stronger.</summary>
    public const double BeastEdge = 1.08;

    public static double Edge(Character c) => c.Companion is { Kind: Companion.Beast } ? BeastEdge : 1;

    /// <summary>The weapon and companion as a brief reads them, for the arbiter and the chronicler.</summary>
    public static string Brief(World world, Character c)
    {
        var w = Weapon(world, c);
        var weapon = w.Name.Length > 0 ? $"wields {w.Name}, {w.What}" + (w.Lore.Length > 0 ? $" ({w.Lore})" : "") : $"wields {w.What}";
        var companion = c.Companion switch
        {
            { Kind: Companion.Beast } k => $"; at their side, {k.Name}, {k.What}" + (k.Lore.Length > 0 ? $" ({k.Lore})" : ""),
            { Kind: Companion.Sworn } k => $"; sworn companion of {k.Name}",
            _ => "",
        };
        return weapon + companion;
    }
}

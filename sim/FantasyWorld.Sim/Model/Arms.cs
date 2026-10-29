namespace FantasyWorld.Sim.Model;

/// <summary>
/// A companion (the owner, 2026-10-29): a beast that follows a character, or another character sworn to them. A beast has
/// its own name, breed and lore; a sworn companion is the character (<see cref="CharacterId"/>). The event that joined
/// them, and its day.
/// </summary>
/// <summary>A fighter's own weapon, as Claude made it to suit them: a name if it has one, what it is, and its story.</summary>
public sealed record Gear(string Name, string What, string Lore);

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
    // What each calling carries without a relic, six to a calling (the owner, 2026-10-29: a wider pool), chosen by the
    // character so a world always arms alike. After the chronicle airs, Claude arms each fighter to suit them (Character.Weapon).
    private static readonly Dictionary<string, string[]> Common = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] = ["a heavy longsword and a battered shield", "a notched greataxe", "a spear and a round shield painted with their colours",
            "a pair of hand-axes", "a two-handed sword taller than a child", "a flanged mace and a kite shield"],
        [Classes.Rogue] = ["a pair of long knives", "a slim rapier and a parrying dagger", "a hooked sickle-blade", "a sheaf of throwing knives and a short blade",
            "a garrotte and a stiletto", "a curved sabre worn low on the hip"],
        [Classes.Mage] = ["a carved staff of old wood", "a wand of black glass", "a staff tipped with a cracked crystal", "a book of spells chained to the belt",
            "a ring of iron that hums when they speak", "a rod of bone wound with silver"],
        [Classes.Paladin] = ["a warhammer and a shield bearing their god's sign", "a broadsword blessed at the altar", "a morning star and a tower shield",
            "a long-hafted poleaxe", "a holy relic-sword passed down their order", "a flail and a shield studded with prayers"],
        [Classes.Warlock] = ["a black-bladed dagger and a grimoire", "a staff of twisted thorn", "a skull-headed rod", "a curved blade that drinks the light",
            "a chain of iron links that moves on its own", "a mask of bone and a sickle"],
        [Classes.Ranger] = ["a longbow of yew and a hunting knife", "a recurved horn bow and a hatchet", "a heavy crossbow and a boar spear",
            "a short bow and a pair of fighting knives", "a sling and a stone-headed club", "a hunting spear and a quiver of barbed arrows"],
    };

    /// <summary>The weapon: a held weapon relic (its name, what it is, its lore), else the calling's own.</summary>
    public static (string Name, string What, string Lore, long ArtifactId) Weapon(World world, Character c)
    {
        var relic = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id && a.Type == ArtifactType.Weapon)
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        return relic is not null
            ? (relic.Name, $"a weapon of {relic.Material}" + (relic.Powers.Count > 0 ? $" ({string.Join(", ", relic.Powers.Select(p => p.Attribute is null ? p.Kind : $"+{p.Amount} {p.Attribute}"))})" : ""),
                relic.Lore, relic.Id)
            : c.Weapon is { } own ? (own.Name, own.What, own.Lore, 0)
            : ("", (Common.GetValueOrDefault(Classes.Base(Classes.Of(world, c))) ?? Common[Classes.Warrior]) is var pool ? pool[(int)(c.Id % pool.Length)] : "", "", 0);
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

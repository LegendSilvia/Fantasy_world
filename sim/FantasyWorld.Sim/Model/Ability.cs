namespace FantasyWorld.Sim.Model;

/// <summary>
/// A character's ability (the owner, 2026-10-29): what they can do in a fight that no one else can, by name and in a few
/// words, so the fights are told as their deeds. Every fighter has a signature one from their calling; more are won
/// through what befalls them (an art of their own, an awakening), with the event that gave it.
/// </summary>
public sealed record Ability(string Name, string Text, long EventId = 0);

public static class Abilities
{
    private static readonly Dictionary<string, Ability[]> Signatures = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] =
        [
            new("Iron Bulwark", "braces behind the shield and turns aside a blow that would fell another"),
            new("Cleaving Rush", "charges and hews through a guard with one great stroke"),
            new("Unbroken", "fights on through wounds that should have ended the fight"),
            new("Shield Breaker", "a blow aimed at the shield, not the foe, and the shield gives"),
            new("War Cry", "a roar that freezes the foe's arm for a heartbeat"),
            new("Blade Wall", "a whirl of steel no strike can pass"),
        ],
        [Classes.Rogue] =
        [
            new("Shadowstep", "slips out of sight and strikes from where the foe is not looking"),
            new("Poisoned Edge", "carries a blade wet with a venom that slows the foe"),
            new("Feint and Riposte", "draws the foe into a false opening and punishes it"),
            new("Smoke and Ash", "a handful of powder, and the foe fights blind"),
            new("Hamstring", "a low cut that leaves the foe slow for the rest of the fight"),
            new("Thrown Steel", "a knife thrown from the hip before the foe can close"),
        ],
        [Classes.Mage] =
        [
            new("Arcane Lance", "hurls a spear of raw light that pierces armour"),
            new("Warding Glyph", "traces a sigil that turns aside the first killing blow"),
            new("Frost Bind", "freezes the ground about the foe's feet"),
            new("Flame Lash", "a whip of fire that keeps the foe at a distance"),
            new("Mind Thorn", "a whispered word that sets the foe's thoughts reeling"),
            new("Stoneskin", "hardens their own flesh against a blow they cannot avoid"),
        ],
        [Classes.Paladin] =
        [
            new("Radiant Smite", "calls holy light into the blade at the decisive moment"),
            new("Oath of Mending", "closes their own wounds with a prayer"),
            new("Aegis of Faith", "stands unmoved while their god is with them"),
            new("Vow of Vengeance", "swears an oath mid-fight, and is stronger while it holds"),
            new("Hallowed Ground", "blesses the ground they stand on, and the foe falters on it"),
            new("Shield Bash", "drives the shield's rim into the foe's face"),
        ],
        [Classes.Warlock] =
        [
            new("Hex of Withering", "lays a curse that saps the foe's strength"),
            new("Soul Leech", "drinks the foe's vigour through a wound"),
            new("Pact Flame", "burns with a fire bought from something below"),
            new("Whispering Dark", "fills the foe's ears with voices only they can hear"),
            new("Blood Price", "pays in their own blood for a blow that cannot be turned"),
            new("Grave Chill", "a cold from beneath the earth that numbs the foe's hands"),
        ],
        [Classes.Ranger] =
        [
            new("Hunter's Mark", "marks the foe and never loses them after"),
            new("Volley", "looses arrow after arrow faster than the eye follows"),
            new("Beast Companion", "fights beside a loyal beast of the wilds"),
            new("Pinning Shot", "an arrow that nails the foe's foot to the ground"),
            new("Trapper's Ground", "has laid snares where the fight would go"),
            new("Unseen Stalker", "vanishes into cover and strikes from where none expected"),
        ],
    };

    /// <summary>The signature ability of a character's calling, chosen by their id, so a world always gives alike.</summary>
    public static Ability Signature(World world, Character c)
    {
        // After the chronicle airs, Claude makes one fitting the character (Character.Signature); else the calling's pool.
        if (c.Signature is { } own) return own;
        var list = Signatures.GetValueOrDefault(Classes.Base(Classes.Of(world, c))) ?? Signatures[Classes.Warrior];
        return list[(int)(c.Id % list.Length)];
    }

    /// <summary>Everything a character can do: their signature, then what they have won since, newest last.</summary>
    /// <summary>Each ability once, by name (a world made before arts were kept unique may hold one twice).</summary>
    public static IReadOnlyList<Ability> Of(World world, Character c) =>
        new[] { Signature(world, c) }.Concat(c.Abilities).DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The abilities as a brief reads them, for the arbiter and the chronicler.</summary>
    public static string Brief(World world, Character c) => string.Join("; ", Of(world, c).Select(a => $"{a.Name} ({a.Text})"));
}

using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// A character awakening into a new calling (the owner, 2026-10-29), shared by the deeds that cause it: at once after a
/// deed, years later when a hidden one wakes, or in the middle of a fight. The calling is named by Claude with its lore
/// (origin, what it does, its condition) and kept in the world's lore; the bearer's class becomes it, their strengths
/// grow, and they gain its power as an ability.
/// </summary>
public static class Awakenings
{
    public const string Now = "now", Slept = "slept", MidFight = "mid-fight";

    /// <summary>The engine's own callings, offline: name, origin, what it does, its condition.</summary>
    // Four to a calling (the owner, 2026-10-29: a wider pool), chosen by the character; after the chronicle airs Claude
    // makes each one fit its bearer.
    private static readonly Dictionary<string, CallingInvention[]> Own = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] =
        [
            new("Bloodsworn", "Blood spilled at the edge of death answered them, as it answered the first kings who swore on it.",
                "the more they bleed, the harder they strike", "the power feeds on their own wounds, and leaves them weaker each time it fades"),
            new("Ironheart", "Their heart stopped on the field, and started again as something harder.",
                "no blow can make them yield or fall while their heart beats", "they feel nothing, not pain, not joy, and are colder every year"),
            new("Warbringer", "The din of a thousand old battles woke in them and will not be still.",
                "those who fight beside them fight as if possessed", "they cannot refuse a fight, nor leave one unfinished"),
            new("Stormbreaker", "They stood against a thing no blade should stand against, and the blade remembered.",
                "their weapon strikes like a falling tree", "every great blow cracks the weapon a little more"),
        ],
        [Classes.Rogue] =
        [
            new("Shadowdancer", "The dark of the deep places marked them, and now every shadow is a door to them.",
                "steps from one shadow into the next as if through a door", "it fails them in full daylight, and each step leaves them colder"),
            new("Facestealer", "They looked too long into a dying foe's eyes and took something away.",
                "wears the face and voice of anyone they have wounded", "each stolen face lingers, and their own grows harder to remember"),
            new("Whisperblade", "The lost god of thieves spoke to them once, in a dark they should not have survived.",
                "their blade cuts what it cannot see", "they must leave one coin for every life they take, or the god will take theirs"),
            new("Ghostwalker", "They died for a moment, and something let them come back lighter.",
                "passes through walls and guards like smoke", "they cannot touch what they love, and forget a little more each time"),
        ],
        [Classes.Mage] =
        [
            new("Stormcaller", "The sky's old wrath, bound since the world was walled, found a way out through them.",
                "lightning falls where they point", "the storm answers only in the open, and every bolt burns their own hands"),
            new("Starseer", "The stars spoke to them on the night they nearly died, and have not stopped.",
                "sees a moment ahead in any fight", "every glimpse of what will be costs them a memory of what was"),
            new("Ashweaver", "A fire older than the gods woke in them, and they learned to shape it.",
                "spins fire into shapes that obey them", "it must burn something every day, or it burns them"),
            new("Deepspeaker", "The stones under the world answered their call when no one else would.",
                "the ground itself rises, opens or holds at their word", "the earth remembers every favour, and one day will ask for them back"),
        ],
        [Classes.Paladin] =
        [
            new("Dawnwarden", "A god's light, they say, lit in them when they should have died.",
                "a light pours from them that the dark cannot abide", "it burns only while their oath holds, and breaking it would blind them"),
            new("Oathkeeper", "They swore an oath as they lay dying, and the oath held them to the world.",
                "cannot fall while their oath is unfulfilled", "when the oath is kept, the power leaves them all at once"),
            new("Lifebinder", "A god let them take back a death that was not theirs to undo.",
                "closes wounds, their own and others', with a touch", "every wound they heal they must bear for a night themselves"),
            new("Sunhammer", "The sun broke through on the worst day of their life, and stayed in them.",
                "their strikes land like noon", "the power wanes with the light, and is gone at night"),
        ],
        [Classes.Warlock] =
        [
            new("Voidbinder", "Something from beyond the world's wall reached for them, and they caught it instead.",
                "holds a thing from beyond the world on a chain of will, and loosens it on their foes", "the chain must be held every moment; should their will slip, it turns on them"),
            new("Plaguelord", "They lived through a sickness that killed everyone around them, and the sickness stayed.",
                "carries a rot that withers whatever they touch in anger", "the sickness is patient, and eats at them too"),
            new("Soulbinder", "A dying foe's spirit clung to them, and they learned to keep it.",
                "binds the spirits of the slain to fight for them", "each spirit kept is a voice that never stops talking"),
            new("Hexmaster", "They paid a price they will not name for words that cannot be unsaid.",
                "a word of theirs falls on a foe like a curse", "every curse they lay must find a target, and sometimes it finds them"),
        ],
        [Classes.Ranger] =
        [
            new("Wildheart", "The old beasts of the land knew them for one of their own.",
                "the beasts of the land rise to fight at their side", "it answers only on wild ground, never inside walls"),
            new("Skinshifter", "They ran with wolves for a season after they were left for dead.",
                "takes the shape of a great beast in the heat of a fight", "each time it is harder to come back"),
            new("Thornwarden", "The forest grew over them where they fell, and let them go only changed.",
                "the trees and roots of the land fight for them", "they must return to the forest each year, or wither"),
            new("Windrunner", "They outran a storm that should have killed them, and the wind kept them.",
                "moves faster than any arrow they loose", "the wind will not let them rest in one place for long"),
        ],
    };

    /// <summary>The calling a character awakens into, as Claude names it (else the engine's own), and recorded in the lore.</summary>
    public static Calling Invent(World world, Character c, WorldEvent deed, IInventor inventor, string how, long day)
    {
        var calling = Classes.Of(world, c);
        var baseClass = Classes.Base(calling);
        var known = world.Callings.Select(k => k.Name).ToList();
        var facts = $"{c.Name}, a {Classes.Title(calling)}, traits {string.Join(", ", c.Traits)}; awakened by what befell them: {Spaced(deed.Type)} in Year {deed.Date.Year}"
                    + how switch
                    {
                        Slept => "; it lay hidden within them for years, unrevealed, and only now wakes.",
                        MidFight => "; it lay hidden within them, and wakes now, in the middle of a fight for their life.",
                        _ => ".",
                    }
                    + (world.Legend is { } legend ? $" The world's legend: {legend.Summary}" : "");
        var pool = Own.TryGetValue(baseClass, out var o) ? o : Own[Classes.Warrior];
        var own = pool[(int)((ulong)c.Id % (ulong)pool.Length)];
        var named = inventor.InventCalling(new CallingBrief($"{c.Ref}|awakening|{day}", facts, Classes.Title(baseClass), known)) ?? own;
        return new Calling(named.Name, baseClass, named.Origin, named.Does, named.Condition, c.Id, 0, day);
    }

    /// <summary>The bearer takes the calling: their class, their strengths, its power as an ability; the lore keeps it.</summary>
    public static void Take(World world, Character c, Calling calling)
    {
        c.Class = Classes.Awaken(calling.Base, calling.Name);
        c.Abilities.Add(new Ability(calling.Name, $"{calling.Does}; {calling.Condition}"));
        c.Dormant = 0;
        var (a, b) = calling.Base switch
        {
            Classes.Rogue => ("intrigue", "prowess"), Classes.Mage => ("lore", "intrigue"), Classes.Paladin => ("martial", "prowess"),
            Classes.Warlock => ("lore", "intrigue"), Classes.Ranger => ("prowess", "stewardship"), _ => ("prowess", "martial"),
        };
        c.Attributes.Set(a, c.Attributes.Get(a) + 2);
        c.Attributes.Set(b, c.Attributes.Get(b) + 2);
        if (world.Callings.All(k => k.Name != calling.Name)) world.Callings.Add(calling);
    }

    /// <summary>The awakening as news, caused by <paramref name="cause"/>; the lore's entry is tied to its first telling.</summary>
    public static WorldEvent Announce(SimContext ctx, Character c, Calling calling, string from, long cause, string how, EntityRef? place, long slept = 0)
    {
        var payload = new Dictionary<string, string>
        {
            ["kind"] = TransformSystem.Awakening, ["title"] = calling.Name, ["text"] = calling.Does, ["from"] = from, ["to"] = c.Class, ["how"] = how,
        };
        if (how == Slept) payload["slept"] = slept.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ev = ctx.Emit(new EventDraft("CharacterTransformed", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref }, Place: place,
            Payload: payload, Causes: new[] { new Cause(cause, CauseKind.Trigger, 1.0) },
            Importance: Fates.Importance(c, how == Now ? 55 : 65), Tags: new[] { "Characters", "Deeds", "Magic" }));
        var i = ctx.World.Callings.FindIndex(k => k.Name == calling.Name && k.EventId == 0);
        if (i >= 0) ctx.World.Callings[i] = ctx.World.Callings[i] with { EventId = ev.Id };
        c.Abilities[^1] = c.Abilities[^1] with { EventId = ev.Id };
        c.ChangedDay = ctx.Date.Day;
        return ev;
    }

    private static string Spaced(string type) => string.Concat(type.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString().ToLowerInvariant()));
}

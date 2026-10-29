using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What befalls a character can change them entirely (the owner, 2026-10-29): coming back from the brink of death, slaying
/// a legend, uncovering a secret of the world, killing a foe. Each month the deeds since the last are read, and those they
/// touched may be changed: sworn to vengeance on who nearly killed them, stirred by a new desire, the makers of a fighting
/// art of their own (a new ability), or awakened into a new class Claude names, which stands where their old one stood
/// on the wheel and fights a tenth stronger. Each change is news (CharacterTransformed), caused by the deed, and an
/// episode tells it right after it. No one is changed twice within <see cref="QuietYears"/> years.
/// </summary>
public sealed class TransformSystem(IInventor? inventor = null) : ISimSystem
{
    public const string Cursor = "transform.cursor";
    public const int QuietYears = 5;
    public const string Awakening = "awakening", Vengeance = "vengeance", Desire = "desire", Art = "art";

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "Transform";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        // A world that had none of this begins from now: its past deeds are history, not news.
        if (!world.Markers.TryGetValue(Cursor, out var cursor))
        {
            world.Markers[Cursor] = ctx.Log.NextId - 1;
            return;
        }
        var deeds = ctx.Log.Since(cursor).ToList();
        world.Markers[Cursor] = ctx.Log.NextId - 1;
        foreach (var deed in deeds)
            foreach (var (who, chance, foe) in Touched(world, deed))
                if (world.Characters.TryGetValue(who, out var c) && c.IsActive && c.Stage(ctx.Date) != LifeStage.Child
                    && (c.ChangedDay == 0 || ctx.Date.Day - c.ChangedDay >= QuietYears * WorldDate.DaysPerYear)
                    && !SagaSystem.IsDemonLord(world, c) && ctx.Rng.Chance(chance))
                    Change(ctx, c, deed, foe);
    }

    /// <summary>Who a deed touched, how likely it is to change them, and who wronged them (for a vengeance).</summary>
    private static IEnumerable<(long Who, double Chance, long Foe)> Touched(World world, WorldEvent deed)
    {
        long Id(string role) => deed.Actors.TryGetValue(role, out var a) && a.Kind == Character.Kind ? a.Id : 0;
        switch (deed.Type)
        {
            case "SecretRevealed":
                yield return (Id("delver"), 0.6, 0);
                break;
            case "MonsterSlain" when deed.Actors.TryGetValue("monster", out var m) && world.Monsters.TryGetValue(m.Id, out var beast) && beast.Legendary:
                yield return (Id("hunter"), 0.4, 0);
                break;
            case "MonsterHuntFailed":
                yield return (Id("hunter"), 0.12, 0);
                break;
            case "DuelFought":
            {
                var outcome = deed.Payload.GetValueOrDefault("outcome") ?? "";
                var challengerWon = outcome.StartsWith("attacker", StringComparison.Ordinal);
                var (winner, loser) = challengerWon ? (Id("challenger"), Id("defender")) : (Id("defender"), Id("challenger"));
                // The beaten who live came back from the brink; the one who killed has tasted it.
                if (!outcome.EndsWith("kills", StringComparison.Ordinal) && outcome != "both_die") yield return (loser, 0.3, winner);
                else if (outcome != "both_die") yield return (winner, 0.15, 0);
                break;
            }
            case "ChampionsClashed" when deed.Payload.GetValueOrDefault("outcome") != "slew":
                yield return (Id("vanquished"), 0.3, Id("victor"));
                break;
        }
    }

    private void Change(SimContext ctx, Character c, WorldEvent deed, long foeId)
    {
        var world = ctx.World;
        var foe = foeId != 0 && world.Characters.TryGetValue(foeId, out var f) && f.IsActive ? f : null;
        var uncanny = deed.Type is "SecretRevealed" or "MonsterSlain";
        var options = new List<(string Kind, double Weight)>
        {
            (Awakening, uncanny ? 60 : Classes.IsAwakened(c.Class) ? 0 : 12),
            (Art, uncanny ? 30 : 30),
            (Desire, uncanny ? 10 : 25),
            (Vengeance, foe is not null && c.Ambition != Ambitions.Vengeance && !c.Has(Traits.Craven) ? 40 + (c.Has(Traits.Wrathful) ? 30 : 0) : 0),
        };
        if (Classes.IsAwakened(c.Class)) options[0] = (Awakening, 0);
        var kind = Pick(ctx.Rng, options);
        var calling = Classes.Of(world, c);
        string title = "", text = "", from = "", to = "";
        var facts = $"{c.Name}, a {Classes.Title(calling)}, traits {string.Join(", ", c.Traits)}; changed by what befell them: {Spaced(deed.Type)}.";
        switch (kind)
        {
            case Awakening:
            {
                var fallback = Awakened.TryGetValue(Classes.Base(calling), out var aw) ? aw : (Name: "Awakened", Text: "a power that was always in them woke at last");
                var named = _inventor.Invent(new InventionBrief("awakened class", $"{c.Ref}|awakening|{ctx.Date.Day}",
                    facts + $" They awaken into a new calling grown from the {Classes.Title(Classes.Base(calling))}'s: name it in one or two words, and tell what they can now do."
                    + (Awakenings(world) is { Count: > 0 } known ? $" Callings already awakened in the world, not to be repeated: {string.Join(", ", known)}." : ""),
                    fallback.Name));
                title = Clean(named.Name, 30) is { Length: > 0 } n && !n.Contains(':') ? n : fallback.Name;
                text = named.Lore is { Length: > 0 } lore ? Clean(lore, 300) : fallback.Text;
                (from, to) = (c.Class, Classes.Awaken(calling, title));
                c.Class = to;
                c.Abilities.Add(new Ability(title, text));
                Raise(c, Classes.Base(calling), 2);
                break;
            }
            case Art:
            {
                var fallback = Arts.TryGetValue(Classes.Base(calling), out var art) ? art : (Name: "The Last Stand", Text: "a way of fighting no one else knows");
                var named = _inventor.Invent(new InventionBrief("fighting art", $"{c.Ref}|art|{ctx.Date.Day}",
                    facts + " From it they make an art of fighting all their own: name it, and tell what it does in a fight.", fallback.Name));
                title = Clean(named.Name, 40) is { Length: > 0 } n ? n : fallback.Name;
                text = named.Lore is { Length: > 0 } lore ? Clean(lore, 300) : fallback.Text;
                c.Abilities.Add(new Ability(title, text));
                Raise(c, Classes.Base(calling), 1);
                break;
            }
            case Vengeance:
                (from, to) = (c.Ambition, Ambitions.Vengeance);
                c.Ambition = Ambitions.Vengeance;
                c.AmbitionTarget = foe!.Id;
                text = $"sworn to destroy {foe.Name}";
                break;
            default:
            {
                var wants = new[] { Ambitions.Renown, Ambitions.Artifact, Ambitions.Magic, Ambitions.Wealth, Ambitions.Family }.Where(a => a != c.Ambition).ToList();
                var want = wants[ctx.Rng.NextInt(0, wants.Count)];
                (from, to) = (c.Ambition, want);
                c.Ambition = want;
                c.AmbitionTarget = 0;
                text = Desires[want];
                break;
            }
        }
        c.ChangedDay = ctx.Date.Day;
        var actors = new Dictionary<string, EntityRef> { ["who"] = c.Ref };
        if (kind == Vengeance) actors["foe"] = foe!.Ref;
        ctx.Emit(new EventDraft("CharacterTransformed", Actors: actors, Place: deed.Place,
            Payload: new Dictionary<string, string> { ["kind"] = kind, ["title"] = title, ["text"] = text, ["from"] = from, ["to"] = to },
            Causes: new[] { new Cause(deed.Id, CauseKind.Trigger, 1.0) },
            Importance: Fates.Importance(c, kind == Awakening ? 55 : 40), Tags: new[] { "Characters", "Deeds" }));
    }

    /// <summary>The awakened callings the world has seen.</summary>
    private static IReadOnlyList<string> Awakenings(World world) =>
        world.Characters.Values.Where(c => Classes.IsAwakened(c.Class)).Select(c => Classes.Title(c.Class)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static string Pick(Rng rng, IReadOnlyList<(string Kind, double Weight)> options)
    {
        var total = options.Sum(o => o.Weight);
        var roll = rng.NextDouble() * total;
        foreach (var (kind, weight) in options)
        {
            if (roll < weight) return kind;
            roll -= weight;
        }
        return options.Last(o => o.Weight > 0).Kind;
    }

    /// <summary>The strengths a calling rests on grow with what changed them.</summary>
    private static void Raise(Character c, string calling, int by)
    {
        var (a, b) = calling switch
        {
            Classes.Rogue => ("intrigue", "prowess"), Classes.Mage => ("lore", "intrigue"), Classes.Paladin => ("martial", "prowess"),
            Classes.Warlock => ("lore", "intrigue"), Classes.Ranger => ("prowess", "stewardship"), _ => ("prowess", "martial"),
        };
        c.Attributes.Set(a, c.Attributes.Get(a) + by);
        c.Attributes.Set(b, c.Attributes.Get(b) + by);
    }

    private static string Clean(string text, int max)
    {
        var t = text.Replace("\"", "", StringComparison.Ordinal).Trim();
        return t.Length <= max ? t : t[..max].TrimEnd();
    }

    private static string Spaced(string type) => string.Concat(type.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString().ToLowerInvariant()));

    /// <summary>The engine's own awakened callings, offline.</summary>
    private static readonly Dictionary<string, (string Name, string Text)> Awakened = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] = ("Bloodsworn", "the more they bleed, the harder they strike"),
        [Classes.Rogue] = ("Shadowdancer", "they move between one shadow and the next as if through a door"),
        [Classes.Mage] = ("Stormcaller", "the sky answers them, and lightning falls where they point"),
        [Classes.Paladin] = ("Dawnwarden", "light pours from them that the dark cannot abide"),
        [Classes.Warlock] = ("Voidbinder", "they hold a thing from beyond the world on a chain of will"),
        [Classes.Ranger] = ("Wildheart", "the beasts of the land rise to fight at their side"),
    };

    /// <summary>The engine's own fighting arts, offline.</summary>
    private static readonly Dictionary<string, (string Name, string Text)> Arts = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] = ("The Broken Guard", "a stroke that comes when the foe believes them beaten"),
        [Classes.Rogue] = ("The Second Knife", "a hidden blade drawn as the first is parried"),
        [Classes.Mage] = ("The Mirrored Spell", "turns a foe's own strength back on them"),
        [Classes.Paladin] = ("The Kneeling Oath", "rises from the ground renewed by a whispered vow"),
        [Classes.Warlock] = ("The Borrowed Breath", "steals the foe's next breath and fights on with it"),
        [Classes.Ranger] = ("The Long Shot", "a shot loosed from where no archer could have stood"),
    };

    private static readonly Dictionary<string, string> Desires = new(StringComparer.Ordinal)
    {
        [Ambitions.Renown] = "a hunger for glory, to be remembered whatever it costs",
        [Ambitions.Artifact] = "a craving for the relics of the old world",
        [Ambitions.Magic] = "a longing to master the powers they glimpsed",
        [Ambitions.Wealth] = "a resolve never to be poor or powerless again",
        [Ambitions.Family] = "a wish to live for those they love while they still can",
    };
}

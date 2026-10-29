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

    /// <summary>
    /// An awakening may lie hidden (the owner, 2026-10-29: "like a demon hiding inside, not revealing itself"): a third of them
    /// sleep for one to five years after the deed that planted them, untold, then wake as news of their own.
    /// </summary>
    public const double DormantChance = 0.35;
    public const string DormantKind = "dormant awakening";

    public IEnumerable<string> HandledKinds => [DormantKind];

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var world = ctx.World;
        if (!long.TryParse(action.Data.GetValueOrDefault("who"), out var who) || !world.Characters.TryGetValue(who, out var c) || !c.IsActive
            || Classes.IsAwakened(c.Class) || SagaSystem.IsDemonLord(world, c)) return;
        // A hidden awakening that already broke out in a fight has nothing left to wake.
        if (!long.TryParse(action.Data.GetValueOrDefault("deed"), out var deedId) || c.Dormant != deedId || !ctx.Log.TryGet(deedId, out var deed)) return;
        Change(ctx, c, deed, 0, woken: true);
    }

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

    private void Change(SimContext ctx, Character c, WorldEvent deed, long foeId, bool woken = false)
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
        var kind = woken ? Awakening : Pick(ctx.Rng, options);
        // Some awakenings sleep: planted now, told only when they wake.
        if (kind == Awakening && !woken && ctx.Rng.Chance(DormantChance))
        {
            ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(WorldDate.DaysPerYear, 5 * WorldDate.DaysPerYear + 1)), DormantKind,
                new Dictionary<string, string> { ["who"] = c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["deed"] = deed.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            c.ChangedDay = ctx.Date.Day;
            c.Dormant = deed.Id;
            return;
        }
        var slept = woken ? Math.Max(1, (ctx.Date.Day - deed.Date.Day) / WorldDate.DaysPerYear) : 0;
        var calling = Classes.Of(world, c);
        string title = "", text = "", from = "", to = "";
        var facts = $"{c.Name}, a {Classes.Title(calling)}, traits {string.Join(", ", c.Traits)}; changed by what befell them: {Spaced(deed.Type)}"
                    + (woken ? $", in Year {deed.Date.Year}; it lay hidden within them for years, unrevealed, and only now wakes." : ".");
        switch (kind)
        {
            case Awakening:
            {
                var awakened = Awakenings.Invent(world, c, deed, _inventor, woken ? Awakenings.Slept : Awakenings.Now, ctx.Date.Day);
                var was = c.Class;
                Awakenings.Take(world, c, awakened);
                Awakenings.Announce(ctx, c, awakened, was, deed.Id, woken ? Awakenings.Slept : Awakenings.Now, deed.Place, slept);
                return;
            }
            case Art:
            {
                var known = c.Abilities.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var fallback = (Arts.TryGetValue(Classes.Base(calling), out var arts) ? arts : [])
                    .Append((Name: "The Last Stand", Text: "a way of fighting no one else knows")).FirstOrDefault(a => !known.Contains(a.Name));
                if (fallback.Name is null) { Raise(c, Classes.Base(calling), 1); return; } // every art of their calling already theirs
                var named = _inventor.Invent(new InventionBrief("fighting art", $"{c.Ref}|art|{ctx.Date.Day}",
                    facts + " From it they make an art of fighting all their own: name it, and tell what it does in a fight.", fallback.Name));
                title = Clean(named.Name, 40) is { Length: > 0 } n ? n : fallback.Name;
                text = named.Lore is { Length: > 0 } lore ? Clean(lore, 300) : fallback.Text;
                // An art they already have is not learned twice (an inventor may name an old one again).
                if (known.Contains(title)) { Raise(c, Classes.Base(calling), 1); return; }
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
            Importance: Fates.Importance(c, 40), Tags: new[] { "Characters", "Deeds" }));
    }

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

    /// <summary>The engine's own fighting arts, offline.</summary>
    // Several arts per calling, so one who masters a second (offline, with no inventor) never learns the same one twice.
    private static readonly Dictionary<string, (string Name, string Text)[]> Arts = new(StringComparer.Ordinal)
    {
        [Classes.Warrior] = [("The Broken Guard", "a stroke that comes when the foe believes them beaten"), ("The Iron Wall", "a guard no blow has broken, held until the foe tires"),
            ("The Red Harvest", "a sweeping cut that clears all before it"), ("The Last Charge", "a rush that gathers every ounce of strength into one blow"),
            ("The Turning Wheel", "a guard that flows into a strike and back without a pause"), ("The Hammer's Fall", "an overhead blow that drives the foe to their knees"),
            ("The Shield Wall of One", "holds a narrow place against any number"), ("The Grinding Advance", "a step forward with every blow, never one back")],
        [Classes.Rogue] = [("The Second Knife", "a hidden blade drawn as the first is parried"), ("The Vanishing Step", "is never where the blow lands"),
            ("The Whispered Cut", "a wound the foe does not feel until it is too late"), ("The False Opening", "offers a gap in the guard that is a trap"),
            ("The Dancing Blades", "two knives that never strike the same way twice"), ("The Kiss of Venom", "a scratch is all it takes"),
            ("The Turned Cloak", "a cloak flung over the foe's blade, a knife beneath it"), ("The Long Wait", "lets the foe tire, then ends it in one stroke")],
        [Classes.Mage] = [("The Mirrored Spell", "turns a foe's own strength back on them"), ("The Silent Word", "a spell cast without a sound or a gesture"),
            ("The Burning Sigil", "a mark that flares when the foe draws near"), ("The Unmaking", "unravels another's spell as it is woven"),
            ("The Glass Storm", "a cloud of shards that cut whatever they touch"), ("The Slow Hour", "makes the moment around the foe drag"),
            ("The Iron Word", "a spoken command the foe's body obeys against their will"), ("The Second Shadow", "a double of themselves the foe cannot tell apart")],
        [Classes.Paladin] = [("The Kneeling Oath", "rises from the ground renewed by a whispered vow"), ("The Shining Ward", "a light that turns aside the darkest blow"),
            ("The Judgement", "a strike that falls hardest on the guilty"), ("The Shield of Faith", "stands unmoved while the faithful behind them hold"),
            ("The Penitent Blade", "fights hardest after a wound, as if it were a penance"), ("The Morning Star", "a strike that blazes like the sun rising"),
            ("The Martyr's Stand", "takes the blow meant for another and returns it"), ("The Litany", "a prayer that steadies every stroke")],
        [Classes.Warlock] = [("The Borrowed Breath", "steals the foe's next breath and fights on with it"), ("The Bargain", "trades a wound of their own for a worse one given"),
            ("The Creeping Dark", "a shadow that slows every step the foe takes"), ("The Bound Name", "speaks the foe's name and holds them fast"),
            ("The Hollow Gaze", "a look that drains the courage from the foe"), ("The Black Mirror", "turns the foe's fear into a shape that fights them"),
            ("The Unpaid Debt", "calls in a favour from something below at the worst moment"), ("The Witherhand", "a touch that ages what it holds")],
        [Classes.Ranger] = [("The Long Shot", "a shot loosed from where no archer could have stood"), ("The Hunter's Patience", "waits, unseen, for the one moment that matters"),
            ("The Twin Arrows", "two shafts loosed as one"), ("The Wild Call", "summons the beasts of the land to their side"),
            ("The Running Fight", "shoots and gives ground, shoots and gives ground, until the foe is spent"), ("The Rooted Stance", "stands like an old tree, and does not fall"),
            ("The Falcon's Eye", "sees the one weakness in any armour"), ("The Silent Hunt", "is never heard until the arrow lands")],
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

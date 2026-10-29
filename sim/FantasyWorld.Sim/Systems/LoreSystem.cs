using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The world's songs are revised every <see cref="RevisionYears"/> years (the owner, 2026-10-29): the greatest deeds of those
/// years become tales, sung from then on; older tales may be forgotten (only before the chronicle first airs); and a forgotten tale is a truth that can be
/// uncovered again, as a secret of the world, at the bottom of a labyrinth (which none reaches before the chronicle airs).
/// Each revision is news.
/// </summary>
public sealed class LoreSystem(IInventor? inventor = null) : ISimSystem
{
    public const int RevisionYears = 25;

    /// <summary>At most this many deeds of an age become tales.</summary>
    public const int TalesPerAge = 3;

    /// <summary>A tale is forgotten only once it is this old, and then at each revision with this chance.</summary>
    public const int ForgetAfterYears = 50;
    public const double ForgetChance = 0.35;

    /// <summary>What kind of deed the songs keep: wars ended, realms fallen or founded, great beasts slain, the saga's turns.</summary>
    private static readonly HashSet<string> Sung = new(StringComparer.Ordinal)
    {
        "WarEnded", "RealmFell", "RealmFounded", "MonsterSlain", "LabyrinthCleared", "HeroChosen", "DemonLordRose", "DemonLordSealed",
        "DemonLordSlain", "RevolutionWon", "BattleFought", "DuelFought", "CharacterTransformed", "SecretRevealed",
    };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "lore";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var year = ctx.Date.Year;
        if (year % RevisionYears != 0 || world.Tales.Any(t => t.SungFrom == year)) return;
        var narrator = new TemplateNarrator(world);
        var since = (long)(year - RevisionYears) * Core.WorldDate.DaysPerYear;
        var sung = world.Tales.Select(t => t.EventId).ToHashSet();
        var deeds = ctx.Log.All.Where(e => e.Date.Day >= since && Sung.Contains(e.Type) && !sung.Contains(e.Id) && e.Importance >= 40)
            .OrderByDescending(e => e.Importance).ThenBy(e => e.Id).Take(TalesPerAge).OrderBy(e => e.Id).ToList();
        var remembered = new List<Tale>();
        foreach (var e in deeds)
        {
            var deed = narrator.Sentence(e);
            var named = _inventor.Invent(new InventionBrief("tale", $"tale|{e.Id}",
                $"A deed of Year {e.Date.Year} the world's songs now tell: {deed} Name the tale, and tell it as the people sing it, in two sentences.",
                $"The Song of Year {e.Date.Year}"));
            remembered.Add(new Tale(named.Name, named.Lore is { Length: > 0 } lore ? lore : deed, e.Date.Year, e.Id, year));
        }
        var forgotten = new List<Tale>();
        world.Tales = world.Tales.Select(t =>
        {
            // Only the offline history forgets (the owner, 2026-10-29): once the chronicle airs, viewers come back to the lore,
            // so nothing sung is lost after that; new deeds are still added.
            if (t.Forgotten || world.LastBroadcastDate is not null || year - t.Year < ForgetAfterYears || !ctx.Rng.Chance(ForgetChance)) return t;
            var lost = t with { Forgotten = true, ForgottenIn = year };
            forgotten.Add(lost);
            return lost;
        }).Concat(remembered).ToList();
        if (remembered.Count == 0 && forgotten.Count == 0) return;
        string Titles(IEnumerable<Tale> tales) => English.List(tales.Select(t => English.InProse(t.Title)).ToList());
        var told = remembered.Count > 0 && forgotten.Count > 0
            ? $"now tell of {Titles(remembered)}, and of {Titles(forgotten)} they sing no more"
            : remembered.Count > 0 ? $"now tell of {Titles(remembered)}" : $"no longer tell of {Titles(forgotten)}";
        var payload = new Dictionary<string, string>
        {
            ["told"] = told,
            ["remembered"] = Titles(remembered),
            ["forgotten"] = Titles(forgotten),
        };
        ctx.Emit(new EventDraft("LoreRevised", Payload: payload,
            Causes: deeds.Select(d => new Cause(d.Id, CauseKind.Trigger, 0.6)).Take(Events.EventLog.MaxCauses).ToArray(),
            Importance: 35, Tags: new[] { "Culture" }));
    }

    /// <summary>A forgotten tale not yet uncovered, oldest first: what the bottom of a labyrinth may give back.</summary>
    public static Tale? Buried(World world) => world.Tales.Where(t => t.Forgotten && !t.Uncovered).OrderBy(t => t.Year).ThenBy(t => t.EventId).FirstOrDefault();
}

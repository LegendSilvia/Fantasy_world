using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// A new identity after a tragedy (the owner, 2026-10-29), yearly: one bereaved, beaten, wounded, exiled or unmasked may
/// become someone else, and how is the cause of it: they forget who they were (a blow, a fever), so their old hatreds and
/// longings go with their memory; the world forgets them (a curse, a bargain with a god, a spell gone wrong), so no one
/// remembers what they did; they cast off their name to hide, so their enemies lose them; or grief makes them over, and
/// they take a new name for the life that is left. Claude names them and tells it (the rules do, offline). At most a few
/// a year, each person once; the rolls come from the person, never the world's dice.
/// </summary>
public sealed class IdentitySystem(IInventor? inventor = null) : ISimSystem
{
    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public const string Forgot = "forgot", Forgotten = "forgotten", Hidden = "hidden", Reborn = "reborn";

    /// <summary>The most who change a year.</summary>
    public const int MostAYear = 3;

    public string Name => "identity";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        var touched = new List<(Character Who, WorldEvent Why, string Plight, int Chance)>();
        for (var i = ctx.Log.All.Count - 1; i >= 0 && ctx.Log.All[i].Date.Day > since; i--)
            foreach (var (id, plight, chance) in Touched(world, ctx.Log.All[i]))
                if (world.Characters.TryGetValue(id, out var c) && c.IsActive && c.FormerNames.Count == 0 && c.Stage(ctx.Date) != LifeStage.Child
                    && !SagaSystem.IsDemonLord(world, c) && touched.All(t => t.Who.Id != id))
                    touched.Add((c, ctx.Log.All[i], plight, chance));
        foreach (var (who, why, plight, chance) in touched.OrderBy(t => t.Why.Id).ThenBy(t => t.Who.Id)
                     .Where(t => Hash.Percent(t.Who.Id, 9300, ctx.Date.Year) < t.Chance).Take(MostAYear))
            Change(ctx, who, why, plight);
    }

    /// <summary>Who an event struck hard enough to change, how (grief, a wound, exile, shame) and how likely, in a hundred.</summary>
    private static IEnumerable<(long Who, string Plight, int Chance)> Touched(World world, WorldEvent ev)
    {
        long Id(string role) => ev.Actors.TryGetValue(role, out var a) && a.Kind == Character.Kind ? a.Id : 0;
        switch (ev.Type)
        {
            case "CharacterDied" when world.Characters.TryGetValue(Id("deceased"), out var dead):
                foreach (var kin in dead.Children.Append(dead.SpouseId)) yield return (kin, "grief", 4);
                break;
            case "DuelFought":
            {
                var outcome = ev.Payload.GetValueOrDefault("outcome") ?? "";
                var loser = outcome.StartsWith("attacker", StringComparison.Ordinal) ? Id("defender") : Id("challenger");
                if (!outcome.EndsWith("kills", StringComparison.Ordinal) && outcome != "both_die") yield return (loser, "wound", 5);
                break;
            }
            case "WoundedInBattle":
                yield return (Id("character"), "wound", 5);
                break;
            case "FledIntoExile":
                yield return (Id("character"), "exile", 15);
                break;
            case "AssassinationFoiled":
                yield return (Id("plotter"), "shame", 20);
                yield return (Id("target"), "terror", 5);
                break;
            case "FalselyAccused":
                yield return (Id("accused"), "exile", 25);
                break;
            case "GuestRightBroken" when world.Characters.TryGetValue(Id("guest"), out var slain):
                foreach (var kin in slain.Children.Append(slain.SpouseId)) yield return (kin, "grief", 10);
                break;
            case "CulpritUnmasked":
                yield return (Id("culprit"), "shame", 20);
                break;
        }
    }

    /// <summary>The ways a plight can make someone over, by weight: a wound takes memory, shame and exile hide, grief remakes.</summary>
    private static (string How, int Weight)[] Ways(Character c, string plight)
    {
        var magic = Classes.Base(c.Class) is Classes.Mage or Classes.Warlock;
        return plight switch
        {
            "wound" => [(Forgot, 60), (Reborn, 30), (Forgotten, magic ? 20 : 5)],
            "exile" or "shame" => [(Hidden, 70), (Forgotten, magic ? 30 : 10), (Reborn, 10)],
            "terror" => [(Hidden, 50), (Forgotten, 20), (Reborn, 20)],
            _ => [(Reborn, 50), (Forgotten, magic ? 40 : 20), (Forgot, 15)],
        };
    }

    private static readonly string[] ForgottenCauses = ["a witch's curse", "a bargain struck with a god", "a spell gone wrong", "the grief of the old powers"];

    private static readonly string[] Epithets = ["the Grey", "the Bereft", "the Unbowed", "the Ashen", "the Silent", "the Wanderer", "the Hollow", "the Returned"];

    private void Change(SimContext ctx, Character c, WorldEvent why, string plight)
    {
        var world = ctx.World;
        var year = ctx.Date.Year;
        var ways = Ways(c, plight);
        var roll = Hash.Percent(c.Id, 9301, year) * ways.Sum(w => w.Weight) / 100;
        var how = ways[^1].How;
        foreach (var (way, weight) in ways)
        {
            if (roll < weight) { how = way; break; }
            roll -= weight;
        }
        var spell = ForgottenCauses[Hash.Percent(c.Id, 9302, year) % ForgottenCauses.Length];
        var culture = world.Cultures.GetValueOrDefault(c.CultureId);
        var given = new NameGen(new Rng(unchecked((ulong)c.Id * 0x9E3779B97F4A7C15UL + (ulong)year)), culture?.Style).Given();
        var fallback = how == Reborn ? $"{c.Name} {Epithets[Hash.Percent(c.Id, 9303, year) % Epithets.Length]}" : given;
        var told = how switch
        {
            Forgot => "woke from it with no memory of who they had been",
            Forgotten => $"was forgotten by everyone who had known them, by {spell}",
            Hidden => "cast off their name to hide from those who knew it",
            _ => "swore off the life they had lived",
        };
        var invention = _inventor.Invent(new InventionBrief("identity", c.Ref.ToString(),
            $"{c.Name}, a {Classes.Title(Classes.Of(world, c))} (traits {string.Join(", ", c.Traits)}), after {Spaced(why.Type)} in year {why.Date.Year}, "
            + $"{told}, and takes a new name for who they are now. Give only the new name they are known by.", fallback));
        var name = invention.Name.Length is > 0 and <= 40 && invention.Name != c.Name ? invention.Name : fallback;
        var was = c.Name;
        c.FormerNames.Add(was);
        c.Name = name;
        switch (how)
        {
            case Forgot:
                // Their old hatreds and loves went with their memory, and they want something new. (A memory emptied of
                // feeling stays in the record, so the chronicle still keeps what happened.)
                Fade(c.Memories, _ => true);
                c.AmbitionTarget = 0;
                break;
            case Forgotten:
                foreach (var other in world.Characters.Values) Fade(other.Memories, m => m.About == c.Id);
                break;
            case Hidden:
                foreach (var other in world.Characters.Values) Fade(other.Memories, m => m.About == c.Id && m.Feeling < 0);
                break;
        }
        using var lore = ctx.Annotate("lore", invention.Lore);
        ctx.Emit(new EventDraft("IdentityChanged", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref },
            Place: world.Settlements[c.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["from"] = was, ["to"] = name, ["how"] = told, ["kind"] = how },
            Causes: new[] { new Cause(why.Id, CauseKind.Trigger, 0.9) }, Importance: Fates.Importance(c, 50), Tags: new[] { "Characters", "Deeds" }));
        c.ChangedDay = ctx.Date.Day;
    }

    private static void Fade(List<Memory> memories, Func<Memory, bool> which)
    {
        for (var i = 0; i < memories.Count; i++)
            if (which(memories[i])) memories[i] = memories[i] with { Feeling = 0, Weight = 0 };
    }

    private static string Spaced(string type) => string.Concat(type.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString().ToLowerInvariant()));
}

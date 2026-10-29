using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What a character strives for can be won, or given up (the owner, 2026-10-29: "will he ever achieve his goals"): each year
/// a goal reached is told as news (renown won, a great treasure held, magic mastered, a family made) and they set their heart
/// on something new; one thrown back again and again with nothing won gives it up, unless they are unyielding.
/// </summary>
public sealed class GoalSystem : ISimSystem
{
    /// <summary>Renown that answers a hunger for it; magic mastered at this lore.</summary>
    public const double RenownWon = 70;
    public const int LoreMastered = 18;

    /// <summary>So many failures within <see cref="FailureYears"/> years, with nothing won, and they give up.</summary>
    public const int GiveUpAfter = 5, FailureYears = 10;

    private static readonly HashSet<string> Failures = new(StringComparer.Ordinal)
    {
        "MonsterHuntFailed", "LabyrinthDelveFailed", "TheftFoiled", "DelveFailed",
    };

    private static readonly HashSet<string> Successes = new(StringComparer.Ordinal)
    {
        "MonstersSlain", "MonsterSlain", "LabyrinthDelved", "LabyrinthCleared", "ArtifactFound", "ArtifactStolen", "DuelFought",
    };

    public string Name => "goals";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - (long)FailureYears * Core.WorldDate.DaysPerYear;
        var recent = ctx.Log.All.Where(e => e.Date.Day >= since && (Failures.Contains(e.Type) || Successes.Contains(e.Type))).ToList();
        foreach (var c in world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child && c.Ambition is not (Ambitions.Vengeance or "")).OrderBy(c => c.Id).ToList())
        {
            var goal = c.Ambition;
            if (Won(world, ctx.Log, c, since))
            {
                var next = Next(c, goal, ctx);
                c.Ambition = next;
                ctx.Emit(new EventDraft("AmbitionAchieved", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref },
                    Payload: new Dictionary<string, string> { ["goal"] = Goal(goal), ["next"] = Goal(next) },
                    Importance: Fates.Importance(c, 14), Tags: new[] { "Characters" }));
                continue;
            }
            if (c.Has(Traits.Unyielding)) continue; // they never give up
            var theirs = recent.Where(e => e.Actors.Values.Contains(c.Ref)).ToList();
            var failed = theirs.Count(e => Failures.Contains(e.Type));
            if (failed < GiveUpAfter || theirs.Any(e => Successes.Contains(e.Type) && !e.Type.StartsWith("Duel", StringComparison.Ordinal))) continue;
            var turned = Next(c, goal, ctx);
            c.Ambition = turned;
            ctx.Emit(new EventDraft("AmbitionAbandoned", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref },
                Payload: new Dictionary<string, string>
                {
                    ["goal"] = Goal(goal), ["next"] = Goal(turned), ["failures"] = English.Number(failed),
                },
                Causes: theirs.Where(e => Failures.Contains(e.Type)).OrderByDescending(e => e.Id).Take(Events.EventLog.MaxCauses)
                    .Select(e => new Cause(e.Id, CauseKind.Trigger, 0.6)).ToArray(),
                Importance: Fates.Importance(c, 10), Tags: new[] { "Characters" }));
        }
    }

    /// <summary>Whether what they strive for is theirs now.</summary>
    public static bool Won(World world, EventLog log, Character c, long since) => c.Ambition switch
    {
        Ambitions.Renown => c.Renown >= RenownWon
                            || log.Involving(c.Ref).Any(e => e.Date.Day >= since && e.Type == "MonsterSlain" && e.Actors.TryGetValue("monster", out var m)
                                                            && world.Monsters.TryGetValue(m.Id, out var beast) && beast.Legendary),
        Ambitions.Artifact => world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id),
        Ambitions.Magic => c.Attributes.Lore >= LoreMastered,
        Ambitions.Family => c.SpouseId != 0 && c.Children.Count > 0,
        _ => false,
    };

    /// <summary>
    /// What they turn to next, fitting who they are: renown for fighters and the bold, magic for the learned, a great treasure
    /// for the greedy and the famous, else wealth; never the goal just won or given up.
    /// </summary>
    private static string Next(Character c, string done, SimContext ctx)
    {
        var calling = Classes.Base(c.Class);
        var options = new List<string>();
        if (calling is Classes.Warrior or Classes.Ranger or Classes.Paladin or Classes.Rogue || c.Has(Traits.Brave)) options.Add(Ambitions.Renown);
        if ((calling is Classes.Mage or Classes.Warlock || c.Attributes.Lore >= 14) && c.Attributes.Lore < LoreMastered) options.Add(Ambitions.Magic);
        if (c.Has(Traits.Greedy) || c.Has(Traits.Ambitious) || c.Renown >= 40) options.Add(Ambitions.Artifact);
        if (c.SpouseId == 0 || c.Children.Count == 0) options.Add(Ambitions.Family);
        options.Remove(done);
        if (c.Has(Traits.Content) && options.Contains(Ambitions.Family)) return Ambitions.Family;
        return options.Count == 0 ? Ambitions.Wealth : options[Core.Hash.Percent(c.Id, 11, ctx.Date.Year) % options.Count];
    }

    private static string Goal(string ambition) => ambition == Ambitions.Renown ? "renown" : English.Ambition(ambition);
}

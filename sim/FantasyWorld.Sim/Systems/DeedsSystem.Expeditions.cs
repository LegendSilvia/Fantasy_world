using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Expeditions (the owner, 2026-10-30: a labyrinth takes time like a battle, and many may clear it together): a company goes
/// down a labyrinth, or out against an elder dragon, and its days are told as they pass. Each step takes days: a level
/// delved, the guardian faced, a bout with the dragon. The company fights as one (<see cref="Encounters.Party"/>), each
/// level of its members counts, and a wound falls on one of them. A failure ends the expedition, as does the last step it
/// had the strength for: a great labyrinth or an elder dragon takes many expeditions, over years.
/// </summary>
public sealed partial class DeedsSystem
{
    public const string ExpeditionKind = "expedition";

    /// <summary>The most who go together: a leader and four.</summary>
    public const int MostInCompany = 5;

    /// <summary>The fewest a world event takes (a great labyrinth, an elder dragon): no one goes alone.</summary>
    public const int FewestForAWorldEvent = 3;

    /// <summary>The days one step takes: a level of a labyrinth (deeper is longer), a bout with a dragon.</summary>
    public static int StepDays(int level) => 2 + level / 2;

    /// <summary>How many steps a company can take before it must come back up: a great labyrinth is delved a few levels at a time.</summary>
    public const int GreatStepsPerExpedition = 3, DragonBoutsPerExpedition = 3;

    /// <summary>How hard each level of a great labyrinth is, against an ordinary one.</summary>
    public const double GreatHardness = 1.6;

    /// <summary>The chance each year that a company gathers against a world event still standing.</summary>
    public const double CompanyPerYear = 0.5;

    /// <summary>Whether a character is out on an expedition now.</summary>
    public static bool InExpedition(EventQueue queue, long characterId) =>
        queue.Snapshot().Any(a => a.Kind == ExpeditionKind && Members(a).Contains(characterId));

    private static IEnumerable<long> Members(ScheduledAction a) =>
        a.Data["members"].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(m => long.Parse(m, CultureInfo.InvariantCulture));

    private static bool Free(SimContext ctx, Character c) =>
        c.IsActive && c.Stage(ctx.Date) == LifeStage.Adult && c.Role is not Role.Lord && !Engaged(ctx.Queue, c.Id) && !InExpedition(ctx.Queue, c.Id)
        && !MilitarySystem.Active(ctx.World).Any(a => a.CommanderId == c.Id || a.Knights.Contains(c.Id) || a.Mercenaries.Contains(c.Id));

    /// <summary>
    /// A delver going down an ordinary labyrinth gathers who will come along: up to three of their friends, or fighters of
    /// the land near it, each of a level the labyrinth allows; the strongest first.
    /// </summary>
    private void BeginExpedition(SimContext ctx, Character leader, Structure maze)
    {
        var world = ctx.World;
        var near = Nearby(world, maze.RegionId).Select(r => r.Id).ToHashSet();
        var company = world.ActiveCharacters
            .Where(c => c.Id != leader.Id && Free(ctx, c) && Levels.MayDelve(c, maze)
                        && (Relations.Opinion(c, leader) >= Relations.FriendAt
                            || world.Settlements.TryGetValue(c.SettlementId, out var s) && near.Contains(s.RegionId)
                               && (c.Role is Role.Adventurer or Role.Knight || c.Ambition is Ambitions.Renown or Ambitions.Artifact)))
            .OrderByDescending(Levels.Of).ThenByDescending(c => Encounters.Strength(world, c)).ThenBy(c => c.Id)
            .Take(MostInCompany - 2).Prepend(leader).ToList();
        Begin(ctx, company, maze.Ref, world.Regions[maze.RegionId].Ref, steps: int.MaxValue, great: false);
    }

    /// <summary>
    /// Each year, against each world event still standing (a great labyrinth, an elder dragon), the strongest of the world
    /// who are free may gather as a company, three at least and five at most; a company too few never sets out.
    /// </summary>
    private void FormCompanies(SimContext ctx)
    {
        var world = ctx.World;
        var targets = world.Structures.Values.Where(s => s.Great && s.ClearedEventId == 0 && s.ExpeditionEventId == 0)
            .Select(s => (Ref: s.Ref, Place: world.Regions[s.RegionId].Ref, Need: Levels.Required(s) - 1, Steps: GreatStepsPerExpedition))
            .Concat(world.Monsters.Values.Where(m => m.Elder && m.Alive && !Hunted(ctx, m))
                .Select(m => (Ref: m.Ref, Place: world.Regions[m.RegionId].Ref, Need: Levels.SoftCap, Steps: DragonBoutsPerExpedition)))
            .ToList();
        foreach (var (target, place, need, steps) in targets)
        {
            if (Hash.Percent(target.Id, 7401, ctx.Date.Year) >= CompanyPerYear * 100) continue; // rolled from the target, not the world's dice
            var company = world.ActiveCharacters.Where(c => Free(ctx, c) && Levels.Of(c) >= need && !c.Has(Traits.Craven))
                .OrderByDescending(Levels.Of).ThenByDescending(c => Encounters.Strength(world, c)).ThenBy(c => c.Id).Take(MostInCompany).ToList();
            if (company.Count < FewestForAWorldEvent) continue;
            Begin(ctx, company, target, place, steps, great: true);
        }
    }

    private static bool Hunted(SimContext ctx, Monster m) =>
        ctx.Queue.Snapshot().Any(a => a.Kind == ExpeditionKind && a.Data["target"] == m.Ref.ToString());

    private static Dictionary<string, EntityRef> Actors(IReadOnlyList<Character> company, EntityRef target)
    {
        var actors = new Dictionary<string, EntityRef> { ["leader"] = company[0].Ref, [target.Kind == Monster.Kind ? "monster" : "structure"] = target, ["target"] = target };
        for (var i = 1; i < company.Count; i++) actors[$"companion {i}"] = company[i].Ref;
        return actors;
    }

    private void Begin(SimContext ctx, IReadOnlyList<Character> company, EntityRef target, EntityRef place, int steps, bool great)
    {
        var world = ctx.World;
        var begun = ctx.Emit(new EventDraft("ExpeditionBegun", Actors: Actors(company, target), Place: place,
            Payload: new Dictionary<string, string>
            {
                ["company"] = company.Count.ToString(CultureInfo.InvariantCulture),
                ["levels"] = string.Join(",", company.Select(c => Levels.Of(c).ToString(CultureInfo.InvariantCulture))),
            },
            Importance: Math.Max(great ? 55 : 20, company.Max(c => Fates.Importance(c, great ? 50 : 18))),
            Tags: new[] { "Characters", "Deeds" }));
        if (target.Kind == Structure.Kind) world.Structures[target.Id].ExpeditionEventId = begun.Id;
        Schedule(ctx, company.Select(c => c.Id), target, begun.Id, step: 1, steps, days: target.Kind == Monster.Kind ? StepDays(Levels.Max) : 1);
    }

    private static void Schedule(SimContext ctx, IEnumerable<long> members, EntityRef target, long begun, int step, int steps, int days) =>
        ctx.Queue.Schedule(ctx.Date.AddDays(days), ExpeditionKind, new Dictionary<string, string>
        {
            ["target"] = target.ToString(),
            ["begun"] = begun.ToString(CultureInfo.InvariantCulture),
            ["members"] = string.Join(",", members.Select(m => m.ToString(CultureInfo.InvariantCulture))),
            ["step"] = step.ToString(CultureInfo.InvariantCulture),
            ["steps"] = steps.ToString(CultureInfo.InvariantCulture),
        });

    private void Expedition(SimContext ctx, ScheduledAction action)
    {
        var world = ctx.World;
        var begun = long.Parse(action.Data["begun"], CultureInfo.InvariantCulture);
        var step = int.Parse(action.Data["step"], CultureInfo.InvariantCulture);
        var steps = int.Parse(action.Data["steps"], CultureInfo.InvariantCulture);
        var company = Members(action).Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>().Where(c => c.IsActive).ToList();
        var target = Chronicle.Importance.ParseRef(action.Data["target"]);
        if (company.Count == 0)
        {
            End(ctx, company, target, begun, begun, "fell");
            return;
        }
        if (target.Kind == Monster.Kind) DragonBout(ctx, company, world.Monsters[target.Id], begun, step, steps);
        else Delve(ctx, company, world.Structures[target.Id], begun, step, steps);
    }

    /// <summary>One wound or death in the company, on the one its dice fall on (rolled from the deed, not the world's dice).</summary>
    private static void Hurt(SimContext ctx, IReadOnlyList<Character> company, Fate fate, string cause, WorldEvent deed, long lostIn)
    {
        if (fate == Fate.Unharmed) return;
        var victim = company[(int)(Hash.Percent(deed.Id, 7301, company.Count) * company.Count / 100) % company.Count];
        Suffer(ctx, victim, fate, cause, deed, lostIn: lostIn);
    }

    /// <summary>A step down a labyrinth: the next level, then its guardian, then its relics and its clearing.</summary>
    private void Delve(SimContext ctx, List<Character> company, Structure maze, long begun, int step, int steps)
    {
        var world = ctx.World;
        var region = world.Regions[maze.RegionId];
        if (maze.Levels == 0) maze.Levels = 3;
        var leader = company[0];
        var hardness = Levels.Power(Levels.Required(maze)) * (maze.Great ? GreatHardness : 1);
        WorldEvent last;
        if (maze.Depth < maze.Levels)
        {
            var level = maze.Depth + 1;
            var against = (region.Danger / 3.0 + level * 2) * hardness;
            var outcome = _arbiter.Resolve(Encounters.Company(Encounters.Delve, world, leader, company, against, 0.3), ctx.Rng);
            var actors = Actors(company, maze.Ref);
            actors["delver"] = leader.Ref;
            using (ctx.Annotate("account", outcome.Account))
                last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "LabyrinthDelved" : "LabyrinthDelveFailed", Actors: actors, Place: region.Ref,
                    Payload: new Dictionary<string, string> { ["level"] = level.ToString(CultureInfo.InvariantCulture), ["outcome"] = outcome.Key },
                    Causes: new[] { new Cause(begun, CauseKind.Trigger, 0.9) },
                    Importance: company.Max(c => Fates.Importance(c, outcome.AttackerWins ? 15 + 3 * level : 15)) + (maze.Great ? 15 : 0),
                    Tags: new[] { "Characters", "Deeds", "Structures" }));
            Hurt(ctx, company, outcome.AttackerFate, maze.Name, last, region.Id);
            if (!outcome.AttackerWins)
            {
                End(ctx, company, maze.Ref, begun, last.Id, "turned back");
                return;
            }
            maze.Depth = level;
            foreach (var c in company) Glory(ctx, c, 2 + level);
            foreach (var found in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id && a.Level == level)
                         .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).ToList())
            {
                last = ctx.Emit(new EventDraft("ArtifactFound", Actors: new Dictionary<string, EntityRef> { ["delver"] = leader.Ref, ["artifact"] = found.Ref },
                    Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = "success", ["level"] = level.ToString(CultureInfo.InvariantCulture) },
                    Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.9) },
                    Importance: Fates.Importance(leader, (int)(20 + found.Fame / 3)), Tags: new[] { "Characters", "Deeds", "Artifacts" }));
                Fates.MoveToHolder(found, leader, last.Id, "found");
                found.Level = 0;
                Glory(ctx, leader, 5);
            }
            // The bottom and its secret wait for the audience (secrets only once the chronicle airs).
            var aired = world.LastBroadcastDate is not null;
            if (maze.Depth < maze.Levels)
            {
                if (step >= steps || !aired && maze.Depth + 1 >= maze.Levels)
                    End(ctx, company, maze.Ref, begun, last.Id, "came back up");
                else Schedule(ctx, company.Select(c => c.Id), maze.Ref, begun, step + 1, steps, StepDays(maze.Depth + 1));
                return;
            }
        }
        else last = ctx.Log.Get(begun);

        if (world.Monsters.GetValueOrDefault(maze.GuardianId) is { Alive: true } guardian)
        {
            var outcome = _arbiter.Resolve(Encounters.Company(Encounters.Hunt, world, leader, company, guardian.Might / 2.0 * hardness, 0.25), ctx.Rng);
            var actors = Actors(company, maze.Ref);
            actors.Remove("structure");
            actors["hunter"] = leader.Ref;
            actors["monster"] = guardian.Ref;
            using (ctx.Annotate("account", outcome.Account))
                last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed", Actors: actors, Place: region.Ref,
                    Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key }, Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                    Importance: company.Max(c => Fates.Importance(c, outcome.AttackerWins ? (int)(30 + guardian.Might / 3) : 18)), Tags: new[] { "Characters", "Deeds", "Magic" }));
            Hurt(ctx, company, outcome.AttackerFate, guardian.Name, last, region.Id);
            if (!outcome.AttackerWins)
            {
                guardian.Notoriety = Math.Min(100, guardian.Notoriety + 8);
                End(ctx, company, maze.Ref, begun, last.Id, "turned back");
                return;
            }
            (guardian.State, guardian.SlainEventId, guardian.SlayerId) = (MonsterState.Slain, last.Id, leader.Id);
            foreach (var c in company) Glory(ctx, c, 8 + guardian.Might / 8);
        }

        var relic = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id && (a.Level == 0 || a.Level <= maze.Depth))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (relic is not null)
        {
            last = ctx.Emit(new EventDraft("ArtifactFound", Actors: new Dictionary<string, EntityRef> { ["delver"] = leader.Ref, ["artifact"] = relic.Ref },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = "success" }, Causes: new[] { new Cause(last.Id, CauseKind.Enabler, 0.8) },
                Importance: Fates.Importance(leader, (int)(20 + relic.Fame / 3)), Tags: new[] { "Characters", "Deeds", "Artifacts" }));
            Fates.MoveToHolder(relic, leader, last.Id, "found");
            relic.Fame = Math.Min(100, relic.Fame + 5);
            Glory(ctx, leader, 10);
        }
        if (world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id))
        {
            End(ctx, company, maze.Ref, begun, last.Id, "came back up");
            return;
        }

        var shrine = leader.Has(Traits.Devout);
        var cleared = Actors(company, maze.Ref);
        cleared["delver"] = leader.Ref;
        var spoils = relic is not null && relic.HolderId == leader.Id ? relic.Name : null;
        if (spoils is not null) cleared["artifact"] = relic!.Ref;
        maze.ClearedEventId = ctx.Emit(new EventDraft("LabyrinthCleared", Actors: cleared, Place: region.Ref,
            Payload: maze.Great
                ? new Dictionary<string, string> { ["becomes"] = shrine ? "shrine" : "ruin", ["great"] = "yes" }
                : new Dictionary<string, string> { ["becomes"] = shrine ? "shrine" : "ruin" },
            Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.9) },
            Importance: Math.Max(maze.Great ? 85 : 50, Fates.Importance(leader, 50)), Tags: new[] { "Characters", "Deeds", "Structures" })).Id;
        (maze.Type, maze.State) = shrine ? (StructureKind.Shrine, StructureState.Standing) : (StructureKind.Ruin, StructureState.Ruined);
        region.Danger = Math.Max(0, region.Danger - (maze.Great ? 40 : 20));
        foreach (var c in company) Glory(ctx, c, maze.Great ? 25 : 10);
        Reveal(ctx, leader, maze, region, maze.ClearedEventId, spoils is null ? null : relic!.Ref);
        End(ctx, company, maze.Ref, begun, maze.ClearedEventId, "cleared");
    }

    /// <summary>
    /// A bout with an elder dragon: a company that wins it wounds the beast, and a beast worn to nothing is slain; one that
    /// loses is driven off. Its strength is its might, as much as is left of it, at level 10.
    /// </summary>
    private void DragonBout(SimContext ctx, List<Character> company, Monster dragon, long begun, int step, int steps)
    {
        var world = ctx.World;
        if (!dragon.Alive)
        {
            End(ctx, company, dragon.Ref, begun, begun, "came back");
            return;
        }
        var region = world.Regions[dragon.RegionId];
        var leader = company[0];
        var against = dragon.Might * Math.Max(0.3, dragon.Vigour / 100) * Levels.Power(Levels.Of(dragon));
        var outcome = _arbiter.Resolve(Encounters.Company(Encounters.Hunt, world, leader, company, against, 0.25), ctx.Rng);
        var actors = Actors(company, dragon.Ref);
        actors["hunter"] = leader.Ref;
        WorldEvent bout;
        if (outcome.AttackerWins)
        {
            dragon.Vigour = Math.Max(0, dragon.Vigour - (outcome.Key == "success" ? 30 : 20));
            var slain = dragon.Vigour <= 0;
            using (ctx.Annotate("account", outcome.Account))
                bout = ctx.Emit(new EventDraft(slain ? "ElderDragonSlain" : "DragonWounded", Actors: actors, Place: region.Ref,
                    Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key, ["vigour"] = ((int)dragon.Vigour).ToString(CultureInfo.InvariantCulture) },
                    Causes: new[] { new Cause(begun, CauseKind.Trigger, 0.9) }, Importance: slain ? 95 : 60, Tags: new[] { "Characters", "Deeds", "Magic" }));
            Hurt(ctx, company, outcome.AttackerFate, dragon.Name, bout, region.Id);
            foreach (var c in company) Glory(ctx, c, slain ? 30 : 6);
            if (slain)
            {
                (dragon.State, dragon.SlainEventId, dragon.SlayerId) = (MonsterState.Slain, bout.Id, leader.Id);
                region.Danger = Math.Max(0, region.Danger - 40);
                End(ctx, company, dragon.Ref, begun, bout.Id, "slew it");
                return;
            }
            if (step >= steps) End(ctx, company, dragon.Ref, begun, bout.Id, "came back");
            else Schedule(ctx, company.Select(c => c.Id), dragon.Ref, begun, step + 1, steps, StepDays(Levels.Max));
            return;
        }
        using (ctx.Annotate("account", outcome.Account))
            bout = ctx.Emit(new EventDraft("DragonHuntFailed", Actors: actors, Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
                Causes: new[] { new Cause(begun, CauseKind.Trigger, 0.9) }, Importance: 50, Tags: new[] { "Characters", "Deeds", "Magic" }));
        Hurt(ctx, company, outcome.AttackerFate, dragon.Name, bout, region.Id);
        End(ctx, company, dragon.Ref, begun, bout.Id, "driven off");
    }

    private static void End(SimContext ctx, IReadOnlyList<Character> company, EntityRef target, long begun, long last, string outcome)
    {
        var world = ctx.World;
        if (target.Kind == Structure.Kind) world.Structures[target.Id].ExpeditionEventId = 0;
        var started = ctx.Log.Get(begun);
        var actors = company.Count > 0 ? Actors(company, target) : new Dictionary<string, EntityRef>(started.Actors);
        ctx.Emit(new EventDraft("ExpeditionEnded", Actors: actors, Place: started.Place,
            Payload: new Dictionary<string, string>
            {
                ["outcome"] = outcome,
                ["days"] = Math.Max(1, ctx.Date.Day - started.Date.Day + 1).ToString(CultureInfo.InvariantCulture),
            },
            Causes: new[] { new Cause(last, CauseKind.Trigger, 0.9) }, Importance: Math.Min(started.Importance, Chronicle.Compactor.MinorBelow + 10),
            Tags: new[] { "Characters", "Deeds" }));
    }
}

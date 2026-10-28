using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// What notables do with their lives (spec §3 "Monthly decisions"). Each month some adults act on
/// their ambition: a decision point offering hunts, delves, duels, study, forging or theft (plus
/// "carry on"). Risky deeds are encounters resolved by the arbiter; outcomes wound, kill, move
/// artifacts, tame regions and build renown. Every deed is an event citing its motive.
/// </summary>
public sealed class DeedsSystem : ISimSystem
{
    public const double ActPerMonth = 0.04;

    /// <summary>Artifacts the world can hold before forging becomes impossible (spec: 20–60 per world).</summary>
    public static int ArtifactCap(World world) => Math.Max(10, world.Regions.Count / 2);

    private readonly IDecider _decider;
    private readonly IArbiter _arbiter;
    private readonly IInventor _inventor;

    public DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null)
    {
        _inventor = inventor ?? new RulesInventor();
        _decider = decider ?? new RulesDecider();
        _arbiter = arbiter ?? new RulesArbiter();
    }

    public string Name => "deeds";

    /// <summary>A challenge is answered a year and a day later (sub-project 8 §4), so it is news in one episode and fought in the next.</summary>
    public const int DuelDelayDays = WorldDate.DaysPerYear + 1;

    public const string DuelKind = "duel";

    /// <summary>A beat of a duel that lasts more than a day, told on its day (sub-project 8 §5).</summary>
    public const string DuelTurnKind = "duel-turn";

    /// <summary>The last day of a duel that lasts more than a day: its outcome is applied.</summary>
    public const string DuelEndKind = "duel-end";

    public IEnumerable<string> HandledKinds => new[] { DuelKind, DuelTurnKind, DuelEndKind };

    /// <summary>The duels declared and not yet fought, or begun and not yet ended, from the queue.</summary>
    public static IEnumerable<ScheduledAction> PendingDuels(EventQueue queue) => queue.Snapshot().Where(a => a.Kind is DuelKind or DuelEndKind);

    private static bool Challenged(EventQueue queue, long characterId) =>
        PendingDuels(queue).Any(a => a.Data["challenger"] == characterId.ToString(CultureInfo.InvariantCulture)
                                     || a.Data["defender"] == characterId.ToString(CultureInfo.InvariantCulture));

    public void OnMonthlyTick(SimContext ctx)
    {
        var residents = ctx.World.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var c in residents.SelectMany(g => g).OrderBy(c => c.Id).ToList())
        {
            if (!c.IsActive || c.Stage(ctx.Date) == LifeStage.Child) continue;
            // The demon lord and the hero sworn against it keep to their saga (sub-project 13): no lesser deeds.
            if (SagaSystem.Active(ctx.World) is { } saga && (c.Id == saga.DemonLordId || c.Id == saga.HeroId)) continue;
            if (c.Ambition == Ambitions.Vengeance
                && !(ctx.World.Characters.TryGetValue(c.AmbitionTarget, out var target) && target.IsActive && target.Id != c.Id))
            {
                c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
                c.AmbitionTarget = 0;
            }
            var drive = c.Has(Traits.Diligent) ? 1.5 : c.Has(Traits.Lazy) ? 0.5 : 1.0;
            if (!ctx.Rng.Chance(ActPerMonth * drive)) continue;
            var options = Options(ctx, c, residents[c.SettlementId]);
            if (options.Count <= 1) continue;
            var choice = _decider.Decide(new DecisionBrief(c.Id, c.Ambition, options), ctx.Rng);
            using (ctx.Annotate("motive", choice.Reasoning)) Act(ctx, c, choice.Key);
        }
    }

    /// <summary>The legal deeds for this character now, weighted by ambition and traits. Keys: verb:target.</summary>
    public static List<DecisionOption> Options(SimContext ctx, Character c, IEnumerable<Character>? neighbours = null)
    {
        var world = ctx.World;
        var home = world.Settlements[c.SettlementId];
        var nearby = Nearby(world, home.RegionId).ToList();
        var options = new List<DecisionOption> { new("rest", 30, "carry on as before") };
        var bold = c.Has(Traits.Brave) ? 2.0 : c.Has(Traits.Craven) ? 0.2 : 1.0;

        var beast = world.Monsters.Values.Where(m => m.State == MonsterState.Roaming && nearby.Any(r => r.Id == m.RegionId))
            .OrderByDescending(m => m.Notoriety).ThenBy(m => m.Id).FirstOrDefault();
        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (beast is not null)
            options.Add(new($"hunt:{beast.RegionId}", (c.Ambition == Ambitions.Renown ? 50 : 6) * bold, $"hunt {beast.Name} in {world.Regions[beast.RegionId].Name}"));
        else if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        // A labyrinth is delved level by level; the relics lost in its land lie on its deepest level.
        var maze = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.ClearedEventId == 0 && nearby.Any(r => r.Id == s.RegionId))
            .OrderBy(s => s.Id).FirstOrDefault();
        if (maze is not null)
            options.Add(new($"maze:{maze.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 25 : 2) * bold,
                $"delve into {maze.Name}, level {Math.Min(Math.Max(maze.Levels, 3), maze.Depth + 1)}"));

        var mazes = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.ClearedEventId == 0).Select(s => s.RegionId).ToHashSet();
        var lost = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && nearby.Any(r => r.Id == a.RegionId) && !mazes.Contains(a.RegionId))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (lost is not null)
            options.Add(new($"delve:{lost.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 10 : 2) * bold,
                $"seek {lost.Name} in {world.Regions[lost.RegionId].Name}"));

        if (c.Ambition == Ambitions.Artifact && (c.Has(Traits.Deceitful) || c.Has(Traits.Greedy)))
        {
            var coveted = world.Artifacts.Values
                .Where(a => a.Place == ArtifactPlace.Held && a.HolderId != c.Id && world.Characters[a.HolderId].SettlementId == c.SettlementId)
                .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
            if (coveted is not null) options.Add(new($"steal:{coveted.Id}", 10, $"steal {coveted.Name}"));
        }

        if (c.Ambition == Ambitions.Magic)
        {
            options.Add(new("study", 40, "study the arcane"));
            if (c.Attributes.Lore >= 15 && world.Regions[home.RegionId].LeyPower >= 50) options.Add(new("forge", 20, "forge an artifact"));
        }

        // One challenge at a time: someone with a duel pending neither issues nor is sent another.
        var free = !Challenged(ctx.Queue, c.Id);
        if (free && c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive && foe.Id != c.Id
            && !Challenged(ctx.Queue, foe.Id) && nearby.Any(r => r.Id == world.Settlements[foe.SettlementId].RegionId))
            options.Add(new($"avenge:{foe.Id}", 50 * bold, $"challenge {foe.Name} to the death"));

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        if (free && rival is not null && !Challenged(ctx.Queue, rival.Id) && (c.Has(Traits.Wrathful) || c.Has(Traits.Brave)))
            options.Add(new($"duel:{rival.Id}", 25 * bold, $"challenge {rival.Name} to first blood"));
        return options;
    }

    private void Act(SimContext ctx, Character c, string key)
    {
        var parts = key.Split(':');
        var target = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        switch (parts[0])
        {
            case "hunt": Hunt(ctx, c, ctx.World.Regions[target]); break;
            case "delve": Delve(ctx, c, ctx.World.Artifacts[target]); break;
            case "maze": Maze(ctx, c, ctx.World.Structures[target]); break;
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            case "avenge" when target != c.Id: Challenge(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Challenge(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        if (ctx.World.Monsters.Values.Where(m => m.State == MonsterState.Roaming && m.RegionId == region.Id).OrderBy(m => m.Id).FirstOrDefault() is { } beast)
        {
            HuntBeast(ctx, hunter, region, beast);
            return;
        }
        var outcome = _arbiter.Resolve(Encounters.HuntIn(ctx.World, hunter, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonstersSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? 25 : 15), Tags: new[] { "Characters", "Deeds" }));
        if (outcome.AttackerWins)
        {
            region.Danger = Math.Max(0, region.Danger - 15);
            Glory(ctx, hunter, 8);
        }
        Suffer(ctx, hunter, outcome.AttackerFate, "monsters", hunt, lostIn: region.Id);
    }

    /// <summary>
    /// A hunt for a named beast: the arbiter weighs the hunter against its might. Slaying it ends its story (fame, a
    /// safer land, and from a mighty beast a trophy); failing feeds its notoriety.
    /// </summary>
    private void HuntBeast(SimContext ctx, Character hunter, Region region, Monster beast)
    {
        var world = ctx.World;
        var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, hunter, beast), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
            Actors: new Dictionary<string, EntityRef> { ["hunter"] = hunter.Ref, ["monster"] = beast.Ref },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: new[] { new Cause(ctx.Log.Involving(beast.Ref).LastOrDefault()?.Id ?? beast.RoseEventId, CauseKind.Trigger, 0.8) },
            Importance: Fates.Importance(hunter, outcome.AttackerWins ? (int)(30 + beast.Might / 3 + (beast.Legendary ? 15 : 0)) : 18),
            Tags: new[] { "Characters", "Deeds", "Magic" }));
        if (outcome.AttackerWins)
        {
            beast.State = MonsterState.Slain;
            beast.SlainEventId = hunt.Id;
            beast.SlayerId = hunter.Id;
            region.Danger = Math.Max(0, region.Danger - 25);
            Glory(ctx, hunter, 8 + beast.Might / 8);
            if (beast.Might >= 60 && world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < ArtifactCap(world))
            {
                var trophy = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), hunt.Id,
                    fame: 20 + beast.Notoriety / 2);
                world.Artifacts[trophy.Id] = trophy;
                Fates.MoveToHolder(trophy, hunter, hunt.Id, "won");
            }
        }
        else beast.Notoriety = Math.Min(100, beast.Notoriety + 8);
        Suffer(ctx, hunter, outcome.AttackerFate, beast.Name, hunt, lostIn: region.Id);
    }

    private void Delve(SimContext ctx, Character delver, Artifact artifact)
    {
        var region = ctx.World.Regions[artifact.RegionId];
        var outcome = _arbiter.Resolve(Encounters.DelveIn(ctx.World, delver, region), ctx.Rng);
        using var account = ctx.Annotate("account", outcome.Account);
        var delve = ctx.Emit(new EventDraft(outcome.AttackerWins ? "ArtifactFound" : "DelveFailed",
            Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = artifact.Ref },
            Place: region.Ref,
            Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
            Causes: artifact.Provenance.Count > 0 ? new[] { new Cause(artifact.Provenance[^1].EventId, CauseKind.Enabler, 0.5) } : null,
            Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(20 + artifact.Fame / 3) : 15),
            Tags: new[] { "Characters", "Deeds", "Artifacts" }));
        if (outcome.AttackerWins)
        {
            Fates.MoveToHolder(artifact, delver, delve.Id, "found");
            artifact.Fame = Math.Min(100, artifact.Fame + 5);
            Glory(ctx, delver, 10);
        }
        Suffer(ctx, delver, outcome.AttackerFate, "a labyrinth's guardians", delve, lostIn: region.Id);
    }

    /// <summary>
    /// A delve one level deeper into a labyrinth (the depth reached is shared: the way down is remembered). At the
    /// deepest level the guardian bars the way; past it lie the relics lost in the land. Slaying the guardian and taking
    /// the last relic clears the labyrinth: the land grows safer, and it becomes a ruin, or a shrine if a devout hand cleared it.
    /// </summary>
    private void Maze(SimContext ctx, Character delver, Structure maze)
    {
        var world = ctx.World;
        var region = world.Regions[maze.RegionId];
        if (maze.Levels == 0) maze.Levels = 3;
        var actors = new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["structure"] = maze.Ref };
        WorldEvent last;
        if (maze.Depth < maze.Levels)
        {
            var level = maze.Depth + 1;
            var outcome = _arbiter.Resolve(Encounters.DelveLevel(world, delver, region, level), ctx.Rng);
            using (ctx.Annotate("account", outcome.Account))
                last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "LabyrinthDelved" : "LabyrinthDelveFailed", Actors: actors, Place: region.Ref,
                    Payload: new Dictionary<string, string> { ["level"] = level.ToString(CultureInfo.InvariantCulture), ["outcome"] = outcome.Key },
                    Causes: new[] { new Cause(ctx.Log.Involving(maze.Ref).LastOrDefault()?.Id ?? maze.StartedEventId, CauseKind.Enabler, 0.5) },
                    Importance: Fates.Importance(delver, outcome.AttackerWins ? 15 + 3 * level : 15), Tags: new[] { "Characters", "Deeds", "Structures" }));
            Suffer(ctx, delver, outcome.AttackerFate, maze.Name, last, lostIn: region.Id);
            if (!outcome.AttackerWins || !delver.IsActive) return;
            maze.Depth = level;
            Glory(ctx, delver, 2 + level);
            // What lies on this level is the delver's (sub-project 13).
            foreach (var found in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id && a.Level == level)
                         .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).ToList())
            {
                last = ctx.Emit(new EventDraft("ArtifactFound", Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = found.Ref },
                    Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = "success", ["level"] = level.ToString(CultureInfo.InvariantCulture) },
                    Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.9) },
                    Importance: Fates.Importance(delver, (int)(20 + found.Fame / 3)), Tags: new[] { "Characters", "Deeds", "Artifacts" }));
                Fates.MoveToHolder(found, delver, last.Id, "found");
                found.Level = 0;
                Glory(ctx, delver, 5);
            }
            if (maze.Depth < maze.Levels) return;
        }
        else last = ctx.Log.Involving(maze.Ref).LastOrDefault() ?? ctx.Log.Get(maze.StartedEventId);

        if (world.Monsters.GetValueOrDefault(maze.GuardianId) is { Alive: true } guardian)
        {
            var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, delver, guardian), ctx.Rng);
            using var account = ctx.Annotate("account", outcome.Account);
            last = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
                Actors: new Dictionary<string, EntityRef> { ["hunter"] = delver.Ref, ["monster"] = guardian.Ref }, Place: region.Ref,
                Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key }, Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: Fates.Importance(delver, outcome.AttackerWins ? (int)(30 + guardian.Might / 3) : 18), Tags: new[] { "Characters", "Deeds", "Magic" }));
            if (!outcome.AttackerWins)
            {
                guardian.Notoriety = Math.Min(100, guardian.Notoriety + 8);
                Suffer(ctx, delver, outcome.AttackerFate, guardian.Name, last, lostIn: region.Id);
                return;
            }
            (guardian.State, guardian.SlainEventId, guardian.SlayerId) = (MonsterState.Slain, last.Id, delver.Id);
            Glory(ctx, delver, 8 + guardian.Might / 8);
            Suffer(ctx, delver, outcome.AttackerFate, guardian.Name, last, lostIn: region.Id);
            if (!delver.IsActive) return;
        }

        var relic = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id && (a.Level == 0 || a.Level <= maze.Depth))
            .OrderByDescending(a => a.Fame).ThenBy(a => a.Id).FirstOrDefault();
        if (relic is not null)
        {
            last = ctx.Emit(new EventDraft("ArtifactFound", Actors: new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["artifact"] = relic.Ref },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = "success" }, Causes: new[] { new Cause(last.Id, CauseKind.Enabler, 0.8) },
                Importance: Fates.Importance(delver, (int)(20 + relic.Fame / 3)), Tags: new[] { "Characters", "Deeds", "Artifacts" }));
            Fates.MoveToHolder(relic, delver, last.Id, "found");
            relic.Fame = Math.Min(100, relic.Fame + 5);
            Glory(ctx, delver, 10);
        }
        if (world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Lost && a.RegionId == region.Id)) return;

        var shrine = delver.Has(Traits.Devout);
        actors = new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["structure"] = maze.Ref };
        maze.ClearedEventId = ctx.Emit(new EventDraft("LabyrinthCleared", Actors: actors, Place: region.Ref,
            Payload: new Dictionary<string, string> { ["becomes"] = shrine ? "shrine" : "ruin" }, Causes: new[] { new Cause(last.Id, CauseKind.Trigger, 0.9) },
            Importance: Fates.Importance(delver, 50), Tags: new[] { "Characters", "Deeds", "Structures" })).Id;
        (maze.Type, maze.State) = shrine ? (StructureKind.Shrine, StructureState.Standing) : (StructureKind.Ruin, StructureState.Ruined);
        region.Danger = Math.Max(0, region.Danger - 20);
        Glory(ctx, delver, 10);
    }

    private static void Steal(SimContext ctx, Character thief, Artifact artifact)
    {
        var victim = ctx.World.Characters[artifact.HolderId];
        var guard = victim.Attributes.Intrigue + artifact.Bonus("intrigue") + 5 + (victim.Has(Traits.Paranoid) ? 5 : 0);
        var success = ctx.Rng.Chance(thief.Attributes.Intrigue / (double)(thief.Attributes.Intrigue + guard));
        var ev = ctx.Emit(new EventDraft(success ? "ArtifactStolen" : "TheftFoiled",
            Actors: new Dictionary<string, EntityRef> { ["thief"] = thief.Ref, ["victim"] = victim.Ref, ["artifact"] = artifact.Ref },
            Place: ctx.World.Settlements[thief.SettlementId].Ref,
            Importance: Math.Max(Fates.Importance(victim, 15), (int)(10 + artifact.Fame / 3)), Tags: new[] { "Characters", "Artifacts" }));
        Fates.Remember(victim, ev.Id, thief.Id, success ? -80 : -50, 80);
        if (success)
        {
            Fates.MoveToHolder(artifact, thief, ev.Id, "stolen");
            SwearVengeance(ctx, victim, thief);
        }
        else Shame(thief);
    }

    private void Forge(SimContext ctx, Character mage)
    {
        var world = ctx.World;
        var region = world.Regions[world.Settlements[mage.SettlementId].RegionId];
        var room = 1 - world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) / (double)ArtifactCap(world);
        if (!ctx.Rng.Chance(Math.Max(0, room) * (mage.Attributes.Lore - 12) / 30.0)) return;
        var artifactRef = new EntityRef(Artifact.Kind, world.Ids.Peek(Artifact.Kind));
        var forged = ctx.Emit(new EventDraft("ArtifactForged",
            Actors: new Dictionary<string, EntityRef> { ["maker"] = mage.Ref, ["artifact"] = artifactRef },
            Place: world.Settlements[mage.SettlementId].Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.6)),
            Importance: Fates.Importance(mage, 35), Tags: new[] { "Characters", "Artifacts" }));
        var artifact = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(mage.CultureId)?.Style),
            forged.Id, rareMaterials: region.Resources.Keys.Where(ResourceKinds.IsRare).ToList());
        var powers = string.Join(", ", artifact.Powers.Select(p => p.Attribute is null ? p.Kind : $"+{p.Amount} {p.Attribute}"));
        var invention = _inventor.Invent(new InventionBrief("artifact", artifact.Ref.ToString(),
            $"A {artifact.Type.ToString().ToLowerInvariant()} of {artifact.Material}, granting {powers}{(artifact.Curse is null ? "" : $", bearing a hidden curse of {artifact.Curse}")}; "
            + $"forged by {mage.Name}, a {world.Cultures.GetValueOrDefault(mage.CultureId)?.Name ?? "nameless"} mage, at {world.Settlements[mage.SettlementId].Name} in {region.Name}.",
            artifact.Name));
        (artifact.Name, artifact.Lore) = (invention.Name, invention.Lore ?? "");
        world.Artifacts[artifact.Id] = artifact;
        Fates.MoveToHolder(artifact, mage, forged.Id, "forged");
        Glory(ctx, mage, 10);
    }

    /// <summary>
    /// The challenge (sub-project 8 §4): declared now with the odds as they stand, fought a year and a day later. The
    /// grudge behind it is its motive.
    /// </summary>
    private static void Challenge(SimContext ctx, Character challenger, Character defender, bool toTheDeath)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var odds = Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath).AttackerOdds;
        var stakes = toTheDeath ? "to the death" : "first blood";
        var declared = ctx.Emit(new EventDraft("DuelDeclared",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: new Dictionary<string, string>
            {
                ["stakes"] = stakes, ["odds"] = Percent(odds),
                ["day"] = ctx.Date.AddDays(DuelDelayDays).Day.ToString(CultureInfo.InvariantCulture),
            },
            Causes: grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : null,
            Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 25 : 12), Fates.Importance(defender, toTheDeath ? 25 : 12)),
            Tags: new[] { "Characters", "Deeds" }));
        ctx.Queue.Schedule(ctx.Date.AddDays(DuelDelayDays), DuelKind, new Dictionary<string, string>
        {
            ["challenger"] = challenger.Id.ToString(CultureInfo.InvariantCulture),
            ["defender"] = defender.Id.ToString(CultureInfo.InvariantCulture),
            ["stakes"] = stakes,
            ["declared"] = declared.Id.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>The challenger's chance as a whole percentage, as it is shown ("62").</summary>
    public static string Percent(double odds) => Math.Round(odds * 100).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The day of a declared duel: fought if both are still here, else called off. A duel Claude made longer than a day
    /// is told on its chapters' days and ends on its last, called off then if either side is gone.
    /// </summary>
    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var world = ctx.World;
        long Id(string key) => long.Parse(action.Data[key], CultureInfo.InvariantCulture);
        var declared = Id("declared");
        world.Characters.TryGetValue(Id("challenger"), out var challenger);
        world.Characters.TryGetValue(Id("defender"), out var defender);
        var begun = action.Data.TryGetValue("begun", out var b) ? long.Parse(b, CultureInfo.InvariantCulture) : 0;
        if (challenger is not { IsActive: true } || defender is not { IsActive: true })
        {
            // A beat of a duel one side has left is not told; the duel's last day calls it off.
            if (action.Kind == DuelTurnKind) return;
            var gone = challenger is { IsActive: true } ? defender : challenger;
            ctx.Emit(new EventDraft("DuelCalledOff",
                Actors: new Dictionary<string, EntityRef?> { ["challenger"] = challenger?.Ref, ["defender"] = defender?.Ref }
                    .Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.Value),
                Payload: new Dictionary<string, string> { ["stakes"] = action.Data["stakes"], ["reason"] = gone?.Died is not null ? "died" : "gone" },
                Causes: new[] { new Cause(begun != 0 ? begun : declared, CauseKind.Trigger, 1.0) },
                Importance: 8, Tags: new[] { "Characters", "Deeds" }));
            return;
        }
        var toTheDeath = action.Data["stakes"] == "to the death";
        switch (action.Kind)
        {
            case DuelTurnKind:
            {
                using var account = ctx.Annotate("account", action.Data.GetValueOrDefault("account"));
                ctx.Emit(new EventDraft("DuelTurned",
                    Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
                    Place: world.Settlements[defender.SettlementId].Ref,
                    Payload: new Dictionary<string, string> { ["stakes"] = action.Data["stakes"], ["day"] = action.Data["day"], ["days"] = action.Data["days"] },
                    Causes: new[] { new Cause(begun, CauseKind.Trigger, 1.0) },
                    Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 18 : 10), Fates.Importance(defender, toTheDeath ? 18 : 10)),
                    Tags: new[] { "Characters", "Deeds" }));
                return;
            }
            case DuelEndKind:
            {
                // The outcome Claude chose, among the same outcomes as on the first day (the stakes fix them).
                var encounter = Encounters.DuelBetween(world, challenger, defender, toTheDeath);
                var outcome = encounter.Outcomes.First(o => o.Key == action.Data["outcome"]) with { Account = action.Data.GetValueOrDefault("account") };
                Conclude(ctx, challenger, defender, toTheDeath, declared, outcome, begun, int.Parse(action.Data["days"], CultureInfo.InvariantCulture));
                return;
            }
            default:
                Duel(ctx, challenger, defender, toTheDeath, declared);
                return;
        }
    }

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath, long declared)
    {
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath), ctx.Rng);
        if (outcome.Days <= 1)
        {
            Conclude(ctx, challenger, defender, toTheDeath, declared, outcome, 0, 1);
            return;
        }
        // A long duel: begun today with the first day's chapters, told on each later chapter's day, ended on its last.
        var days = outcome.Days;
        var chapters = outcome.Chapters.Select(c => c with { Day = Math.Clamp(c.Day, 1, days) }).ToList();
        var stakes = toTheDeath ? "to the death" : "first blood";
        WorldEvent begun;
        using (ctx.Annotate("account", FightChapter.On(chapters, 1)))
            begun = ctx.Emit(new EventDraft("DuelBegun",
                Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
                Place: ctx.World.Settlements[defender.SettlementId].Ref,
                Payload: new Dictionary<string, string> { ["stakes"] = stakes, ["days"] = days.ToString(CultureInfo.InvariantCulture) },
                Causes: new[] { new Cause(declared, CauseKind.Trigger, 1.0) },
                Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 25 : 12), Fates.Importance(defender, toTheDeath ? 25 : 12)),
                Tags: new[] { "Characters", "Deeds" }));
        Dictionary<string, string> Data(int day) => new()
        {
            ["challenger"] = challenger.Id.ToString(CultureInfo.InvariantCulture),
            ["defender"] = defender.Id.ToString(CultureInfo.InvariantCulture),
            ["stakes"] = stakes,
            ["declared"] = declared.ToString(CultureInfo.InvariantCulture),
            ["begun"] = begun.Id.ToString(CultureInfo.InvariantCulture),
            ["day"] = day.ToString(CultureInfo.InvariantCulture),
            ["days"] = days.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var day in chapters.Select(c => c.Day).Where(d => d > 1 && d < days).Distinct())
        {
            var data = Data(day);
            data["account"] = FightChapter.On(chapters, day)!;
            ctx.Queue.Schedule(ctx.Date.AddDays(day - 1), DuelTurnKind, data);
        }
        var end = Data(days);
        end["outcome"] = outcome.Key;
        if (FightChapter.On(chapters, days) is { } last) end["account"] = last;
        ctx.Queue.Schedule(ctx.Date.AddDays(days - 1), DuelEndKind, end);
    }

    /// <summary>The duel's end: its outcome applied, on the day it was fought or the last day of a long one.</summary>
    private static void Conclude(SimContext ctx, Character challenger, Character defender, bool toTheDeath, long declared,
        EncounterOutcome outcome, long begun, int days)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var payload = new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key };
        if (days > 1) payload["days"] = days.ToString(CultureInfo.InvariantCulture);
        using var account = ctx.Annotate("account", outcome.Account);
        var duel = ctx.Emit(new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: payload,
            Causes: new[] { new Cause(begun != 0 ? begun : declared, CauseKind.Trigger, 1.0) }
                .Concat(grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : Array.Empty<Cause>()).ToArray(),
            Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 30 : 15), Fates.Importance(defender, toTheDeath ? 30 : 15)),
            Tags: new[] { "Characters", "Deeds" }));
        var (winner, loser) = outcome.AttackerWins ? (challenger, defender) : (defender, challenger);
        Glory(ctx, winner, toTheDeath ? 8 : 4);
        Fates.Remember(loser, duel.Id, winner.Id, -60, 60);
        if (!toTheDeath && ctx.Rng.Chance(0.5)) Shame(loser);
        if (!toTheDeath && loser.Id != winner.Id) SwearVengeance(ctx, loser, winner);
        if (toTheDeath && outcome.AttackerWins && challenger.Ambition == Ambitions.Vengeance)
            challenger.Ambition = CharacterFactory.ChooseAmbition(challenger, ctx.Rng);
        Suffer(ctx, challenger, outcome.AttackerFate, "a duel", duel, outcome.AttackerFate == Fate.Dead ? defender : null);
        Suffer(ctx, defender, outcome.DefenderFate, "a duel", duel, outcome.DefenderFate == Fate.Dead ? challenger : null);
    }

    private static void Suffer(SimContext ctx, Character c, Fate fate, string cause, WorldEvent deed,
        Character? killer = null, long lostIn = 0)
    {
        if (fate == Fate.Dead)
        {
            if (c.IsAlive)
                Fates.Die(ctx, c, cause, new[] { new Cause(deed.Id, CauseKind.Trigger, 1.0) },
                    killer is { IsAlive: true } ? killer : null, lostIn);
            return;
        }
        Fates.Injure(c, fate, ctx.Rng);
    }

    /// <summary>Renown for the deed; the artifacts they carry grow famous with them.</summary>
    private static void Glory(SimContext ctx, Character c, double renown)
    {
        c.Renown = Math.Min(100, c.Renown + renown);
        if (c.Renown >= 50 && !c.Has(Traits.Renowned))
        {
            c.Traits.Add(Traits.Renowned);
            c.Traits.Sort(StringComparer.Ordinal);
        }
        foreach (var a in ctx.World.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id))
            a.Fame = Math.Min(100, a.Fame + renown / 2);
    }

    /// <summary>The wrathful and the cruel do not forgive a humiliation or a theft: half of them swear vengeance.</summary>
    private static void SwearVengeance(SimContext ctx, Character wronged, Character wrongdoer)
    {
        if (wronged.Has(Traits.Craven) || !(wronged.Has(Traits.Wrathful) || wronged.Has(Traits.Cruel))) return;
        if (wronged.Ambition == Ambitions.Vengeance || !ctx.Rng.Chance(0.5)) return;
        wronged.Ambition = Ambitions.Vengeance;
        wronged.AmbitionTarget = wrongdoer.Id;
    }

    private static void Shame(Character c)
    {
        if (c.Has(Traits.Humiliated)) return;
        c.Traits.Add(Traits.Humiliated);
        c.Traits.Sort(StringComparer.Ordinal);
    }

    private static IEnumerable<Region> Nearby(World world, long regionId) =>
        new[] { regionId }.Concat(world.Geography?.Graph.Neighbours(regionId) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]);
}

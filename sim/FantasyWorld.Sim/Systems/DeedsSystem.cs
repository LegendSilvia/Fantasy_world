using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
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
public sealed partial class DeedsSystem : ISimSystem
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

    /// <summary>At most this many challenges wait to be fought across the world before a new one to first blood is made.</summary>
    public const int MaxOpenChallenges = 4;

    /// <summary>Of those, how many may be between two ordinary people; the rest are kept for the story's own (<see cref="Prominence"/>).</summary>
    public const int OrdinaryChallenges = 2;

    /// <summary>How keenly a challenge is weighed when a notable fighter is in it (an ordinary one weighs 10).</summary>
    public const int NotableUrge = 70;

    /// <summary>How often a fight that matters turns a fighter's nature, in a hundred.</summary>
    public const int TurnChance = 30;

    /// <summary>A nature turned by a fight: one trait gives way to its opposite, told as news.</summary>
    private static void Turn(SimContext ctx, Character c, Character foe, string from, string to, string how, long cause)
    {
        c.Traits.Remove(from);
        if (!c.Traits.Contains(to)) c.Traits.Add(to);
        c.Traits.Sort(StringComparer.Ordinal);
        ctx.Emit(new EventDraft("TraitTurned", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref, ["foe"] = foe.Ref },
            Place: ctx.World.Settlements.GetValueOrDefault(c.SettlementId)?.Ref,
            Payload: new Dictionary<string, string> { ["from"] = from, ["to"] = to, ["how"] = how },
            Causes: new[] { new Cause(cause, CauseKind.Trigger, 0.9) }, Importance: Fates.Importance(c, 16), Tags: new[] { "Characters" }));
    }

    /// <summary>A beat of a duel that lasts more than a day, told on its day (sub-project 8 §5).</summary>
    public const string DuelTurnKind = "duel-turn";

    /// <summary>The last day of a duel that lasts more than a day: its outcome is applied.</summary>
    public const string DuelEndKind = "duel-end";

    public IEnumerable<string> HandledKinds => new[] { DuelKind, DuelTurnKind, DuelEndKind, ExpeditionKind };

    /// <summary>The duels declared and not yet fought, or begun and not yet ended, from the queue.</summary>
    public static IEnumerable<ScheduledAction> PendingDuels(EventQueue queue) => queue.Snapshot().Where(a => a.Kind is DuelKind or DuelEndKind);

    private static bool Challenged(EventQueue queue, long characterId) =>
        PendingDuels(queue).Any(a => a.Data["challenger"] == characterId.ToString(CultureInfo.InvariantCulture)
                                     || a.Data["defender"] == characterId.ToString(CultureInfo.InvariantCulture));

    public void OnYearlyTick(SimContext ctx)
    {
        Train(ctx);
        FormCompanies(ctx);
    }

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

        var beast = world.Monsters.Values.Where(m => m.State == MonsterState.Roaming && !m.Elder && nearby.Any(r => r.Id == m.RegionId))
            .OrderByDescending(m => m.Notoriety).ThenBy(m => m.Id).FirstOrDefault();
        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (beast is not null)
            options.Add(new($"hunt:{beast.RegionId}", (c.Ambition == Ambitions.Renown ? 50 : 6) * bold * Levels.Wariness(Levels.Of(c), Levels.Of(beast)),
                $"hunt {beast.Name} in {world.Regions[beast.RegionId].Name}"));
        else if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        // A labyrinth is delved level by level; the relics lost in its land lie on its deepest level.
        // The bottom of a labyrinth, and the secret of the world it keeps, waits for the audience (the owner, 2026-10-29:
        // secrets only from the year the chronicle first airs): before then no delve goes past the level above it.
        var aired = world.LastBroadcastDate is not null;
        var maze = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.ClearedEventId == 0 && !s.Great && s.ExpeditionEventId == 0
                                                      && Levels.MayDelve(c, s) && !InExpedition(ctx.Queue, c.Id) && nearby.Any(r => r.Id == s.RegionId)
                                                      && (aired || s.Depth + 1 < Math.Max(s.Levels, 3)))
            .OrderBy(s => s.Id).FirstOrDefault();
        if (maze is not null)
            options.Add(new($"maze:{maze.Id}", (c.Ambition == Ambitions.Artifact ? 40 : c.Ambition == Ambitions.Renown ? 25 : 2) * bold * Levels.Wariness(Levels.Of(c), Levels.Required(maze)),
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
        {
            // A sworn avenger bides their time until they are a match for their foe, then comes for their life.
            var ready = Encounters.Strength(world, c) >= Encounters.Strength(world, foe) * ReadyForRevenge;
            options.Add(new($"avenge:{foe.Id}", (ready ? 60 : 8) * bold * Levels.Wariness(c, foe), $"challenge {foe.Name} to the death"));
        }

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        // A notable one's quarrels reach further (the owner, 2026-10-29: about six showdowns a year, the story's own first):
        // anyone nearby they hold a grudge against, a milder one will do.
        if (rival is null && Prominence.IsNotable(world, c))
            rival = c.Memories.Select(m => m.About).Distinct()
                .Select(id => world.Characters.GetValueOrDefault(id))
                .OfType<Character>()
                .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt / 2
                            && world.Settlements.TryGetValue(o.SettlementId, out var at) && nearby.Any(r => r.Id == at.RegionId))
                .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        // And a notable one hungry for glory may call out another notable fighter nearby with no grudge at all: a contest
        // of honour between two of the story's own, to first blood.
        if (rival is null && Prominence.IsNotable(world, c) && (c.Ambition == Ambitions.Renown || c.Has(Traits.Brave) || c.Has(Traits.Wrathful)))
        {
            var regions = nearby.Select(r => r.Id).ToHashSet();
            rival = world.ActiveCharacters
                .Where(o => o.Id != c.Id && o.Stage(ctx.Date) != LifeStage.Child && o.Attributes.Prowess >= c.Attributes.Prowess - 3
                            && Relations.Opinion(c, o) < 20 && world.Settlements.TryGetValue(o.SettlementId, out var at) && regions.Contains(at.RegionId)
                            && Prominence.IsNotable(world, o))
                .OrderByDescending(o => o.Renown).ThenBy(o => o.Id).FirstOrDefault();
        }
        // A challenge to first blood is news (the owner, 2026-10-29: about six showdowns a year): only while fewer than
        // MaxOpenChallenges duels are pending in the whole world, and only OrdinaryChallenges of them may be between two of
        // the crowd; the rest wait for a notable fighter. A vengeance is never held back.
        // A notable one with a rival need not be wrathful or brave to call them out: their standing is at stake.
        var notable = rival is not null && (Prominence.IsNotable(world, c) || Prominence.IsNotable(world, rival));
        if (free && rival is not null && !Challenged(ctx.Queue, rival.Id) && (notable || c.Has(Traits.Wrathful) || c.Has(Traits.Brave))
            && PendingDuels(ctx.Queue).Count(a => a.Kind == DuelKind) is var open && open < MaxOpenChallenges
            && (open < OrdinaryChallenges || notable))
            options.Add(new($"duel:{rival.Id}", (notable ? NotableUrge : 10) * bold * Levels.Wariness(c, rival), $"challenge {rival.Name} to first blood"));
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
            case "maze": BeginExpedition(ctx, c, ctx.World.Structures[target]); break;
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            // One sought out by a higher level may keep out of their way (the owner, 2026-10-30).
            case "avenge" or "duel" when target != c.Id && Levels.Evades(ctx.World.Characters[target], c, ctx.Date.Day): Avoided(ctx, ctx.World.Characters[target], c); break;
            case "avenge" when target != c.Id: Equip(ctx, c); Equip(ctx, ctx.World.Characters[target]); Challenge(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Equip(ctx, c); Equip(ctx, ctx.World.Characters[target]); Challenge(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        if (ctx.World.Monsters.Values.Where(m => m.State == MonsterState.Roaming && !m.Elder && m.RegionId == region.Id).OrderBy(m => m.Id).FirstOrDefault() is { } beast)
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
    /// At the bottom of a cleared labyrinth lies a secret of the world (the owner, 2026-10-29): a truth the legend never
    /// told, written by Claude where it may, kept on the world and told as news.
    /// </summary>
    private void Reveal(SimContext ctx, Character delver, Structure maze, Region region, long clearedEventId, EntityRef? spoils)
    {
        var world = ctx.World;
        // What the songs forgot is found again first (the owner, 2026-10-29): the oldest forgotten tale, as a secret.
        if (LoreSystem.Buried(world) is { } tale)
        {
            world.Tales = world.Tales.Select(t => t == tale ? t with { Uncovered = true } : t).ToList();
            Announce(ctx, world, delver, maze, region, clearedEventId, spoils, $"The Forgotten {Narration.English.InProse(tale.Title).TrimStart()}".Replace("Forgotten the ", "Forgotten "),
                $"Carved at the bottom was a tale the songs forgot: {tale.Text}");
            return;
        }
        var rules = Legends.RulesSecret(world, maze.Name);
        var invention = _inventor.Invent(new InventionBrief("secret of the world", $"{maze.Ref}|secret",
            Legends.SecretFacts(world, $"{maze.Name} in {region.Name}", delver.Name), rules?.Title ?? $"The Secret of {Narration.English.InProse(maze.Name)}"));
        string title, text;
        if (invention.Lore is { Length: > 0 } lore) (title, text) = (invention.Name, lore.Length <= Legends.MaxSecret ? lore : lore[..Legends.MaxSecret]);
        else if (rules is { } told) (title, text) = told;
        else return; // nothing new to reveal: the labyrinth is cleared, its secret kept
        Announce(ctx, world, delver, maze, region, clearedEventId, spoils, title, text);
    }

    private static void Announce(SimContext ctx, World world, Character delver, Structure maze, Region region, long clearedEventId, EntityRef? spoils,
        string title, string text)
    {
        var who = new Dictionary<string, EntityRef> { ["delver"] = delver.Ref, ["structure"] = maze.Ref };
        if (spoils is { } relic) who["artifact"] = relic;
        var revealed = ctx.Emit(new EventDraft("SecretRevealed", Actors: who, Place: region.Ref,
            Payload: new Dictionary<string, string> { ["title"] = title, ["text"] = text },
            Causes: new[] { new Cause(clearedEventId, CauseKind.Trigger, 1.0) },
            Importance: Fates.Importance(delver, 70), Tags: new[] { "Characters", "Deeds", "Magic" }));
        world.Secrets.Add(new Secret(revealed.Id, title, text, ctx.Date.Day, region.Id, delver.Id));
        Glory(ctx, delver, 10);
        // A secret uncovered may bring one of the world's own resources to light (the owner, 2026-10-29).
        Wonders.Discover(ctx, delver, revealed.Id);
    }

    private static void Steal(SimContext ctx, Character thief, Artifact artifact)
    {
        var victim = ctx.World.Characters[artifact.HolderId];
        var guard = victim.Attributes.Intrigue + artifact.Bonus("intrigue") + 5 + (victim.Has(Traits.Paranoid) ? 5 : 0);
        var success = ctx.Rng.Chance(thief.Attributes.Intrigue / (double)(thief.Attributes.Intrigue + guard));
        var ev = ctx.Emit(new EventDraft(success ? "ArtifactStolen" : "TheftFoiled",
            Actors: new Dictionary<string, EntityRef> { ["thief"] = thief.Ref, ["victim"] = victim.Ref, ["artifact"] = artifact.Ref },
            Place: ctx.World.Settlements[thief.SettlementId].Ref,
            // How the victim came to hold it is what made it theirs to lose.
            Causes: artifact.Provenance.LastOrDefault() is { EventId: > 0 } got ? new[] { new Cause(got.EventId, CauseKind.Enabler, 0.4) } : null,
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
    /// Once the chronicle airs, a fighter stepping into a duel for the first time gets their own signature move and weapon,
    /// made by Claude to suit them (the owner, 2026-10-29: Claude decides what suits each character); offline, or when no
    /// answer comes, their calling's pools stand. A relic weapon outranks a made one.
    /// </summary>
    private void Equip(SimContext ctx, Character c)
    {
        var world = ctx.World;
        if (world.LastBroadcastDate is null) return;
        static string Clean(string text, int max)
        {
            var t = text.Replace("\"", "", StringComparison.Ordinal).Trim().TrimEnd('.');
            return t.Length <= max ? t : t[..max].TrimEnd();
        }
        var calling = Classes.Of(world, c);
        var facts = $"{c.Name}, {Narration.English.WithArticle(Classes.Title(calling))} of the {Peoples.Of(world, c)?.Name ?? "common folk"}, "
                    + $"{c.Role.ToString().ToLowerInvariant()}, traits {string.Join(", ", c.Traits)}; in a fight: {Talents.Brief(c)}.";
        if (c.Signature is null)
        {
            var pool = Abilities.Signature(world, c);
            var named = _inventor.Invent(new InventionBrief("signature ability", $"{c.Ref}|signature",
                facts + " Give them the one move in a fight that is theirs alone, fitting who they are: name it in two to four words, and tell what it does in a phrase.",
                pool.Name));
            if (named.Lore is { Length: > 0 } lore && Clean(named.Name, 40) is { Length: > 0 } name) c.Signature = new Ability(name, Clean(lore, 160));
        }
        var relic = world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id && a.Type == ArtifactType.Weapon);
        if (c.Weapon is null && !relic)
        {
            var named = _inventor.Invent(new InventionBrief("weapon", $"{c.Ref}|weapon",
                facts + " Arm them as suits them: describe the weapon they carry in a short phrase (\"a notched greataxe of black iron\"), and tell its story in one sentence.",
                Arms.Weapon(world, c).What));
            if (named.Lore is { Length: > 0 } lore && Clean(named.Name, 80) is { Length: > 0 } what) c.Weapon = new Gear("", what, Clean(lore, 220));
        }
    }

    /// <summary>
    /// The challenge (sub-project 8 §4): declared now with the odds as they stand, fought a year and a day later. The
    /// grudge behind it is its motive.
    /// </summary>
    /// <summary>One of a lower level kept out of the way of a higher one who came looking for a fight: no fight, told in passing.</summary>
    public static void Avoided(SimContext ctx, Character avoider, Character seeker) =>
        ctx.Emit(new EventDraft("FightAvoided", Actors: new Dictionary<string, EntityRef> { ["avoider"] = avoider.Ref, ["seeker"] = seeker.Ref },
            Place: ctx.World.Settlements.TryGetValue(avoider.SettlementId, out var home) ? home.Ref : (EntityRef?)null,
            Payload: new Dictionary<string, string>
            {
                ["avoider level"] = Levels.Of(avoider).ToString(CultureInfo.InvariantCulture),
                ["seeker level"] = Levels.Of(seeker).ToString(CultureInfo.InvariantCulture),
            },
            Importance: Chronicle.Compactor.MinorBelow, Tags: new[] { "Characters" }));

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
                ["stakes"] = stakes,
                ["odds"] = Percent(odds),
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

    /// <summary>The ways a fight starts without a challenge (the owner, 2026-10-29): enemies meeting, an ambush, a planned attack.</summary>
    public const string Encounter = "encounter", Ambush = "ambush", Attack = "attack";

    /// <summary>The surprise an attacker has: an ambush a third stronger, a planned attack a sixth; a chance meeting none.</summary>
    public static double Edge(string? how) => how switch { Ambush => 1.3, Attack => 1.15, _ => 1 };

    /// <summary>
    /// A clash with no challenge and no day set (the owner, 2026-10-29): enemies who meet and fight at once, an ambush, or an
    /// attack planned in secret. It is told as under way now, and its end, decided like a duel's, comes the next year.
    /// </summary>
    public static WorldEvent Clash(SimContext ctx, Character attacker, Character defender, bool toTheDeath, string how, long cause)
    {
        var odds = Encounters.DuelBetween(ctx.World, attacker, defender, toTheDeath, edge: Edge(how)).AttackerOdds;
        var stakes = toTheDeath ? "to the death" : "first blood";
        var verb = how switch
        {
            Ambush => "sprang an ambush on",
            Attack => "struck without warning at the home of",
            _ => "came upon their enemy",
        };
        var place = how == Encounter ? ctx.World.Settlements[attacker.SettlementId].Ref : ctx.World.Settlements[defender.SettlementId].Ref;
        var clash = ctx.Emit(new EventDraft("ClashBegun",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = attacker.Ref, ["defender"] = defender.Ref },
            Place: place,
            Payload: new Dictionary<string, string> { ["stakes"] = stakes, ["odds"] = Percent(odds), ["how"] = how, ["verb"] = verb },
            Causes: cause != 0 ? new[] { new Cause(cause, CauseKind.Motive, 0.9) } : null,
            Importance: Math.Max(Fates.Importance(attacker, toTheDeath ? 30 : 18), Fates.Importance(defender, toTheDeath ? 30 : 18)),
            Tags: new[] { "Characters", "Deeds" }));
        ctx.Queue.Schedule(ctx.Date.AddDays(DuelDelayDays), DuelKind, new Dictionary<string, string>
        {
            ["challenger"] = attacker.Id.ToString(CultureInfo.InvariantCulture),
            ["defender"] = defender.Id.ToString(CultureInfo.InvariantCulture),
            ["stakes"] = stakes,
            ["declared"] = clash.Id.ToString(CultureInfo.InvariantCulture),
            ["how"] = how,
        });
        return clash;
    }

    /// <summary>Whether a character has a duel or clash pending.</summary>
    public static bool Engaged(EventQueue queue, long characterId) => Challenged(queue, characterId);

    /// <summary>The challenger's chance as a whole percentage, as it is shown ("62").</summary>
    public static string Percent(double odds) => Math.Round(odds * 100).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The day of a declared duel: fought if both are still here, else called off. A duel Claude made longer than a day
    /// is told on its chapters' days and ends on its last, called off then if either side is gone.
    /// </summary>
    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        if (action.Kind == ExpeditionKind)
        {
            Expedition(ctx, action);
            return;
        }
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
        var how = action.Data.GetValueOrDefault("how");
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
                    var encounter = Encounters.DuelBetween(world, challenger, defender, toTheDeath, edge: Edge(how));
                    var outcome = encounter.Outcomes.First(o => o.Key == action.Data["outcome"]) with { Account = action.Data.GetValueOrDefault("account") };
                    Conclude(ctx, challenger, defender, toTheDeath, declared, outcome, begun, int.Parse(action.Data["days"], CultureInfo.InvariantCulture), how);
                    return;
                }
            default:
                // A clash's fighters are armed as a challenge's were when it was declared.
                if (how is not null)
                {
                    Equip(ctx, challenger);
                    Equip(ctx, defender);
                }
                Duel(ctx, challenger, defender, toTheDeath, declared, how);
                return;
        }
    }

    /// <summary>The chance a hidden awakening breaks out in a fight (the owner, 2026-10-29: turning the table mid-fight).</summary>
    public const double MidFightWake = 0.4;

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath, long declared, string? how = null)
    {
        // A hidden awakening may break out in the fight: the fighter fights on in their new calling.
        var surged = new[] { challenger, defender }.FirstOrDefault(c => c.Dormant != 0 && ctx.Log.TryGet(c.Dormant, out _) && ctx.Rng.Chance(MidFightWake));
        Calling? surge = null;
        var was = surged?.Class ?? "";
        if (surged is not null)
        {
            surge = Awakenings.Invent(ctx.World, surged, ctx.Log.Get(surged.Dormant), _inventor, Awakenings.MidFight, ctx.Date.Day);
            Awakenings.Take(ctx.World, surged, surge);
        }
        void Surge(long cause)
        {
            if (surged is not null) Awakenings.Announce(ctx, surged, surge!, was, cause, Awakenings.MidFight, ctx.World.Settlements[defender.SettlementId].Ref);
        }
        // A fight that matters may turn a nature before a blow is struck (the owner, 2026-10-29): a coward finds courage
        // defending their home, fighting for their life, or for the dead they mean to avenge; a calm one facing the foe they
        // swore vengeance on is overtaken by rage. Rolled from the fighters themselves, not the world's dice.
        foreach (var (c, foe) in new[] { (defender, challenger), (challenger, defender) })
        {
            var avenging = c.Ambition == Ambitions.Vengeance && c.AmbitionTarget == foe.Id;
            if (c.Has(Traits.Craven) && (c == defender || toTheDeath || avenging) && Hash.Percent(c.Id, 21, declared) < TurnChance)
                Turn(ctx, c, foe, Traits.Craven, Traits.Brave, "found courage when it mattered, and would not run", declared);
            else if (c.Has(Traits.Calm) && avenging && Hash.Percent(c.Id, 22, declared) < TurnChance)
                Turn(ctx, c, foe, Traits.Calm, Traits.Wrathful, "lost their calm at the sight of the one they had sworn to destroy", declared);
        }
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath, surged?.Id ?? 0, Edge(how)), ctx.Rng);
        if (outcome.Days <= 1)
        {
            Surge(Conclude(ctx, challenger, defender, toTheDeath, declared, outcome, 0, 1, how).Id);
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
                Payload: How(new Dictionary<string, string> { ["stakes"] = stakes, ["days"] = days.ToString(CultureInfo.InvariantCulture) }, how),
                Causes: new[] { new Cause(declared, CauseKind.Trigger, 1.0) },
                Importance: Math.Max(Fates.Importance(challenger, toTheDeath ? 25 : 12), Fates.Importance(defender, toTheDeath ? 25 : 12)),
                Tags: new[] { "Characters", "Deeds" }));
        Surge(begun.Id);
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
        Dictionary<string, string> DataOf(int day) => how is null ? Data(day) : new Dictionary<string, string>(Data(day)) { ["how"] = how };
        foreach (var day in chapters.Select(c => c.Day).Where(d => d > 1 && d < days).Distinct())
        {
            var data = DataOf(day);
            data["account"] = FightChapter.On(chapters, day)!;
            ctx.Queue.Schedule(ctx.Date.AddDays(day - 1), DuelTurnKind, data);
        }
        var end = DataOf(days);
        end["outcome"] = outcome.Key;
        if (FightChapter.On(chapters, days) is { } last) end["account"] = last;
        ctx.Queue.Schedule(ctx.Date.AddDays(days - 1), DuelEndKind, end);
    }

    private static Dictionary<string, string> How(Dictionary<string, string> payload, string? how)
    {
        if (how is not null) payload["how"] = how;
        return payload;
    }

    /// <summary>The duel's end: its outcome applied, on the day it was fought or the last day of a long one.</summary>
    private static WorldEvent Conclude(SimContext ctx, Character challenger, Character defender, bool toTheDeath, long declared,
        EncounterOutcome outcome, long begun, int days, string? how = null)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var payload = How(new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key }, how);
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
        // Who comes out of a fight to the death alive, beaten, swears to return (the owner, 2026-10-28: a nemesis).
        var loserFate = outcome.AttackerWins ? outcome.DefenderFate : outcome.AttackerFate;
        if (toTheDeath && loserFate != Fate.Dead && loser.Id != winner.Id && !loser.Has(Traits.Craven)
            && !SagaSystem.IsDemonLord(ctx.World, loser))
            Nemesis(ctx, loser, winner, duel);
        if (toTheDeath && outcome.AttackerWins && challenger.Ambition == Ambitions.Vengeance)
            challenger.Ambition = CharacterFactory.ChooseAmbition(challenger, ctx.Rng);
        Suffer(ctx, challenger, outcome.AttackerFate, "a duel", duel, outcome.AttackerFate == Fate.Dead ? defender : null);
        Suffer(ctx, defender, outcome.DefenderFate, "a duel", duel, outcome.DefenderFate == Fate.Dead ? challenger : null);
        // What the fight leaves in them (the owner, 2026-10-29: more than courage): a cruel victor may spare the beaten and be
        // changed by it; a brave one maimed may lose their nerve; a lazy one beaten may be shaken into discipline; a content
        // victor may get a taste for more.
        if (winner.IsAlive && loser.IsAlive && winner.Id != loser.Id)
        {
            if (winner.Has(Traits.Cruel) && Hash.Percent(winner.Id, 23, duel.Id) < TurnChance / 2)
                Turn(ctx, winner, loser, Traits.Cruel, Traits.Kind, "stood over the beaten foe and, for once, showed mercy", duel.Id);
            else if (winner.Has(Traits.Content) && Hash.Percent(winner.Id, 24, duel.Id) < TurnChance / 2)
                Turn(ctx, winner, loser, Traits.Content, Traits.Ambitious, "tasted victory and found it wanted more", duel.Id);
            if (loser.Has(Traits.Brave) && loser.Has(Traits.Maimed) && Hash.Percent(loser.Id, 25, duel.Id) < TurnChance)
                Turn(ctx, loser, winner, Traits.Brave, Traits.Craven, "was maimed, and the nerve went out of them", duel.Id);
            else if (loser.Has(Traits.Lazy) && Hash.Percent(loser.Id, 26, duel.Id) < TurnChance)
                Turn(ctx, loser, winner, Traits.Lazy, Traits.Diligent, "was beaten, and swore never to be caught unready again", duel.Id);
        }
        return duel;
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

    /// <summary>How near a foe's strength a sworn avenger must come before returning for them.</summary>
    public const double ReadyForRevenge = 0.9;

    /// <summary>The beaten who escaped a fight to the death swear to come back for the victor's life.</summary>
    private static void Nemesis(SimContext ctx, Character sworn, Character foe, WorldEvent duel)
    {
        if (sworn.Ambition == Ambitions.Vengeance && sworn.AmbitionTarget == foe.Id) return;
        sworn.Ambition = Ambitions.Vengeance;
        sworn.AmbitionTarget = foe.Id;
        ctx.Emit(new EventDraft("VengeanceSworn",
            Actors: new Dictionary<string, EntityRef> { ["sworn"] = sworn.Ref, ["foe"] = foe.Ref },
            Place: ctx.World.Settlements[sworn.SettlementId].Ref,
            Causes: new[] { new Cause(duel.Id, CauseKind.Trigger, 1.0) },
            Importance: Math.Max(Fates.Importance(sworn, 22), Fates.Importance(foe, 18)), Tags: new[] { "Characters", "Deeds" }));
    }

    /// <summary>
    /// Each year a sworn avenger whose foe still lives grows stronger at their best: a point of prowess or martial skill
    /// (up to 20). The chronicle tells it once, the year they become a match for their foe and are ready to return.
    /// </summary>
    private static void Train(SimContext ctx)
    {
        foreach (var c in ctx.World.Characters.Values.Where(c => c.IsActive && c.Ambition == Ambitions.Vengeance).OrderBy(c => c.Id).ToList())
        {
            if (!ctx.World.Characters.TryGetValue(c.AmbitionTarget, out var foe) || !foe.IsActive) continue;
            var a = c.Attributes;
            var prowess = a.Prowess >= a.Martial;
            if ((prowess ? a.Prowess : a.Martial) >= 20) continue;
            bool Ready() => Encounters.Strength(ctx.World, c) >= Encounters.Strength(ctx.World, foe) * ReadyForRevenge;
            var was = Ready();
            if (prowess) a.Prowess++; else a.Martial++;
            if (was || !Ready()) continue;
            ctx.Emit(new EventDraft("TrainedForVengeance",
                Actors: new Dictionary<string, EntityRef> { ["sworn"] = c.Ref, ["foe"] = foe.Ref },
                Place: ctx.World.Settlements[c.SettlementId].Ref,
                Payload: new Dictionary<string, string> { ["skill"] = prowess ? "prowess" : "martial skill" },
                Importance: Fates.Importance(c, 10), Tags: new[] { "Characters" }));
        }
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

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

    public DeedsSystem(IDecider? decider = null, IArbiter? arbiter = null)
    {
        _decider = decider ?? new RulesDecider();
        _arbiter = arbiter ?? new RulesArbiter();
    }

    public string Name => "deeds";

    public void OnMonthlyTick(SimContext ctx)
    {
        var residents = ctx.World.ActiveCharacters.ToLookup(c => c.SettlementId);
        foreach (var c in residents.SelectMany(g => g).OrderBy(c => c.Id).ToList())
        {
            if (!c.IsActive || c.Stage(ctx.Date) == LifeStage.Child) continue;
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

        var lair = nearby.Where(r => r.Danger >= 50).OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (lair is not null)
            options.Add(new($"hunt:{lair.Id}", (c.Ambition == Ambitions.Renown ? 40 : 5) * bold, $"hunt the monsters of {lair.Name}"));

        var lost = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Lost && nearby.Any(r => r.Id == a.RegionId))
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

        if (c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive && foe.Id != c.Id
            && nearby.Any(r => r.Id == world.Settlements[foe.SettlementId].RegionId))
            options.Add(new($"avenge:{foe.Id}", 50 * bold, $"challenge {foe.Name} to the death"));

        var rival = (neighbours ?? world.PeopleOf(home))
            .Where(o => o.Id != c.Id && o.IsActive && o.Stage(ctx.Date) != LifeStage.Child && Relations.Opinion(c, o) <= Relations.RivalAt)
            .OrderBy(o => Relations.Opinion(c, o)).ThenBy(o => o.Id).FirstOrDefault();
        if (rival is not null && (c.Has(Traits.Wrathful) || c.Has(Traits.Brave)))
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
            case "steal": Steal(ctx, c, ctx.World.Artifacts[target]); break;
            case "study": c.Attributes.Lore = Math.Min(20, c.Attributes.Lore + 1); break;
            case "forge": Forge(ctx, c); break;
            case "avenge" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: true); break;
            case "duel" when target != c.Id: Duel(ctx, c, ctx.World.Characters[target], toTheDeath: false); break;
        }
    }

    private void Hunt(SimContext ctx, Character hunter, Region region)
    {
        var outcome = _arbiter.Resolve(Encounters.HuntIn(ctx.World, hunter, region), ctx.Rng);
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

    private void Delve(SimContext ctx, Character delver, Artifact artifact)
    {
        var region = ctx.World.Regions[artifact.RegionId];
        var outcome = _arbiter.Resolve(Encounters.DelveIn(ctx.World, delver, region), ctx.Rng);
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

    private static void Forge(SimContext ctx, Character mage)
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
        world.Artifacts[artifact.Id] = artifact;
        Fates.MoveToHolder(artifact, mage, forged.Id, "forged");
        Glory(ctx, mage, 10);
    }

    private void Duel(SimContext ctx, Character challenger, Character defender, bool toTheDeath)
    {
        var grudge = challenger.Memories.Where(m => m.About == defender.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(ctx.World, challenger, defender, toTheDeath), ctx.Rng);
        var duel = ctx.Emit(new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = challenger.Ref, ["defender"] = defender.Ref },
            Place: ctx.World.Settlements[defender.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = toTheDeath ? "to the death" : "first blood", ["outcome"] = outcome.Key },
            Causes: grudge is not null ? new[] { new Cause(grudge.EventId, CauseKind.Motive, 0.9) } : null,
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

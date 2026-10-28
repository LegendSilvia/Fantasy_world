using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Structures (spec §5, minimal hooks for sub-project 3), yearly and last in the tick order. Wealthy lords commission
/// landmarks that take years to raise (paid from the settlement's wealth; completion brings renown). Labyrinths open
/// in dangerous, ley-rich regions, raising the danger and hiding a lost artifact for delvers. Abandoned settlements
/// leave ruins, and their unfinished or standing landmarks fall with them.
/// Great works (sub-project 3 §6): a landmark has an architect; war, plague or famine hold the work up; an impatient
/// patron rushes it, at the risk of collapse; on completion the patron gains renown and the realm stability. Towns that
/// have known siege raise fortresses, which strengthen their defence. A lord may restore a fallen landmark.
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

    public const double RushedCollapse = 0.08;
    public const double FortressPerYear = 0.03;
    public const double FortressWealth = 2500;
    public const int FortressDefence = 400;
    public const double RestorePerYear = 0.05;

    private static readonly string[] Works = { "Spire", "Great Hall", "Bridge", "Temple", "Library", "Colossus", "Lighthouse", "Garden" };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "structures";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        Ruin(ctx);
        Build(ctx);
        Commission(ctx);
        Fortify(ctx);
        Restore(ctx);
        OpenLabyrinths(ctx);
    }

    /// <summary>A standing fortress guards the town.</summary>
    public static bool Fortified(World world, Settlement s) =>
        world.Structures.Values.Any(x => x.Type == StructureKind.Fortification && x.State == StructureState.Standing && x.SettlementId == s.Id);

    /// <summary>The town's best designer: a grown notable, not its lord, by lore and stewardship.</summary>
    public static Character? Architect(World world, Settlement seat, Core.WorldDate today) =>
        world.ActiveCharacters.Where(c => c.SettlementId == seat.Id && c.Id != seat.LordId && c.Stage(today) != LifeStage.Child)
            .OrderByDescending(c => c.Attributes.Lore + c.Attributes.Stewardship).ThenBy(c => c.Id).FirstOrDefault();

    public static int LabyrinthCap(World world) => Math.Max(1, world.Regions.Count / 15);

    private void Commission(SimContext ctx)
    {
        var world = ctx.World;
        var busy = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= LandmarkWealth
                                                                 && !busy.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            if (!ctx.Rng.Chance(CommissionPerYear)) continue;
            var fallback = $"The {Works[ctx.Rng.NextInt(0, Works.Length)]} of {seat.Name}";
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("landmark", $"{Structure.Kind}#{id}",
                $"A landmark commissioned by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}.", fallback));
            seat.Wealth *= 0.8;
            var architect = Architect(world, seat, ctx.Date);
            var actors = new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) };
            if (architect is not null) actors["architect"] = architect.Ref;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun", Actors: actors,
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = architect?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
                Rushed = lord.Has(Traits.Ambitious) || lord.Has(Traits.Wrathful),
            };
        }
    }

    /// <summary>
    /// What holds a work up this year: war at the town, plague or famine in its land, or its realm short of timber or
    /// stone (the event that began it).
    /// </summary>
    private static long Hindrance(SimContext ctx, Settlement seat)
    {
        var world = ctx.World;
        var region = world.Regions[seat.RegionId];
        if (Realms.SovereignOf(world, region) is { } realm)
            foreach (var kind in new[] { ResourceKinds.Timber, ResourceKinds.Stone })
                if (Stocks.Short(realm, kind) && !Stocks.CanBuild(realm, kind)) return realm.Conditions["short:" + kind];
        if (region.OccupierRealmId != 0 || region.BesiegerArmyId != 0)
            return ctx.Log.Involving(region.Ref).Concat(ctx.Log.Involving(seat.Ref))
                .Where(e => e.Type is "RegionOccupied" or "SiegeBegan" or "SiegeEnded").Select(e => e.Id).DefaultIfEmpty().Max();
        var plague = region.Conditions.GetValueOrDefault(Conditions.Plague);
        return plague != 0 ? plague : region.Conditions.GetValueOrDefault(Conditions.Shortage);
    }

    private void Fortify(SimContext ctx)
    {
        var world = ctx.World;
        var guarded = world.Structures.Values.Where(s => s.Type == StructureKind.Fortification && s.State != StructureState.Ruined)
            .Select(s => s.SettlementId).ToHashSet();
        foreach (var seat in world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && s.Wealth >= FortressWealth
                                                                 && !guarded.Contains(s.Id)).ToList())
        {
            if (!world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive || lord.Stage(ctx.Date) == LifeStage.Child) continue;
            var region = world.Regions[seat.RegionId];
            var siege = ctx.Log.Involving(seat.Ref).Concat(ctx.Log.Involving(region.Ref))
                .Where(e => e.Type is "SiegeEnded" or "RegionOccupied").OrderBy(e => e.Id).LastOrDefault();
            if (siege is null || !ctx.Rng.Chance(FortressPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("fortress", $"{Structure.Kind}#{id}",
                $"A fortress raised by {lord.Name}, lord of {seat.Name}, in year {ctx.Date.Year}, after the town knew war.", $"The Walls of {seat.Name}"));
            seat.Wealth *= 0.8;
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("FortificationBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: new[] { new Cause(siege.Id, CauseKind.Motive, 0.8) }, Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures", "War" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Fortification, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, ArchitectId = Architect(world, seat, ctx.Date)?.Id ?? 0, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Restore(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s is { Type: StructureKind.Landmark or StructureKind.Fortification, State: StructureState.Ruined }).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned || seat.Wealth < LandmarkWealth || !world.Characters.TryGetValue(seat.LordId, out var lord) || !lord.IsActive) continue;
            if (!ctx.Rng.Chance(RestorePerYear)) continue;
            var fell = ctx.Log.Involving(work.Ref).LastOrDefault(e => e.Type is "WorksCollapsed" or "StructureRuined");
            seat.Wealth *= 0.85;
            work.State = StructureState.Building;
            work.Progress = 50;
            work.DelayedByEventId = 0;
            ctx.Emit(new EventDraft("RestorationBegun", Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = work.Ref },
                Place: seat.Ref, Causes: fell is null ? null : new[] { new Cause(fell.Id, CauseKind.Motive, 0.7) },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            if (seat.Abandoned) continue; // its ruin comes with the town's (Ruin)
            var hindrance = Hindrance(ctx, seat);
            if (hindrance != 0)
            {
                if (work.DelayedByEventId != hindrance)
                {
                    work.DelayedByEventId = hindrance;
                    ctx.Emit(new EventDraft("WorksDelayed", Actors: new Dictionary<string, EntityRef> { ["structure"] = work.Ref }, Place: seat.Ref,
                        Causes: new[] { new Cause(hindrance, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Structures" }));
                }
                continue;
            }
            work.DelayedByEventId = 0;
            // The year's work uses the realm's timber and stone, bought when its land has none (sub-project 12); what it can
            // neither find nor pay for holds the work up from next year.
            if (Realms.SovereignOf(world, world.Regions[seat.RegionId]) is { } payer)
            {
                Stocks.Supply(ctx, payer, ResourceKinds.Timber, Stocks.BuildingCost);
                Stocks.Supply(ctx, payer, ResourceKinds.Stone, Stocks.BuildingCost);
            }
            var step = ctx.Rng.NextInt(7, 17) * (work.Rushed ? 1.6 : 1);
            work.Progress = Math.Min(100, work.Progress + step);
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Rushed && work.Progress < 100 && ctx.Rng.Chance(RushedCollapse))
            {
                Collapse(ctx, work, seat);
                continue;
            }
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var architect = world.Characters.GetValueOrDefault(work.ArchitectId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            if (architect is not null) actors["architect"] = architect.Ref;
            var fortress = work.Type == StructureKind.Fortification;
            var done = ctx.Emit(new EventDraft(fortress ? "FortificationCompleted" : "LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: fortress ? new[] { "Structures", "War" } : new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
            if (architect is { IsAlive: true }) architect.Renown = Math.Min(100, architect.Renown + 8);
            if (Realms.SovereignOf(world, world.Regions[seat.RegionId]) is { } realm) realm.Stability = Math.Min(100, realm.Stability + 5);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        // A land whose labyrinth was cleared does not open another.
        var haunted = labyrinths.Select(s => s.RegionId).Concat(world.Structures.Values.Where(s => s.GuardianId != 0).Select(s => s.RegionId)).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            var guardianId = world.Ids.Take(Monster.Kind);
            var levels = ctx.Rng.NextInt(3, 8);
            var breed = Monster.GuardianBreeds[ctx.Rng.NextInt(0, Monster.GuardianBreeds.Count)];
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id), ["guardian"] = new(Monster.Kind, guardianId) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
                Levels = levels, GuardianId = guardianId,
            };
            var deeps = invention.Name.StartsWith("The ", StringComparison.Ordinal) ? "the " + invention.Name[4..] : invention.Name;
            world.Monsters[guardianId] = new Monster
            {
                Id = guardianId, Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {deeps}", Breed = breed, RegionId = region.Id,
                Might = Math.Clamp(30 + 8 * levels + ctx.Rng.NextInt(0, 11), 1, 100), BornDay = ctx.Date.Day, State = MonsterState.Lairing,
                RoseEventId = opened.Id, LairStructureId = id,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            // Its treasures (sub-project 13): one every other level, the deeper the more famous, the greatest at the bottom.
            foreach (var level in Enumerable.Range(1, levels).Where(l => l == levels || l % 2 == 0))
            {
                if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) >= DeedsSystem.ArtifactCap(world)) break;
                var treasure = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id,
                    fame: 10 + 6 * level);
                world.Artifacts[treasure.Id] = treasure;
                Fates.Lose(treasure, region.Id, opened.Id, 0);
                treasure.Level = level == levels ? 0 : level;
            }
            return; // at most one a year
        }
    }

    private static void Ruin(SimContext ctx)
    {
        var world = ctx.World;
        var ruined = world.Structures.Values.Where(s => s.Type == StructureKind.Ruin).Select(s => s.SettlementId).ToHashSet();
        foreach (var town in world.Settlements.Values.Where(s => s.Abandoned && !ruined.Contains(s.Id)).ToList())
        {
            var abandonment = ctx.Log.Involving(town.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned");
            var id = world.Ids.Take(Structure.Kind);
            var fell = ctx.Emit(new EventDraft("StructureRuined",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: town.Ref, Payload: new Dictionary<string, string> { ["name"] = $"The Ruins of {town.Name}" },
                Causes: abandonment is null ? null : new[] { new Cause(abandonment.Id, CauseKind.Trigger, 1.0) },
                Importance: 25, Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = $"The Ruins of {town.Name}", Type = StructureKind.Ruin, RegionId = town.RegionId, SettlementId = town.Id,
                StartedEventId = fell.Id, CompletedEventId = fell.Id, Progress = 100, State = StructureState.Ruined,
            };
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type is StructureKind.Landmark or StructureKind.Fortification && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }

    /// <summary>A rushed work falls: people die, half the work is lost, and the architect is blamed.</summary>
    private static void Collapse(SimContext ctx, Structure work, Settlement seat)
    {
        var before = seat.Population;
        Demography.Kill(seat, 0.005, ctx.Rng);
        work.Progress = Math.Max(0, work.Progress - 50);
        var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
        if (ctx.World.Characters.TryGetValue(work.ArchitectId, out var architect))
        {
            actors["architect"] = architect.Ref;
            architect.Renown = Math.Max(0, architect.Renown - 10);
        }
        var builder = ctx.World.Characters.GetValueOrDefault(work.BuilderId);
        ctx.Emit(new EventDraft("WorksCollapsed", Actors: actors, Place: seat.Ref,
            Payload: new Dictionary<string, string>
            {
                ["dead"] = (before - seat.Population).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ruined"] = work.Progress <= 0 ? "yes" : "no",
            },
            Causes: new[] { new Cause(work.StartedEventId, CauseKind.Enabler, 0.7) },
            Importance: builder is null ? 40 : Fates.Importance(builder, 40), Tags: new[] { "Structures" }));
        if (work.Progress <= 0) work.State = StructureState.Ruined;
    }
}

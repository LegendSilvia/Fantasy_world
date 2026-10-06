using FantasyWorld.Sim.Agency;
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
/// </summary>
public sealed class StructureSystem(IInventor? inventor = null) : ISimSystem
{
    public const double CommissionPerYear = 0.02;
    public const double LandmarkWealth = 3000;
    public const double LabyrinthPerYear = 0.02;
    public const double LabyrinthLey = 70;
    public const double LabyrinthDanger = 35;

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
        OpenLabyrinths(ctx);
    }

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
            using var lore = ctx.Annotate("lore", invention.Lore);
            var begun = ctx.Emit(new EventDraft("LandmarkBegun",
                Actors: new Dictionary<string, EntityRef> { ["builder"] = lord.Ref, ["structure"] = new(Structure.Kind, id) },
                Place: seat.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Importance: Fates.Importance(lord, 30), Tags: new[] { "Structures" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Landmark, RegionId = seat.RegionId, SettlementId = seat.Id,
                BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building,
            };
        }
    }

    private static void Build(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var work in world.Structures.Values.Where(s => s.State == StructureState.Building).ToList())
        {
            var seat = world.Settlements[work.SettlementId];
            work.Progress = Math.Min(100, work.Progress + ctx.Rng.NextInt(7, 17));
            seat.Wealth = Math.Max(0, seat.Wealth - 0.02 * seat.Wealth);
            if (work.Progress < 100) continue;
            var builder = world.Characters.GetValueOrDefault(work.BuilderId);
            var actors = new Dictionary<string, EntityRef> { ["structure"] = work.Ref };
            if (builder is not null) actors["builder"] = builder.Ref;
            var done = ctx.Emit(new EventDraft("LandmarkCompleted", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["name"] = work.Name },
                Causes: new[] { new Cause(work.StartedEventId, CauseKind.Trigger, 1.0) },
                Importance: builder is null ? 45 : Fates.Importance(builder, 45), Tags: new[] { "Structures" }));
            work.State = StructureState.Standing;
            work.CompletedEventId = done.Id;
            if (builder is { IsAlive: true }) builder.Renown = Math.Min(100, builder.Renown + 10);
        }
    }

    private void OpenLabyrinths(SimContext ctx)
    {
        var world = ctx.World;
        var labyrinths = world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth).ToList();
        if (labyrinths.Count >= LabyrinthCap(world)) return;
        var haunted = labyrinths.Select(s => s.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.LeyPower >= LabyrinthLey && r.Danger >= LabyrinthDanger && !haunted.Contains(r.Id)).ToList())
        {
            if (!ctx.Rng.Chance(LabyrinthPerYear)) continue;
            var id = world.Ids.Take(Structure.Kind);
            var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
                $"A labyrinth that opened in the wild, ley-charged land of {region.Name} in year {ctx.Date.Year}.", $"The Deeps of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var opened = ctx.Emit(new EventDraft("LabyrinthOpened",
                Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.7), (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
                Importance: 50, Tags: new[] { "Structures", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id, Name = invention.Name, Type = StructureKind.Labyrinth, RegionId = region.Id,
                StartedEventId = opened.Id, CompletedEventId = opened.Id, Progress = 100, State = StructureState.Standing,
            };
            region.Danger = Math.Min(100, region.Danger + 15);
            if (world.Artifacts.Values.Count(a => a.Place != ArtifactPlace.Destroyed) < DeedsSystem.ArtifactCap(world))
            {
                var relic = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng, world.Cultures.GetValueOrDefault(region.CultureId)?.Style), opened.Id, fame: 20);
                world.Artifacts[relic.Id] = relic;
                Fates.Lose(relic, region.Id, opened.Id, 0);
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
            foreach (var work in world.Structures.Values.Where(s => s.SettlementId == town.Id && s.Type == StructureKind.Landmark && s.State != StructureState.Ruined))
                work.State = StructureState.Ruined;
        }
    }
}

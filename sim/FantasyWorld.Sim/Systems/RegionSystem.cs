using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Yearly odds of each region event (rolled monthly as odds / 12). Plague spread odds are per month
/// per link. Tests pass custom odds to force or silence events.
/// </summary>
public sealed record RegionOdds(
    double Drought = 0.06,
    double Flood = 0.05,
    double Blight = 0.02,
    double Plague = 0.008,
    double LeySurge = 0.02,
    double Earthquake = 0.01,
    double Monsters = 0.08,
    double Discovery = 0.015,
    double PlagueSpreadLand = 0.04,
    double PlagueSpreadSea = 0.02)
{
    public static readonly RegionOdds None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The land itself: fertility, danger and ley power drift; droughts, floods, blights, plagues,
/// ley surges, earthquakes, monster migrations and discoveries happen, each citing what enabled it.
/// Also records each region's defining events in Region.Memory.
/// </summary>
public sealed class RegionSystem : ISimSystem
{
    public const string ConditionEndKind = "region.condition.end";
    public const string MemoryCursor = "regions.memoryCursor";
    public const int MemorySize = 12;
    public const int MemoryImportance = 35;
    public const int ImmunityDays = 10 * 360;

    private readonly RegionOdds _odds;

    public RegionSystem(RegionOdds? odds = null) => _odds = odds ?? new RegionOdds();

    public string Name => "regions";

    public IEnumerable<string> HandledKinds => new[] { ConditionEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var plagued = world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.Plague)).ToList();
        foreach (var region in world.Regions.Values)
        {
            var settlements = world.LivingSettlements(region).ToList();
            Drift(ctx, region, settlements);
            RollEvents(ctx, region, settlements);
            if (region.Conditions.ContainsKey(Conditions.Plague))
                foreach (var s in settlements) Demography.Kill(s, 0.015, ctx.Rng);
        }
        SpreadPlague(ctx, plagued);
        Remember(ctx);
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var region = ctx.World.Regions[long.Parse(action.Data["region"], CultureInfo.InvariantCulture)];
        var condition = action.Data["condition"];
        var began = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!region.Conditions.TryGetValue(condition, out var current) || current != began) return;
        region.Conditions.Remove(condition);
        if (action.Data["end"] is not { Length: > 0 } endType) return;
        var ended = ctx.Emit(new EventDraft(endType, Place: region.Ref,
            Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: 10, Tags: new[] { "Regions" }));
        if (condition == Conditions.Plague) Hold(ctx, region, Conditions.Immune, ended.Id, ImmunityDays);
    }

    private static void Drift(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var farmers = settlements.Sum(Demography.Farmers);
        var capacity = Demography.FarmCapacity(region);
        region.Fertility = capacity > 0 && farmers > capacity * 0.9
            ? Math.Max(0, region.Fertility - 0.2)
            : Math.Clamp(region.Fertility + (region.BaseFertility - region.Fertility) * 0.02, 0, 100);

        var population = settlements.Sum(s => s.Population);
        var dangerTarget = Math.Max(0, region.BaseDanger - Math.Min(20, population / 500.0));
        region.Danger = Math.Clamp(region.Danger + (dangerTarget - region.Danger) * 0.03, 0, 100);
        region.LeyPower = Math.Clamp(region.LeyPower + (region.BaseLey - region.LeyPower) * 0.01, 0, 100);

        Threshold(ctx, region, Conditions.Exhausted,
            begins: region.Fertility < region.BaseFertility * 0.7, ends: region.Fertility >= region.BaseFertility * 0.85,
            "FertilityExhausted", "FertilityRecovered", importance: 25, Array.Empty<Cause>());
        Threshold(ctx, region, Conditions.Dangerous,
            begins: region.Danger >= 70, ends: region.Danger < 50,
            "DangerRose", "DangerSubsided", importance: 30,
            Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Enabler, 0.7)));
    }

    /// <summary>Condition events for a value crossing a threshold, with hysteresis.</summary>
    private static void Threshold(SimContext ctx, Region region, string condition, bool begins, bool ends,
        string beginType, string endType, int importance, IReadOnlyList<Cause> causes)
    {
        var active = region.Conditions.TryGetValue(condition, out var began);
        if (!active && begins)
            region.Conditions[condition] = ctx.Emit(new EventDraft(beginType, Place: region.Ref, Causes: causes,
                Importance: importance, Tags: new[] { "Regions" })).Id;
        else if (active && ends)
        {
            ctx.Emit(new EventDraft(endType, Place: region.Ref, Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) },
                Importance: 10, Tags: new[] { "Regions" }));
            region.Conditions.Remove(condition);
        }
    }

    private void RollEvents(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var season = ctx.Date.Season;
        var c = region.Conditions;

        var droughtOdds = _odds.Drought
                          * region.Climate switch { ClimateBand.Hot => 1.5, ClimateBand.Cold => 0.5, _ => 1.0 }
                          * (region.Biome is Biome.Swamp or Biome.Jungle ? 0.3 : 1.0);
        if (season == Season.Summer && !c.ContainsKey(Conditions.Drought) && Roll(ctx, droughtOdds))
        {
            var autumnEnds = WorldDate.FromYmd(ctx.Date.Year, 10, 1);
            Begin(ctx, region, Conditions.Drought, "Drought", 35, autumnEnds.Day - ctx.Date.Day, "DroughtEnded");
        }

        if (season == Season.Spring && region.HasRiver && !c.ContainsKey(Conditions.Flood) && Roll(ctx, _odds.Flood))
        {
            region.Food *= 0.75;
            region.Fertility = Math.Min(100, region.Fertility + 3);
            foreach (var s in settlements) Demography.Kill(s, 0.005, ctx.Rng);
            Begin(ctx, region, Conditions.Flood, "Flood", 30, 120);
        }

        if (ctx.Date.Month is >= 2 and <= 8 && region.Farmland > 0 && !c.ContainsKey(Conditions.Blight) && Roll(ctx, _odds.Blight))
            Begin(ctx, region, Conditions.Blight, "Blight", 25, 240, "BlightEnded");

        var population = settlements.Sum(s => s.Population);
        var plagueOdds = _odds.Plague * (1 + population / 10_000.0) * (c.ContainsKey(Conditions.Shortage) ? 2 : 1);
        if (!c.ContainsKey(Conditions.Plague) && !c.ContainsKey(Conditions.Immune) && Roll(ctx, plagueOdds))
            Begin(ctx, region, Conditions.Plague, "PlagueOutbreak", 50, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                Conditions.Cite(c, (Conditions.Shortage, CauseKind.Enabler, 0.6)));

        if (!c.ContainsKey(Conditions.LeySurge) && Roll(ctx, _odds.LeySurge * region.LeyPower / 50))
        {
            region.LeyPower = Math.Min(100, region.LeyPower + 20);
            region.Danger = Math.Min(100, region.Danger + 10);
            Begin(ctx, region, Conditions.LeySurge, "LeySurge", 30, 720);
        }

        if (region.MountainShare >= 0.05) Earthquake(ctx, region, settlements);
        if (region.Danger >= 65 && Roll(ctx, _odds.Monsters)) Monsters(ctx, region);
        if (Roll(ctx, _odds.Discovery)) Discovery(ctx, region);
    }

    private void Earthquake(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var surge = new[] { region }.Concat(Neighbours(ctx, region))
            .Select(r => r.Conditions.TryGetValue(Conditions.LeySurge, out var id) ? id : 0)
            .FirstOrDefault(id => id > 0);
        if (!Roll(ctx, _odds.Earthquake * (surge > 0 ? 5 : 1))) return;
        foreach (var s in settlements)
        {
            s.Walls = Math.Max(0, s.Walls - 1);
            Demography.Kill(s, 0.01, ctx.Rng);
        }
        region.Food *= 0.9;
        ctx.Emit(new EventDraft("Earthquake", Place: region.Ref,
            Causes: surge > 0 ? new[] { new Cause(surge, CauseKind.Trigger, 0.8) } : null,
            Importance: 40, Tags: new[] { "Regions" }));
    }

    private static void Monsters(SimContext ctx, Region region)
    {
        var target = Neighbours(ctx, region, LinkKind.Land).OrderBy(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (target is null) return;
        region.Danger = Math.Max(0, region.Danger - 15);
        target.Danger = Math.Min(100, target.Danger + 20);
        ctx.Emit(new EventDraft("MonstersMigrated",
            Actors: new Dictionary<string, EntityRef> { ["from"] = region.Ref },
            Place: target.Ref,
            Causes: Conditions.Cite(region.Conditions, (Conditions.Dangerous, CauseKind.Trigger, 0.8)),
            Importance: 35, Tags: new[] { "Regions" }));
    }

    private static void Discovery(SimContext ctx, Region region)
    {
        string kind;
        double capacity;
        if (region.LeyPower >= 60 && ctx.Rng.Chance(0.3)) (kind, capacity) = (ResourceKinds.ArcaneCrystal, region.LeyPower / 10);
        else if (region.MountainShare >= 0.1 && ctx.Rng.Chance(0.2)) (kind, capacity) = (ResourceKinds.Mithril, 3);
        else
        {
            var common = region.Resources.Keys.Where(k => !ResourceKinds.IsRare(k) && k != ResourceKinds.Grain).ToList();
            kind = common.Count > 0 ? common[ctx.Rng.NextInt(0, common.Count)] : ResourceKinds.Stone;
            capacity = region.Resources.TryGetValue(kind, out var existing) ? existing.Capacity * 0.5 : 5;
        }
        var deposit = region.Resources.TryGetValue(kind, out var d) ? d : region.Resources[kind] = new ResourceDeposit();
        deposit.Capacity = Math.Round(deposit.Capacity + capacity, 1);
        deposit.Yield = Math.Round(deposit.Yield + capacity, 1);
        ctx.Emit(new EventDraft("ResourceDiscovered", Place: region.Ref,
            Payload: new Dictionary<string, string> { ["resource"] = kind },
            Importance: ResourceKinds.IsRare(kind) ? 45 : 20, Tags: new[] { "Regions", "Economy" }));
    }

    private void SpreadPlague(SimContext ctx, IReadOnlyList<Region> plagued)
    {
        foreach (var source in plagued)
        {
            if (!source.Conditions.TryGetValue(Conditions.Plague, out var outbreak)) continue;
            foreach (var link in ctx.World.Geography?.Graph.LinksOf(source.Id) ?? Array.Empty<RegionLink>())
            {
                var target = ctx.World.Regions[link.Other(source.Id)];
                var odds = link.Kind == LinkKind.Sea ? _odds.PlagueSpreadSea : _odds.PlagueSpreadLand;
                if (target.Conditions.ContainsKey(Conditions.Plague) || target.Conditions.ContainsKey(Conditions.Immune)
                    || !ctx.Rng.Chance(odds)) continue;
                Begin(ctx, target, Conditions.Plague, "PlagueSpread", 45, ctx.Rng.NextInt(90, 181), "PlagueEnded",
                    new[] { new Cause(outbreak, CauseKind.Trigger, 1.0) },
                    new Dictionary<string, EntityRef> { ["from"] = source.Ref });
            }
        }
    }

    /// <summary>Appends recent important events that happened in a region to its memory.</summary>
    private static void Remember(SimContext ctx)
    {
        var cursor = ctx.World.Markers.GetValueOrDefault(MemoryCursor, 0);
        var all = ctx.Log.All;
        // Ids increase along the log, so binary-search the first unscanned event instead of walking history.
        int lo = 0, hi = all.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (all[mid].Id <= cursor) lo = mid + 1; else hi = mid;
        }
        for (var i = lo; i < all.Count; i++)
        {
            var ev = all[i];
            if (ev.Importance < MemoryImportance || ev.Place is not { Kind: Region.Kind } place) continue;
            if (!ctx.World.Regions.TryGetValue(place.Id, out var region)) continue;
            region.Memory.Add(ev.Id);
            if (region.Memory.Count > MemorySize) region.Memory.RemoveAt(0);
        }
        if (ctx.Log.Count > 0) ctx.World.Markers[MemoryCursor] = ctx.Log.NextId - 1;
    }

    private static WorldEvent Begin(SimContext ctx, Region region, string condition, string type, int importance,
        long days, string endType = "", IReadOnlyList<Cause>? causes = null, IReadOnlyDictionary<string, EntityRef>? actors = null)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: actors, Place: region.Ref, Causes: causes,
            Importance: importance, Tags: new[] { "Regions" }));
        Hold(ctx, region, condition, ev.Id, days, endType);
        return ev;
    }

    /// <summary>Sets a condition caused by eventId and schedules its end (with an end event if endType is set).</summary>
    private static void Hold(SimContext ctx, Region region, string condition, long eventId, long days, string endType = "")
    {
        region.Conditions[condition] = eventId;
        ctx.Queue.Schedule(ctx.Date.AddDays(days), ConditionEndKind, new Dictionary<string, string>
        {
            ["region"] = region.Id.ToString(CultureInfo.InvariantCulture),
            ["condition"] = condition,
            ["event"] = eventId.ToString(CultureInfo.InvariantCulture),
            ["end"] = endType,
        });
    }

    private static IEnumerable<Region> Neighbours(SimContext ctx, Region region, LinkKind? kind = null) =>
        (ctx.World.Geography?.Graph.LinksOf(region.Id) ?? Array.Empty<RegionLink>())
        .Where(l => kind is null || l.Kind == kind)
        .Select(l => ctx.World.Regions[l.Other(region.Id)]);

    private static bool Roll(SimContext ctx, double perYear) => perYear > 0 && ctx.Rng.Chance(perYear / 12);
}

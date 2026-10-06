using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Faiths (sub-project 3 §4). Each year a region's people may turn to the faith pressing hardest on them: their
/// realm's faith and their neighbours' faiths, stronger where temples and shrines stand, weaker for ascetic or
/// tolerant faiths; their own temples hold them. A realm whose lands have mostly turned takes up the new faith.
/// A faith whose holy land falls to a realm of another faith grows zealous until it is regained.
/// </summary>
public sealed class FaithSystem : ISimSystem
{
    public const double ConvertPerYear = 0.04;
    public const int SettleYears = 40;
    public const double RealmShare = 2.0 / 3;
    public const double LossZeal = 30;

    public string Name => "faith";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        Spread(ctx);
        RealmsConvert(ctx);
        HolySites(ctx);
    }

    /// <summary>Temples count fully, shrines by half.</summary>
    public static double Devotion(World world, Region region) =>
        world.LivingSettlements(region).Sum(s => (s.Buildings.Contains("temple") ? 1 : 0) + (s.Buildings.Contains("shrine") ? 0.5 : 0));

    /// <summary>Each other faith's pull on the region.</summary>
    public static SortedDictionary<long, double> Pressure(World world, Region region)
    {
        var pull = new SortedDictionary<long, double>();
        void Add(long faith, double weight)
        {
            if (faith == 0 || faith == region.FaithId || !world.Faiths.TryGetValue(faith, out var f)) return;
            if (f.Has("ascetic") || f.Has("tolerant")) weight *= 0.6;
            pull[faith] = pull.GetValueOrDefault(faith) + weight * (1 + f.Zeal / 100);
        }
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner)) Add(owner.FaithId, 3);
        foreach (var n in world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>())
        {
            var neighbour = world.Regions[n];
            Add(neighbour.FaithId, 1 + Devotion(world, neighbour) / 2);
        }
        return pull;
    }

    /// <summary>The faith pressing hardest on a peopled region and the yearly chance it wins them (0 if none).</summary>
    public static (long Faith, double Odds) Odds(World world, Region region)
    {
        if (region.FaithId == 0 || !world.LivingSettlements(region).Any()) return (0, 0);
        var (faith, pull) = Pressure(world, region).OrderByDescending(p => p.Value).ThenBy(p => p.Key).FirstOrDefault();
        return faith == 0 ? (0, 0) : (faith, ConvertPerYear * pull / (pull + 3 + Devotion(world, region)));
    }

    private static void Spread(SimContext ctx)
    {
        var world = ctx.World;
        var turned = new List<(Region Region, long Faith)>();
        foreach (var region in world.Regions.Values)
        {
            if (Settling(ctx, region)) continue;
            var (faith, odds) = Odds(world, region);
            if (faith != 0 && ctx.Rng.Chance(odds)) turned.Add((region, faith));
        }
        // Decided on last year's map, applied together, so the order of regions does not matter.
        foreach (var (region, faith) in turned) Convert(ctx, region, faith);
    }

    /// <summary>A people that turned within a generation keep their new faith a while.</summary>
    private static bool Settling(SimContext ctx, Region region) =>
        ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type == "RegionConverted") is { } turned
        && ctx.Date.Day - turned.Date.Day < SettleYears * (long)Core.WorldDate.DaysPerYear;

    private static void Convert(SimContext ctx, Region region, long faithId)
    {
        var world = ctx.World;
        var old = world.Faiths[region.FaithId];
        var faith = world.Faiths[faithId];
        var causes = new List<Cause>();
        var neighbours = world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>();
        var spread = neighbours.SelectMany(n => ctx.Log.Involving(world.Regions[n].Ref))
            .Where(e => e.Type == "RegionConverted" && e.Actors.GetValueOrDefault("faith") == faith.Ref)
            .OrderBy(e => e.Id).LastOrDefault();
        if (spread is not null) causes.Add(new Cause(spread.Id, CauseKind.Enabler, 0.6));
        if (world.Realms.TryGetValue(region.OwnerRealmId, out var owner) && owner.FaithId == faithId)
        {
            var taken = ctx.Log.Involving(region.Ref).Where(e => e.Type == "RegionCeded").OrderBy(e => e.Id).LastOrDefault();
            causes.Add(new Cause(taken?.Id ?? owner.FoundedEventId, CauseKind.Enabler, 0.5));
        }
        region.FaithId = faithId;
        ctx.Emit(new EventDraft("RegionConverted", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["old"] = old.Ref },
            Place: region.Ref, Causes: causes.Where(c => c.EventId != 0).ToArray(), Importance: 25, Tags: new[] { "Faith" }));
    }

    private static void RealmsConvert(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            var domain = Realms.Domain(world, realm).ToList();
            if (domain.Count < 2) continue;
            var (faith, count) = domain.GroupBy(r => r.FaithId).Select(g => (g.Key, g.Count()))
                .OrderByDescending(g => g.Item2).ThenBy(g => g.Key).First();
            if (faith == realm.FaithId || faith == 0 || count < domain.Count * RealmShare || !ctx.Rng.Chance(0.2)) continue;
            var last = domain.SelectMany(r => ctx.Log.Involving(r.Ref))
                .Where(e => e.Type == "RegionConverted" && e.Actors["faith"].Id == faith).OrderBy(e => e.Id).LastOrDefault();
            realm.FaithId = faith;
            realm.Stability = Math.Max(0, realm.Stability - 10);
            if (Realms.RulerOf(world, realm) is { } ruler) ruler.FaithId = faith;
            ctx.Emit(new EventDraft("RealmConverted", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["faith"] = world.Faiths[faith].Ref },
                Place: world.Settlements[realm.CapitalId].Ref, Causes: last is null ? null : new[] { new Cause(last.Id, CauseKind.Trigger, 0.8) },
                Importance: 50, Tags: new[] { "Faith", "Politics" }));
        }
    }

    private static void HolySites(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var faith in world.Faiths.Values)
        {
            var calm = faith.Has("zealous") ? 40 : 10;
            faith.Zeal = Math.Max(Math.Min(faith.Zeal, calm), faith.Zeal - 3);
            if (!world.Regions.TryGetValue(faith.HolyRegionId, out var holy)) continue;
            var holder = Realms.SovereignOf(world, holy);
            var lost = holder is not null && holder.FaithId != faith.Id;
            if (lost && faith.HolySiteLostEventId == 0)
            {
                // Lost only by its cession to the holder or the holder's change of faith: a holy land never held is not a loss.
                var taken = ctx.Log.Involving(holy.Ref)
                    .Where(e => e.Type == "RegionCeded" && e.Actors.TryGetValue("to", out var to) && world.Realms.TryGetValue(to.Id, out var gainer)
                                && Realms.Top(world, gainer).Id == holder!.Id)
                    .Concat(ctx.Log.Involving(holder!.Ref).Where(e => e.Type == "RealmConverted"))
                    .OrderBy(e => e.Id).LastOrDefault();
                if (taken is null) continue;
                faith.Zeal = Math.Min(100, faith.Zeal + LossZeal);
                faith.HolySiteLostEventId = ctx.Emit(new EventDraft("HolySiteLost",
                    Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref, ["realm"] = holder.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(taken.Id, CauseKind.Trigger, 0.8) }, Importance: 45, Tags: new[] { "Faith" })).Id;
            }
            else if (holder?.FaithId == faith.Id && faith.HolySiteLostEventId != 0)
            {
                var loss = faith.HolySiteLostEventId;
                faith.HolySiteLostEventId = 0;
                faith.Zeal = Math.Min(100, faith.Zeal + 10);
                ctx.Emit(new EventDraft("HolySiteRegained", Actors: new Dictionary<string, EntityRef> { ["faith"] = faith.Ref }, Place: holy.Ref,
                    Causes: new[] { new Cause(loss, CauseKind.Enabler, 0.6) }, Importance: 45, Tags: new[] { "Faith" }));
            }
        }
    }
}

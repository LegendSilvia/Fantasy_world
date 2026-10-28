using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Trade routes (sub-project 3 §8). Towns and cities are markets; each year a few pairs within a journey's reach open
/// a road along the region graph when their realms are at peace with each other and the way is safe. A road carries
/// wealth both ways each year in proportion to the smaller market. Each month, a road that a war cuts (an occupied
/// region on the way, or its markets' realms at war with each other) or that grows too dangerous is severed, and it
/// reopens once the way is clear again; a road whose market is abandoned or shrinks below a town closes for good.
/// </summary>
public sealed class TradeSystem : ISimSystem
{
    public const double MaxTravelDays = 40;
    public const double OpenChance = 0.12;
    public const int MaxNewPerYear = 3;
    public const int MaxPerMarket = 3;
    public const double DangerLimit = 60;
    public const double FlowShare = 0.03;

    public string Name => "trade";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var route in world.Routes.Values.Where(r => r.State != RouteState.Closed).ToList())
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            if (CloseIfDead(ctx, route)) continue;
            var blocked = Blocked(ctx, route);
            if (route.State == RouteState.Open && blocked is { } cut)
                Sever(ctx, route, cut.Reason, cut.Place ?? from.Ref, cut.Cause);
            else if (route.State == RouteState.Severed && blocked is null && !RaidedLately(ctx, route))
            {
                route.State = RouteState.Open;
                // Recorded at a weight compaction keeps, so past maps see the road reopen.
                ctx.Emit(new EventDraft("RouteRestored", Actors: Ends(route, from, to),
                    Place: from.Ref, Causes: new[] { new Cause(route.SeveredEventId, CauseKind.Enabler, 0.5) },
                    Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
            }
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        // Towns change size in the settlements' yearly tick: a road to one no longer a market closes the same day.
        foreach (var route in world.Routes.Values.Where(r => r.State != RouteState.Closed).ToList()) CloseIfDead(ctx, route);
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open))
        {
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            route.Volume = FlowShare * Math.Min(from.Wealth, to.Wealth);
            from.Wealth += route.Volume;
            to.Wealth += route.Volume;
        }
        Open(ctx);
    }

    /// <summary>A road a beast's raid cut stays shunned this long.</summary>
    public const int RaidDays = 180;

    private static bool RaidedLately(SimContext ctx, TradeRoute route) =>
        ctx.Log.TryGet(route.SeveredEventId, out var cut) && cut.Payload.GetValueOrDefault("reason") == "raid" && ctx.Date.Day - cut.Date.Day < RaidDays;

    /// <summary>Cuts an open road, citing what cut it.</summary>
    public static void Sever(SimContext ctx, TradeRoute route, string reason, EntityRef place, long? cause)
    {
        var world = ctx.World;
        route.State = RouteState.Severed;
        route.Volume = 0;
        route.SeveredEventId = ctx.Emit(new EventDraft("RouteSevered", Actors: Ends(route, world.Settlements[route.FromId], world.Settlements[route.ToId]),
            Place: place, Payload: new Dictionary<string, string> { ["reason"] = reason },
            Causes: cause is { } c ? new[] { new Cause(c, CauseKind.Trigger, 0.9) } : null,
            Importance: 30, Tags: new[] { "Economy" })).Id;
    }

    private static Dictionary<string, EntityRef> Ends(TradeRoute route, Settlement from, Settlement to) =>
        new() { ["route"] = route.Ref, ["from"] = from.Ref, ["to"] = to.Ref };

    /// <summary>A market abandoned, or shrunk below a town, ends the road for good. True if it closed.</summary>
    private static bool CloseIfDead(SimContext ctx, TradeRoute route)
    {
        var (from, to) = (ctx.World.Settlements[route.FromId], ctx.World.Settlements[route.ToId]);
        if (IsMarket(from) && IsMarket(to)) return false;
        route.State = RouteState.Closed;
        route.Volume = 0;
        var gone = !IsMarket(from) ? from : to;
        var why = ctx.Log.Involving(gone.Ref).LastOrDefault(e => e.Type is "SettlementAbandoned" or "SettlementDeclined");
        ctx.Emit(new EventDraft("RouteClosed", Actors: Ends(route, from, to), Place: gone.Ref,
            Causes: why is null ? null : new[] { new Cause(why.Id, CauseKind.Trigger, 0.9) }, Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
        return true;
    }

    public static bool IsMarket(Settlement s) => !s.Abandoned && s.Tier >= SettlementTier.Town;

    private static void Open(SimContext ctx)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        if (graph is null) return;
        var markets = world.Settlements.Values.Where(IsMarket).ToList();
        int Roads(long id) => world.Routes.Values.Count(r => r.State != RouteState.Closed && (r.FromId == id || r.ToId == id));
        var linked = world.Routes.Values.Where(r => r.State != RouteState.Closed).Select(r => (r.FromId, r.ToId)).ToHashSet();
        var opened = 0;
        for (var i = 0; i < markets.Count && opened < MaxNewPerYear; i++)
            for (var j = i + 1; j < markets.Count && opened < MaxNewPerYear; j++)
            {
                var (a, b) = (markets[i], markets[j]);
                if (a.RegionId == b.RegionId || linked.Contains((a.Id, b.Id)) || Roads(a.Id) >= MaxPerMarket || Roads(b.Id) >= MaxPerMarket) continue;
                if (graph.ShortestPath(a.RegionId, b.RegionId) is not { } way || way.Days > MaxTravelDays) continue;
                if (AtWar(world, a, b) || way.Path.Any(r => world.Regions[r].OccupierRealmId != 0 || world.Regions[r].Danger >= DangerLimit)) continue;
                if (!ctx.Rng.Chance(OpenChance)) continue;
                var id = world.Ids.Take(TradeRoute.Kind);
                var road = ctx.Emit(new EventDraft("RouteOpened",
                    Actors: new Dictionary<string, EntityRef> { ["route"] = new(TradeRoute.Kind, id), ["from"] = a.Ref, ["to"] = b.Ref },
                    Place: a.Ref, Importance: 25, Tags: new[] { "Economy" }));
                var route = new TradeRoute
                {
                    Id = id, Name = $"The {a.Name}–{b.Name} Road", FromId = a.Id, ToId = b.Id,
                    Regions = way.Path.ToList(), State = RouteState.Open, OpenedEventId = road.Id,
                };
                world.Routes[route.Id] = route;
                linked.Add((a.Id, b.Id));
                opened++;
            }
    }

    /// <summary>Why a road cannot be used now (and the event to blame), or null when the way is clear.</summary>
    private static (string Reason, EntityRef? Place, long? Cause)? Blocked(SimContext ctx, TradeRoute route)
    {
        var world = ctx.World;
        var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
        if (AtWar(world, from, to))
        {
            var war = world.Wars.Values.Where(w => !w.Ended).FirstOrDefault(w => Opposed(w, Realms.SovereignOf(world, from)?.Id, Realms.SovereignOf(world, to)?.Id));
            return ("war", null, war?.DeclaredEventId);
        }
        foreach (var id in route.Regions)
        {
            var region = world.Regions[id];
            if (region.OccupierRealmId != 0)
                return ("occupation", region.Ref, ctx.Log.Involving(region.Ref).LastOrDefault(e => e.Type is "RegionOccupied" or "SiegeEnded")?.Id);
            if (region.Danger >= DangerLimit)
                return ("danger", region.Ref, region.Conditions.TryGetValue(Conditions.Dangerous, out var danger) ? danger : null);
        }
        return null;
    }

    private static bool AtWar(World world, Settlement a, Settlement b)
    {
        var (x, y) = (Realms.SovereignOf(world, a)?.Id, Realms.SovereignOf(world, b)?.Id);
        return x is not null && y is not null && x != y && world.Wars.Values.Any(w => !w.Ended && Opposed(w, x, y));
    }

    private static bool Opposed(War w, long? a, long? b) =>
        a is { } x && b is { } y && (w.Attackers.Contains(x) && w.Defenders.Contains(y) || w.Attackers.Contains(y) && w.Defenders.Contains(x));
}

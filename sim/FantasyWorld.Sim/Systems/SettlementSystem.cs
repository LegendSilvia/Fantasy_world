using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The populace's mood and fortunes: unrest follows hunger, plague and danger; high unrest starts
/// revolts and drives people to calmer neighbours; settlements grow, decline, build and die out.
/// </summary>
public sealed class SettlementSystem : ISimSystem
{
    public const string RevoltEndKind = "settlement.revolt.end";
    public const double RevoltAt = 60;
    public const double EmigrateAt = 50;
    public const int AbandonBelow = 15;

    private static readonly (string Name, SettlementTier MinTier, double Cost)[] BuildOrder =
    {
        ("granary", SettlementTier.Village, 300),
        ("market", SettlementTier.Town, 1500),
        ("temple", SettlementTier.Town, 2500),
        ("guildhall", SettlementTier.City, 8000),
        ("mage tower", SettlementTier.City, 12_000),
    };

    /// <summary>Walls are a level, not a building: towns raise them to 2, cities to 3, and rebuild after quakes.</summary>
    public const double WallCost = 4000;

    private readonly double _revoltChance;

    public SettlementSystem(double revoltChancePerMonth = 0.35) => _revoltChance = revoltChancePerMonth;

    public string Name => "settlements";

    public IEnumerable<string> HandledKinds => new[] { RevoltEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                UpdateUnrest(ctx, region, s);
                if (s.Unrest >= RevoltAt && !s.Conditions.ContainsKey(Conditions.Revolt) && ctx.Rng.Chance(_revoltChance))
                    BeginRevolt(ctx, region, s);
                Build(ctx, region, s);
            }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
            foreach (var s in ctx.World.LivingSettlements(region).ToList())
            {
                Emigrate(ctx, region, s);
                ChangeTier(ctx, region, s);
                if (s.Population < AbandonBelow) Abandon(ctx, region, s);
            }
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var s = ctx.World.Settlements[long.Parse(action.Data["settlement"], CultureInfo.InvariantCulture)];
        var revolt = long.Parse(action.Data["event"], CultureInfo.InvariantCulture);
        if (!s.Conditions.TryGetValue(Conditions.Revolt, out var current) || current != revolt) return;
        s.Conditions.Remove(Conditions.Revolt);
        if (s.Abandoned) return;
        Demography.Kill(s, 0.02, ctx.Rng);
        s.Wealth *= 0.7;
        s.Unrest = 30;
        ctx.Emit(new EventDraft("RevoltEnded", Place: s.Ref,
            Causes: new[] { new Cause(revolt, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "Politics" }));
    }

    private static void UpdateUnrest(SimContext ctx, Region region, Settlement s)
    {
        var target = 10 + region.Danger * 0.2;
        if (region.Conditions.ContainsKey(Conditions.Shortage)) target += 50;
        if (region.Conditions.ContainsKey(Conditions.Plague)) target += 15;
        if (s.Walls == 0 && region.Danger > 60) target += 5;
        // A realm short of what it needs, unsettled or weary of war stirs its towns (sub-project 12): riots breed revolution.
        if (Realms.SovereignOf(ctx.World, region) is { } realm)
            target += Stocks.Shortages(realm).Count() * Stocks.UnrestPerShortage
                      + Math.Max(0, RevolutionSystem.UnsettledBelow + 10 - realm.Stability) * 0.6 + realm.WarWeariness * 0.1;
        s.Unrest = Math.Clamp(s.Unrest + (target - s.Unrest) * 0.25 + ctx.Rng.NextInt(-3, 4), 0, 100);
    }

    private static void BeginRevolt(SimContext ctx, Region region, Settlement s)
    {
        var revolt = ctx.Emit(new EventDraft("RevoltBegan",
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Place: s.Ref,
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.9),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.4)),
            Importance: s.Tier switch
            {
                SettlementTier.City => 60,
                SettlementTier.Town => 45,
                SettlementTier.Village => 30,
                _ => 20,
            },
            Tags: new[] { "Politics" }));
        s.Conditions[Conditions.Revolt] = revolt.Id;
        ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(60, 151)), RevoltEndKind, new Dictionary<string, string>
        {
            ["settlement"] = s.Id.ToString(CultureInfo.InvariantCulture),
            ["event"] = revolt.Id.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static void Build(SimContext ctx, Region region, Settlement s)
    {
        var maxWalls = s.Tier switch { SettlementTier.City => 3, SettlementTier.Town => 2, _ => 0 };
        if (s.Walls < maxWalls && s.Wealth >= WallCost)
        {
            s.Wealth -= WallCost;
            s.Walls++;
            ctx.Emit(new EventDraft("WallsRaised", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["level"] = s.Walls.ToString(CultureInfo.InvariantCulture) },
                Importance: 15, Tags: new[] { "Settlements" }));
            return;
        }
        foreach (var (name, minTier, cost) in BuildOrder)
        {
            if (s.Buildings.Contains(name) || s.Tier < minTier) continue;
            if (name == "mage tower" && (region.LeyPower < 50 || Realms.SovereignOf(ctx.World, region)?.MagicLaw == MagicLaw.Outlawed)) continue;
            if (s.Wealth < cost) return;
            s.Wealth -= cost;
            s.Buildings.Add(name);
            s.Buildings.Sort(StringComparer.Ordinal);
            ctx.Emit(new EventDraft("BuildingCompleted", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["building"] = name },
                Importance: name == "mage tower" ? 30 : 10, Tags: new[] { "Settlements" }));
            return;
        }
    }

    /// <summary>Unhappy or hungry settlements lose 4% of their families to the calmest settlement nearby.</summary>
    private static void Emigrate(SimContext ctx, Region region, Settlement s)
    {
        var hungry = region.Conditions.ContainsKey(Conditions.Shortage);
        if (s.Unrest < EmigrateAt && !hungry) return;

        var nearby = new[] { region.Id }.Concat(ctx.World.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>());
        var destination = nearby
            .Select(id => ctx.World.Regions[id])
            .Where(r => !r.Conditions.ContainsKey(Conditions.Shortage))
            .SelectMany(r => ctx.World.LivingSettlements(r))
            .Where(d => d.Id != s.Id && d.Unrest < s.Unrest - 10)
            .OrderBy(d => d.Unrest).ThenByDescending(d => d.Tier).ThenBy(d => d.Id)
            .FirstOrDefault();
        if (destination is null) return;

        var children = Demography.RoundRandom(s.Children * 0.04, ctx.Rng);
        var adults = Demography.RoundRandom(s.Adults * 0.04, ctx.Rng);
        s.Children -= children;
        s.Adults -= adults;
        destination.Children += children;
        destination.Adults += adults;
        if (children + adults < 100) return;

        ctx.Emit(new EventDraft("Exodus",
            Actors: new Dictionary<string, EntityRef> { ["to"] = destination.Ref },
            Place: s.Ref,
            Payload: new Dictionary<string, string> { ["people"] = (children + adults).ToString(CultureInfo.InvariantCulture) },
            Causes: Conditions.Cite(region.Conditions, (Conditions.Shortage, CauseKind.Trigger, 0.8))
                .Concat(Conditions.Cite(s.Conditions, (Conditions.Revolt, CauseKind.Enabler, 0.6))).ToList(),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    /// <summary>Grow at the next tier's threshold; decline below 75% of the current tier's.</summary>
    private static void ChangeTier(SimContext ctx, Region region, Settlement s)
    {
        var grown = Demography.TierFor(s.Population);
        if (grown > s.Tier)
        {
            s.Tier = grown;
            ctx.Emit(new EventDraft("SettlementGrew", Place: s.Ref,
                Payload: new Dictionary<string, string> { ["tier"] = grown.ToString() },
                Importance: grown switch { SettlementTier.City => 50, SettlementTier.Town => 30, _ => 15 },
                Tags: new[] { "Settlements" }));
            return;
        }
        if (s.Tier == SettlementTier.Hamlet || s.Population >= Threshold(s.Tier) * 0.75) return;
        s.Tier -= 1;
        ctx.Emit(new EventDraft("SettlementDeclined", Place: s.Ref,
            Payload: new Dictionary<string, string> { ["tier"] = s.Tier.ToString() },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Enabler, 0.7), (Conditions.Plague, CauseKind.Enabler, 0.6)),
            Importance: 25, Tags: new[] { "Settlements" }));
    }

    private static void Abandon(SimContext ctx, Region region, Settlement s)
    {
        s.Abandoned = true;
        s.Children = s.Adults = s.Elders = 0;
        ctx.Emit(new EventDraft("SettlementAbandoned", Place: s.Ref,
            Actors: new Dictionary<string, EntityRef> { ["region"] = region.Ref },
            Causes: Conditions.Cite(region.Conditions,
                (Conditions.Shortage, CauseKind.Trigger, 0.8),
                (Conditions.Plague, CauseKind.Enabler, 0.6),
                (Conditions.Dangerous, CauseKind.Enabler, 0.5)),
            Importance: 30, Tags: new[] { "Settlements" }));
    }

    private static int Threshold(SettlementTier tier) => tier switch
    {
        SettlementTier.City => 6000,
        SettlementTier.Town => 1500,
        SettlementTier.Village => 300,
        _ => 0,
    };
}

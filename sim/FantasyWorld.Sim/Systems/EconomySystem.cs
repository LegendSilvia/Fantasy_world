using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Food, labour, wealth and population. Each region pools the food of its settlements: farmers
/// (capped by farmland) produce by season and fertility, coasts and rivers add fish, everyone eats.
/// Stores below a month of need begin a shortage, which cites whatever conditions caused it.
/// </summary>
public sealed class EconomySystem : ISimSystem
{
    public const double FoodPerFarmerMonth = 3.2;
    public const double FishFoodPerYield = 4;
    public const double ShortageBelowMonths = 1;
    public const double ShortageEndsAtMonths = 3;
    public const double MaxStoreMonths = 12;
    public const double CraftWealthPerWorker = 0.02;

    /// <summary>Harvest weight per season: Spring, Summer, Autumn, Winter.</summary>
    private static readonly double[] SeasonWeight = { 0.5, 1.0, 2.0, 0.0 };

    private static readonly (string Condition, double Factor)[] ConditionYield =
    {
        (Conditions.Drought, 0.4),
        (Conditions.Blight, 0.6),
        (Conditions.Plague, 0.8),
    };

    private static readonly IReadOnlyDictionary<string, double> ResourceValue = new Dictionary<string, double>
    {
        [ResourceKinds.Timber] = 0.05,
        [ResourceKinds.Ore] = 0.1,
        [ResourceKinds.Stone] = 0.05,
        [ResourceKinds.Horses] = 0.1,
        [ResourceKinds.Mithril] = 2,
        [ResourceKinds.Dragonbone] = 3,
        [ResourceKinds.ArcaneCrystal] = 2,
    };

    public string Name => "economy";

    public void OnMonthlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
        {
            var settlements = ctx.World.LivingSettlements(region).ToList();
            if (settlements.Count == 0) continue;
            var need = settlements.Sum(Demography.Consumption);
            region.Food = Math.Max(0, region.Food + Production(ctx, region, settlements) - need);
            if (need > 0)
            {
                if (region.Food == 0) Starve(ctx, region, settlements, need);
                region.Food = Math.Min(region.Food, need * MaxStoreMonths);
            }
            UpdateShortage(ctx, region, settlements, need);
            Trade(region, settlements);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var region in ctx.World.Regions.Values)
        {
            var hungry = region.Conditions.ContainsKey(Conditions.Shortage);
            foreach (var s in ctx.World.LivingSettlements(region)) AgeOneYear(s, hungry, ctx.Rng);
        }
    }

    /// <summary>Food made this month, before anyone eats.</summary>
    public static double Production(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements)
    {
        var farmers = Math.Min(settlements.Sum(Demography.Farmers), Demography.FarmCapacity(region));
        var yield = region.Fertility / 50 * SeasonWeight[(int)ctx.Date.Season];
        foreach (var (condition, factor) in ConditionYield)
            if (region.Conditions.ContainsKey(condition)) yield *= factor;
        var fish = region.Resources.TryGetValue(ResourceKinds.Fish, out var deposit) ? deposit.Yield * FishFoodPerYield : 0;
        return farmers * FoodPerFarmerMonth * yield + fish;
    }

    /// <summary>Empty stores: up to 5% of people die per month, in proportion to how much food was missing.</summary>
    private static void Starve(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements, double need)
    {
        var produced = Production(ctx, region, settlements);
        var shortfall = Math.Clamp(1 - produced / need, 0, 1);
        foreach (var s in settlements) Demography.Kill(s, 0.05 * shortfall, ctx.Rng);
    }

    private static void UpdateShortage(SimContext ctx, Region region, IReadOnlyList<Settlement> settlements, double need)
    {
        var months = need > 0 ? region.Food / need : double.PositiveInfinity;
        var active = region.Conditions.TryGetValue(Conditions.Shortage, out var shortageId);
        if (!active && months < ShortageBelowMonths)
        {
            var hasCity = settlements.Any(s => s.Tier == SettlementTier.City);
            var shortage = ctx.Emit(new EventDraft("FoodShortageBegan",
                Place: region.Ref,
                Payload: new Dictionary<string, string> { ["months"] = months.ToString("0.0", CultureInfo.InvariantCulture) },
                Causes: Conditions.Cite(region.Conditions,
                    (Conditions.Drought, CauseKind.Trigger, 0.9),
                    (Conditions.Blight, CauseKind.Trigger, 0.8),
                    (Conditions.Flood, CauseKind.Trigger, 0.7),
                    (Conditions.Plague, CauseKind.Enabler, 0.6),
                    (Conditions.Exhausted, CauseKind.Enabler, 0.5)),
                Importance: hasCity ? 45 : 30,
                Tags: new[] { "Economy" }));
            region.Conditions[Conditions.Shortage] = shortage.Id;
        }
        else if (active && months >= ShortageEndsAtMonths)
        {
            ctx.Emit(new EventDraft("FoodShortageEnded",
                Place: region.Ref,
                Causes: new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) },
                Importance: 15,
                Tags: new[] { "Economy" }));
            region.Conditions.Remove(Conditions.Shortage);
        }
    }

    /// <summary>Crafters and the region's non-food resources earn wealth; extraction wears deposits down.</summary>
    private static void Trade(Region region, IReadOnlyList<Settlement> settlements)
    {
        var population = settlements.Sum(s => s.Population);
        var resourceIncome = 0.0;
        foreach (var (kind, deposit) in region.Resources)
        {
            if (!ResourceValue.TryGetValue(kind, out var value)) continue;
            resourceIncome += deposit.Yield * value;
            var extraction = deposit.Yield * 0.01 * Math.Min(1, population / 5000.0);
            deposit.Yield = Math.Clamp(deposit.Yield - extraction + (deposit.Capacity - deposit.Yield) * 0.02, 0, deposit.Capacity);
        }
        foreach (var s in settlements)
        {
            var crafters = s.Adults - Demography.Farmers(s);
            var share = population > 0 ? (double)s.Population / population : 0;
            s.Wealth += crafters * CraftWealthPerWorker * (1 + (int)s.Tier * 0.5) + resourceIncome * share;
        }
    }

    private static void AgeOneYear(Settlement s, bool hungry, Rng rng)
    {
        var mortality = hungry ? 1.5 : 1.0;
        var births = Demography.RoundRandom(s.Adults * 0.05 * (hungry ? 0.5 : 1.0), rng);
        // Each person leaves a band at most once: clamp so no one is created from nothing.
        var childDeaths = Math.Min(s.Children, Demography.RoundRandom(s.Children * 0.012 * mortality, rng));
        var grownUp = Math.Min(s.Children - childDeaths, Demography.RoundRandom(s.Children / 14.0, rng));
        var adultDeaths = Math.Min(s.Adults, Demography.RoundRandom(s.Adults * 0.01 * mortality, rng));
        var aged = Math.Min(s.Adults - adultDeaths, Demography.RoundRandom(s.Adults / 35.0, rng));
        var elderDeaths = Math.Min(s.Elders, Demography.RoundRandom(s.Elders * 0.09 * mortality, rng));
        s.Children += births - grownUp - childDeaths;
        s.Adults += grownUp - aged - adultDeaths;
        s.Elders += aged - elderDeaths;
    }
}

using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>Shared population arithmetic used by world generation and the economy.</summary>
public static class Demography
{
    public const double ChildFood = 0.6;
    public const double ElderFood = 0.8;

    /// <summary>Food eaten per month, in person-months.</summary>
    public static double Consumption(Settlement s) => s.Children * ChildFood + s.Adults + s.Elders * ElderFood;

    /// <summary>Share of adults who farm; the rest are crafters, traders and soldiers.</summary>
    public static double FarmShare(SettlementTier tier) => tier switch
    {
        SettlementTier.Hamlet => 0.9,
        SettlementTier.Village => 0.85,
        SettlementTier.Town => 0.65,
        _ => 0.45,
    };

    public static double Farmers(Settlement s) => s.Adults * FarmShare(s.Tier);

    /// <summary>How many farmers one tile of farmland can usefully employ.</summary>
    public const double FarmersPerFarmland = 12;

    /// <summary>Farmers beyond this add nothing: the land is fully worked.</summary>
    public static double FarmCapacity(Region region) => region.Farmland * FarmersPerFarmland;

    /// <summary>The tier a settlement of this size grows into (no hysteresis).</summary>
    public static SettlementTier TierFor(int population) => population switch
    {
        >= 6000 => SettlementTier.City,
        >= 1500 => SettlementTier.Town,
        >= 300 => SettlementTier.Village,
        _ => SettlementTier.Hamlet,
    };

    /// <summary>Rounds up with probability equal to the fraction, so small settlements still change.</summary>
    public static int RoundRandom(double value, Rng rng)
    {
        var floor = Math.Floor(value);
        return (int)floor + (rng.NextDouble() < value - floor ? 1 : 0);
    }

    /// <summary>Removes a share of each age band (children and elders are hit harder). Returns the dead.</summary>
    public static int Kill(Settlement s, double fraction, Rng rng)
    {
        if (fraction <= 0) return 0;
        var children = Math.Min(s.Children, RoundRandom(s.Children * Math.Min(1, fraction * 1.5), rng));
        var adults = Math.Min(s.Adults, RoundRandom(s.Adults * Math.Min(1, fraction), rng));
        var elders = Math.Min(s.Elders, RoundRandom(s.Elders * Math.Min(1, fraction * 1.5), rng));
        s.Children -= children;
        s.Adults -= adults;
        s.Elders -= elders;
        return children + adults + elders;
    }
}

using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Realms' stocks (sub-project 12 R1, the owner's decisions of 2026-10-28). Each month a realm's domain yields what its
/// regions' deposits give (a year's yield is the deposit's yield); the stock keeps at most <see cref="YearsKept"/> years of
/// it. Hosts in the field use ore and horses, raising a host costs ore, and a rising work uses timber and stone. Wanting
/// what the stock cannot give begins a shortage (an event, a condition on the realm) that ends when the stock recovers.
/// Stocks arm the hosts (<see cref="Arms"/>, <see cref="Mounted"/>), hold up building when short, and weigh on
/// contentment: plenty steadies the realm, shortages unsettle it and stir its towns (<see cref="Contentment"/>).
/// </summary>
public static class Stocks
{
    public static readonly IReadOnlyList<string> Kinds =
    [
        ResourceKinds.Ore, ResourceKinds.Horses, ResourceKinds.Timber, ResourceKinds.Stone,
        ResourceKinds.Mithril, ResourceKinds.Dragonbone, ResourceKinds.ArcaneCrystal,
    ];

    public const double YearsKept = 2;

    /// <summary>Ore a host uses each month per soldier, horses per knight and per soldier, and ore per soldier raised.</summary>
    public const double OrePerSoldier = 1 / 50.0, HorsesPerKnight = 0.5, HorsesPerSoldier = 1 / 250.0, OreToRaise = 1 / 20.0;

    /// <summary>Timber and stone a rising work uses each year.</summary>
    public const double BuildingCost = 50;

    private const string ShortPrefix = "short:";

    /// <summary>What the realm's domain yields of a kind in a year.</summary>
    public static double Output(World world, Realm realm, string kind) =>
        Realms.Domain(world, realm).Sum(r => r.Resources.TryGetValue(kind, out var d) ? d.Yield : 0);

    public static double Stock(Realm realm, string kind) => realm.Stocks.GetValueOrDefault(kind);

    public static bool Short(Realm realm, string kind) => realm.Conditions.ContainsKey(ShortPrefix + kind);

    public static IEnumerable<string> Shortages(Realm realm) =>
        realm.Conditions.Keys.Where(k => k.StartsWith(ShortPrefix, StringComparison.Ordinal)).Select(k => k[ShortPrefix.Length..]);

    /// <summary>The month's yield into the stocks, then the end of any shortage the stock has recovered from.</summary>
    public static void Fill(SimContext ctx, Realm realm)
    {
        var inField = ctx.World.Armies.Values.Any(a => !a.Disbanded && a.RealmId == realm.Id);
        foreach (var kind in Kinds)
        {
            var output = Output(ctx.World, realm, kind);
            if (output <= 0 && Stock(realm, kind) <= 0) continue;
            var (stock, cap) = (Stock(realm, kind), output * YearsKept);
            realm.Stocks[kind] = stock >= cap ? stock : Math.Round(Math.Min(cap, stock + output / 12), 3);
            // Over: a few months' yield in hand; or the want is gone (no host in the field wants arms; a treasury that can
            // buy two years' building wants no stone of its own).
            var over = realm.Stocks[kind] >= Math.Max(1, output / 4)
                       || kind is ResourceKinds.Ore or ResourceKinds.Horses && !inField
                       || kind is ResourceKinds.Timber or ResourceKinds.Stone && realm.Treasury >= BuildingCost * BuyingPrice * 2;
            if (Short(realm, kind) && over) EndShortage(ctx, realm, kind);
        }
    }

    /// <summary>Spends what is wanted; what the stock cannot give begins a shortage (or keeps one). True if all was there.</summary>
    public static bool Use(SimContext ctx, Realm realm, string kind, double wanted, long cause = 0)
    {
        if (wanted <= 0) return true;
        var stock = Stock(realm, kind);
        realm.Stocks[kind] = Math.Round(Math.Max(0, stock - wanted), 3);
        if (stock >= wanted) return true;
        if (!Short(realm, kind))
        {
            var place = ctx.World.Settlements[realm.CapitalId].Ref;
            realm.Conditions[ShortPrefix + kind] = ctx.Emit(new EventDraft("ResourceShortageBegan",
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: place,
                Payload: new Dictionary<string, string> { ["resource"] = kind },
                Causes: cause != 0 ? new[] { new Cause(cause, CauseKind.Enabler, 0.6) } : null,
                Importance: realm.LiegeId == 0 ? 30 : 15, Tags: new[] { "Economy", "Politics" })).Id;
        }
        return false;
    }

    /// <summary>Gold a unit of timber or stone costs when the realm's own stock cannot give it.</summary>
    public const double BuyingPrice = 2;

    /// <summary>
    /// Spends what is wanted from the stock and buys the rest from the treasury; only what the treasury cannot pay for
    /// begins a shortage. For building: a realm whose land yields no stone still raises walls, at a price.
    /// </summary>
    public static bool Supply(SimContext ctx, Realm realm, string kind, double wanted)
    {
        var missing = Math.Max(0, wanted - Stock(realm, kind));
        var bought = Math.Min(missing, Math.Max(0, realm.Treasury) / BuyingPrice);
        realm.Treasury -= bought * BuyingPrice;
        var supplied = Use(ctx, realm, kind, wanted - bought);
        if (supplied && Short(realm, kind)) EndShortage(ctx, realm, kind);
        return supplied;
    }

    /// <summary>Whether the realm can find a year's building of a kind, from its stock or its treasury.</summary>
    public static bool CanBuild(Realm realm, string kind) =>
        Stock(realm, kind) + Math.Max(0, realm.Treasury) / BuyingPrice >= BuildingCost;

    private static void EndShortage(SimContext ctx, Realm realm, string kind)
    {
        var began = realm.Conditions[ShortPrefix + kind];
        realm.Conditions.Remove(ShortPrefix + kind);
        ctx.Emit(new EventDraft("ResourceShortageEnded",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["resource"] = kind },
            Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: Compactor.MinorBelow, Tags: new[] { "Economy" }));
    }

    /// <summary>A host's arms by its realm's stocks: short of ore, three quarters of its strength; with horses in hand, a tenth more.</summary>
    public static double Arms(Realm realm) => (Short(realm, ResourceKinds.Ore) ? 0.75 : 1) * (Mounted(realm) ? 1.1 : 1);

    /// <summary>Horses in hand: the realm's hosts ride (they march faster and strike harder).</summary>
    public static bool Mounted(Realm realm) => Stock(realm, ResourceKinds.Horses) >= 1 && !Short(realm, ResourceKinds.Horses);

    /// <summary>
    /// The stocks' weight on a realm's stability target: each kind the realm yields and has a year of in hand steadies it
    /// (at most three), each shortage unsettles it.
    /// </summary>
    public static double Contentment(World world, Realm realm)
    {
        var plenty = Kinds.Count(k => Output(world, realm, k) is var output and > 0 && Stock(realm, k) >= output);
        return Math.Min(3, plenty) * 2 - Shortages(realm).Count() * 6;
    }

    /// <summary>How much a shortage stirs the realm's towns: unrest added to each one's target.</summary>
    public const double UnrestPerShortage = 8;
}

using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The starting political map (spec: 6–8 starting realms). Every city's lord (in small worlds without
/// two cities, the largest towns') rules a kingdom whose
/// territory is the regions nearest its capital by travel days; every town's lord outside a capital
/// region rules a vassal county of the kingdom around it. Government, succession and magic law follow
/// the realm's faith and land.
/// </summary>
public static class RealmGenerator
{
    public static readonly string[] Palette =
        { "crimson", "azure", "gold", "silver", "sable", "vert", "purpure", "orange", "white", "russet" };

    public static void Generate(World world, Rng rng)
    {
        var graph = world.Geography?.Graph ?? throw new InvalidOperationException("Realms need a region graph.");
        // Kingdom seats: every city; small worlds without two cities crown their largest towns instead.
        var seats = world.Settlements.Values.Where(s => s.LordId != 0 && !s.Abandoned && s.Tier >= SettlementTier.Town)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id)
            .DistinctBy(s => s.RegionId).ToList(); // one seat per region: the largest
        var cities = seats.Where(s => s.Tier == SettlementTier.City).ToList();
        if (cities.Count < 2) cities = seats.Take(2).ToList();
        cities = cities.OrderBy(s => s.Id).ToList();
        if (cities.Count == 0) return;

        var kingdoms = cities.Select(city => Found(world, rng, city, liegeId: 0)).ToList();
        var nearest = NearestSource(graph, kingdoms.Select(k => world.Settlements[k.CapitalId].RegionId).ToList());
        foreach (var region in world.Regions.Values)
            if (nearest.TryGetValue(region.Id, out var capitalRegion))
                region.OwnerRealmId = kingdoms.Single(k => world.Settlements[k.CapitalId].RegionId == capitalRegion).Id;

        var claimed = new HashSet<long>(kingdoms.Select(k => world.Settlements[k.CapitalId].RegionId));
        foreach (var town in world.Settlements.Values.Where(s => s.Tier == SettlementTier.Town && s.LordId != 0 && !s.Abandoned).OrderBy(s => s.Id))
        {
            var region = world.Regions[town.RegionId];
            if (!claimed.Add(region.Id) || region.OwnerRealmId == 0) continue;
            var county = Found(world, rng, town, liegeId: region.OwnerRealmId);
            region.OwnerRealmId = county.Id;
        }

        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId != 0))
            world.FoundingOwners[region.Id] = region.OwnerRealmId;
        foreach (var realm in world.Realms.Values) world.FoundingLieges[realm.Id] = realm.LiegeId;
        foreach (var realm in world.Realms.Values)
        {
            realm.Rank = Realms.RankFor(world, realm);
            realm.RegionsAtLastReview = Realms.Domain(world, realm).Count();
            realm.Treasury = Math.Round(Realms.Domain(world, realm).SelectMany(world.LivingSettlements).Sum(s => s.Wealth) * 0.2);
        }
    }

    private static Realm Found(World world, Rng rng, Settlement capital, long liegeId)
    {
        var region = world.Regions[capital.RegionId];
        var faith = world.Faiths.GetValueOrDefault(region.FaithId);
        var tenets = faith?.Tenets ?? new List<string>();
        var government = tenets.Contains("zealous") && rng.Chance(0.5) ? Government.Theocracy
            : region.LeyPower >= 75 && rng.Chance(0.5) ? Government.Magocracy
            : tenets.Contains("mercantile") && region.Coastal && rng.Chance(0.6) ? Government.MerchantRepublic
            : region.Climate == ClimateBand.Cold && rng.Chance(0.4) ? Government.Tribal
            : rng.Chance(0.2) ? Government.ElectiveKingdom
            : Government.FeudalMonarchy;
        var colors = new List<string>();
        while (colors.Count < 2)
        {
            var color = Palette[rng.NextInt(0, Palette.Length)];
            if (!colors.Contains(color)) colors.Add(color);
        }
        var realm = new Realm
        {
            Id = world.Ids.Take(Realm.Kind),
            Name = region.Name,
            CapitalId = capital.Id,
            LiegeId = liegeId,
            Government = government,
            SuccessionLaw = SuccessionFor(government),
            MagicLaw = tenets.Contains("arcane") || government == Government.Magocracy ? MagicLaw.Embraced
                : tenets.Contains("zealous") || tenets.Contains("pacifist") ? MagicLaw.Outlawed
                : MagicLaw.Licensed,
            Legitimacy = rng.NextInt(60, 81),
            Stability = rng.NextInt(60, 81),
            CultureId = region.CultureId,
            FaithId = region.FaithId,
            Colors = colors.ToArray(),
        };
        world.Realms[realm.Id] = realm;
        region.OwnerRealmId = realm.Id;
        return realm;
    }

    /// <summary>The succession a government keeps: councils and temples elect, tribes follow seniority, crowns pass to the eldest.</summary>
    public static SuccessionLaw SuccessionFor(Government government) => government switch
    {
        Government.ElectiveKingdom or Government.MerchantRepublic or Government.Theocracy => SuccessionLaw.Elective,
        Government.Tribal => SuccessionLaw.Seniority,
        _ => SuccessionLaw.Primogeniture,
    };

    /// <summary>Multi-source Dijkstra over travel days: each region → the nearest source region.</summary>
    public static Dictionary<long, long> NearestSource(RegionGraph graph, IReadOnlyList<long> sources)
    {
        var distance = new Dictionary<long, double>();
        var nearest = new Dictionary<long, long>();
        var queue = new PriorityQueue<long, (double Days, long Region)>();
        foreach (var s in sources)
        {
            distance[s] = 0;
            nearest[s] = s;
            queue.Enqueue(s, (0, s));
        }
        while (queue.TryDequeue(out var region, out var priority))
        {
            if (priority.Days > distance[region]) continue;
            foreach (var link in graph.LinksOf(region))
            {
                var next = link.Other(region);
                var days = priority.Days + link.TravelDays;
                if (distance.TryGetValue(next, out var known) && known <= days) continue;
                distance[next] = days;
                nearest[next] = nearest[region];
                queue.Enqueue(next, (days, next));
            }
        }
        return nearest;
    }
}

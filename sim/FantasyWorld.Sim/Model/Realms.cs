namespace FantasyWorld.Sim.Model;

/// <summary>Queries over realms: rulers, hierarchy, territory and neighbours.</summary>
public static class Realms
{
    /// <summary>The ruler is whoever is lord of the capital (null during an interregnum).</summary>
    public static Character? RulerOf(World world, Realm realm) =>
        world.Settlements.TryGetValue(realm.CapitalId, out var capital) && capital.LordId != 0
            ? world.Characters[capital.LordId]
            : null;

    /// <summary>The independent realm at the top of the realm's chain of lieges.</summary>
    public static Realm Top(World world, Realm realm)
    {
        var current = realm;
        for (var guard = 0; current.LiegeId != 0 && guard < 64; guard++) current = world.Realms[current.LiegeId];
        return current;
    }

    public static IEnumerable<Realm> Independent(World world) =>
        world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == 0);

    public static IEnumerable<Realm> VassalsOf(World world, Realm realm) =>
        world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == realm.Id);

    /// <summary>Regions held directly.</summary>
    public static IEnumerable<Region> Domain(World world, Realm realm) =>
        world.Regions.Values.Where(r => r.OwnerRealmId == realm.Id);

    /// <summary>Regions held directly or through vassals, at any depth.</summary>
    public static IEnumerable<Region> Territory(World world, Realm realm)
    {
        var members = new HashSet<long> { realm.Id };
        foreach (var r in world.Realms.Values.Where(r => !r.Destroyed))
            if (IsBelow(world, r, realm)) members.Add(r.Id);
        return world.Regions.Values.Where(r => members.Contains(r.OwnerRealmId));
    }

    /// <summary>The independent realm a region ultimately belongs to (null if unclaimed).</summary>
    public static Realm? SovereignOf(World world, Region region) =>
        world.Realms.TryGetValue(region.OwnerRealmId, out var owner) ? Top(world, owner) : null;

    /// <summary>Independent realms whose territory touches this realm's territory on the region graph.</summary>
    public static IEnumerable<Realm> Neighbours(World world, Realm realm)
    {
        var graph = world.Geography?.Graph;
        if (graph is null) return Array.Empty<Realm>();
        var ids = new SortedSet<long>();
        foreach (var region in Territory(world, realm))
            foreach (var n in graph.Neighbours(region.Id))
                if (SovereignOf(world, world.Regions[n]) is { } other && other.Id != realm.Id) ids.Add(other.Id);
        return ids.Select(id => world.Realms[id]);
    }

    public static bool IsBelow(World world, Realm realm, Realm liege)
    {
        var current = realm;
        for (var guard = 0; current.LiegeId != 0 && guard < 64; guard++)
        {
            if (current.LiegeId == liege.Id) return true;
            current = world.Realms[current.LiegeId];
        }
        return false;
    }

    /// <summary>The independent realm a settlement belongs to.</summary>
    public static Realm? SovereignOf(World world, Settlement settlement) => SovereignOf(world, world.Regions[settlement.RegionId]);

    /// <summary>Rank by size: County &lt; 3 regions, Duchy &lt; 8, Kingdom otherwise; Empire with 20+ regions and 2+ kingdom-sized vassals.</summary>
    public static RealmRank RankFor(World world, Realm realm)
    {
        var size = Territory(world, realm).Count();
        if (size >= 20 && VassalsOf(world, realm).Count(v => Territory(world, v).Count() >= 8) >= 2) return RealmRank.Empire;
        return size switch { >= 8 => RealmRank.Kingdom, >= 3 => RealmRank.Duchy, _ => RealmRank.County };
    }
}

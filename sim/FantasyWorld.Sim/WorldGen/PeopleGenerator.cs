using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The starting notables: a lord's household in every town and city (with knights, priests, merchants
/// and mages in cities), an elder in every village, adventurers in dangerous regions. Ancient
/// artifacts are given to the greatest lords or lie lost in the most dangerous regions.
/// </summary>
public static class PeopleGenerator
{
    public static void Populate(World world, Rng rng)
    {
        var today = world.Date.Day;
        long BornYearsAgo(int min, int max) => today - (long)rng.NextInt(min, max + 1) * WorldDate.DaysPerYear - rng.NextInt(0, 360);

        foreach (var settlement in world.Settlements.Values.Where(s => !s.Abandoned))
        {
            var region = world.Regions[settlement.RegionId];
            switch (settlement.Tier)
            {
                case SettlementTier.City:
                    Household(world, rng, settlement, BornYearsAgo, maxChildren: 4);
                    foreach (var role in new[] { Role.Knight, Role.Knight, Role.Priest, Role.Merchant })
                        Single(world, rng, settlement, role, BornYearsAgo(20, 50));
                    if (region.LeyPower >= 40) Single(world, rng, settlement, Role.Mage, BornYearsAgo(25, 70));
                    break;
                case SettlementTier.Town:
                    Household(world, rng, settlement, BornYearsAgo, maxChildren: 3);
                    Single(world, rng, settlement, rng.Chance(0.5) ? Role.Knight : rng.Chance(0.5) ? Role.Priest : Role.Merchant,
                        BornYearsAgo(20, 55));
                    break;
                case SettlementTier.Village:
                    var elder = Single(world, rng, settlement, Role.Elder, BornYearsAgo(45, 65));
                    if (rng.Chance(0.5)) Spouse(world, rng, elder, BornYearsAgo);
                    break;
            }
        }

        foreach (var region in world.Regions.Values.Where(r => r.Danger >= 55))
        {
            var home = world.LivingSettlements(region).OrderByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
            if (home is not null) Single(world, rng, home, Role.Adventurer, BornYearsAgo(18, 35));
        }

        PlaceAncientArtifacts(world, rng);
    }

    private static void Household(World world, Rng rng, Settlement settlement, Func<int, int, long> bornYearsAgo, int maxChildren)
    {
        var region = world.Regions[settlement.RegionId];
        var house = CharacterFactory.NewHouse(world, rng, region.CultureId, settlement.Id);
        var lord = CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male,
            bornYearsAgo(25, 55), house.Id, Role.Lord);
        settlement.LordId = lord.Id;
        var spouse = Spouse(world, rng, lord, bornYearsAgo);
        var youngest = Math.Min(lord.Age(world.Date), spouse.Age(world.Date)) - 18;
        var (father, mother) = lord.Sex == Sex.Male ? (lord, spouse) : (spouse, lord);
        var children = youngest > 0 ? rng.NextInt(0, maxChildren + 1) : 0;
        for (var i = 0; i < children; i++)
            CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male,
                bornYearsAgo(0, Math.Max(0, youngest - 1)), house.Id, father: father, mother: mother);
    }

    private static Character Single(World world, Rng rng, Settlement settlement, Role role, long birthDay)
    {
        var region = world.Regions[settlement.RegionId];
        var house = CharacterFactory.NewHouse(world, rng, region.CultureId, settlement.Id);
        return CharacterFactory.Create(world, rng, settlement.Id, rng.Chance(0.5) ? Sex.Female : Sex.Male, birthDay, house.Id, role);
    }

    private static Character Spouse(World world, Rng rng, Character partner, Func<int, int, long> bornYearsAgo)
    {
        var age = partner.Age(world.Date);
        var spouse = CharacterFactory.Create(world, rng, partner.SettlementId, partner.Sex == Sex.Male ? Sex.Female : Sex.Male,
            bornYearsAgo(Math.Max(18, age - 6), Math.Max(18, age + 6)), partner.HouseId);
        partner.SpouseId = spouse.Id;
        spouse.SpouseId = partner.Id;
        return spouse;
    }

    private static void PlaceAncientArtifacts(World world, Rng rng)
    {
        var count = Math.Max(3, world.Regions.Count / 5);
        var names = new NameGen(rng);
        var lords = world.Settlements.Values
            .Where(s => s.LordId != 0)
            .OrderByDescending(s => s.Tier).ThenByDescending(s => s.Population).ThenBy(s => s.Id)
            .Select(s => world.Characters[s.LordId])
            .ToList();
        var lairs = world.Regions.Values.OrderByDescending(r => r.Danger).ThenBy(r => r.Id).ToList();
        for (var i = 0; i < count; i++)
        {
            var heldByLord = i % 2 == 0 && i / 2 < lords.Count;
            var region = heldByLord ? world.Regions[world.Settlements[lords[i / 2].SettlementId].RegionId] : lairs[(i / 2) % lairs.Count];
            var rare = region.Resources.Keys.Where(ResourceKinds.IsRare).ToList();
            var artifact = ArtifactForge.Create(rng, world.Ids, names, 0, rareMaterials: rare, fame: rng.NextInt(30, 61));
            if (heldByLord)
            {
                artifact.Place = ArtifactPlace.Held;
                artifact.HolderId = lords[i / 2].Id;
            }
            else
            {
                artifact.Place = ArtifactPlace.Lost;
                artifact.RegionId = region.Id;
            }
            world.Artifacts[artifact.Id] = artifact;
        }
    }
}

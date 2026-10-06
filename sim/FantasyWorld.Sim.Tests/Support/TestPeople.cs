using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Builds notables, houses and artifacts on top of TestWorlds.</summary>
public static class TestPeople
{
    /// <summary>TwoRegions plus one culture and faith covering both regions.</summary>
    public static World World(ulong seed = 1)
    {
        var world = TestWorlds.TwoRegions(seed);
        var faith = new Faith { Id = world.Ids.Take(Faith.Kind), Name = "The Way of Ohm", Deity = "Ohm", HolyRegionId = 1 };
        var culture = new Culture { Id = world.Ids.Take(Culture.Kind), Name = "Westfolk", HomeRegionId = 1, FaithId = faith.Id };
        world.Faiths[faith.Id] = faith;
        world.Cultures[culture.Id] = culture;
        foreach (var region in world.Regions.Values) (region.CultureId, region.FaithId) = (culture.Id, faith.Id);
        return world;
    }

    public static Character Add(World world, long settlementId, int age, Sex sex = Sex.Male, Role role = Role.None,
        long houseId = 0, string[]? traits = null, long fatherId = 0, long motherId = 0)
    {
        if (houseId == 0)
        {
            var house = new House { Id = world.Ids.Take(House.Kind), Name = $"House{world.Houses.Count + 1}", SeatSettlementId = settlementId, CultureId = 1 };
            world.Houses[house.Id] = house;
            houseId = house.Id;
        }
        var c = new Character
        {
            Id = world.Ids.Take(Character.Kind),
            Name = $"Person{world.Characters.Count + 1}",
            HouseId = houseId,
            Sex = sex,
            BirthDay = world.Date.Day - (long)age * WorldDate.DaysPerYear,
            CultureId = 1,
            FaithId = 1,
            SettlementId = settlementId,
            FatherId = fatherId,
            MotherId = motherId,
            Attributes = new Attributes { Martial = 10, Diplomacy = 10, Stewardship = 10, Intrigue = 10, Lore = 10, Prowess = 10 },
            Traits = (traits ?? Array.Empty<string>()).ToList(),
            Role = role,
        };
        world.Characters[c.Id] = c;
        if (world.Houses[houseId].FounderId == 0) world.Houses[houseId].FounderId = c.Id;
        foreach (var parent in new[] { fatherId, motherId })
            if (parent != 0) world.Characters[parent].Children.Add(c.Id);
        if (role == Role.Lord) world.Settlements[settlementId].LordId = c.Id;
        return c;
    }

    public static void Marry(World world, Character a, Character b, long marriageEventId = 0)
    {
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
        a.MarriageEventId = b.MarriageEventId = marriageEventId;
    }

    public static Artifact GiveArtifact(World world, Character holder, params ArtifactPower[] powers)
    {
        var artifact = new Artifact
        {
            Id = world.Ids.Take(Artifact.Kind),
            Name = $"Relic{world.Artifacts.Count + 1}",
            Type = ArtifactType.Weapon,
            Material = "steel",
            Powers = powers.ToList(),
            Place = ArtifactPlace.Held,
            HolderId = holder.Id,
        };
        world.Artifacts[artifact.Id] = artifact;
        return artifact;
    }
}

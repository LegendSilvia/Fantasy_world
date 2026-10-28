using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Creates notables and houses: culture-styled names, attributes (inherited from parents or rolled),
/// 3–4 non-conflicting traits and an ambition. Used by world generation, births and promotions.
/// </summary>
public static class CharacterFactory
{
    public static Character Create(World world, Rng rng, long settlementId, Sex sex, long birthDay, long houseId,
        Role role = Role.None, Character? father = null, Character? mother = null, string? name = null)
    {
        var region = world.Regions[world.Settlements[settlementId].RegionId];
        var lineageParent = LineageParent(world, father, mother);
        var cultureId = lineageParent?.CultureId ?? region.CultureId;
        var culture = world.Cultures.GetValueOrDefault(cultureId);

        var character = new Character
        {
            Id = world.Ids.Take(Character.Kind),
            Name = name ?? new NameGen(rng, culture?.Style).Given(),
            HouseId = houseId,
            Sex = sex,
            BirthDay = birthDay,
            CultureId = cultureId,
            FaithId = lineageParent?.FaithId ?? region.FaithId,
            SettlementId = settlementId,
            FatherId = father?.Id ?? 0,
            MotherId = mother?.Id ?? 0,
            Attributes = RollAttributes(rng, role, father, mother, Peoples.Of(world, culture)),
            Traits = RollTraits(rng, father, mother),
            Role = role,
        };
        character.Ambition = ChooseAmbition(character, rng);
        world.Characters[character.Id] = character;
        father?.Children.Add(character.Id);
        mother?.Children.Add(character.Id);
        if (world.Houses.TryGetValue(houseId, out var house) && house.FounderId == 0) house.FounderId = character.Id;
        return character;
    }

    /// <summary>A new house with a name unique in the world, styled by its culture.</summary>
    public static House NewHouse(World world, Rng rng, long cultureId, long seatSettlementId)
    {
        var names = new NameGen(rng, world.Cultures.GetValueOrDefault(cultureId)?.Style);
        var taken = world.Houses.Values.Select(h => h.Name).ToHashSet(StringComparer.Ordinal);
        var name = names.House();
        for (var attempt = 0; taken.Contains(name) && attempt < 20; attempt++) name = names.House();
        if (taken.Contains(name)) name = $"{name} {world.Houses.Count + 1}";
        var house = new House { Id = world.Ids.Take(House.Kind), Name = name, SeatSettlementId = seatSettlementId, CultureId = cultureId };
        world.Houses[house.Id] = house;
        return house;
    }

    /// <summary>The parent whose house and culture children follow: by the culture's lineage, else whoever exists.</summary>
    public static Character? LineageParent(World world, Character? father, Character? mother)
    {
        if (father is null || mother is null) return father ?? mother;
        var lineage = world.Cultures.GetValueOrDefault(father.CultureId)?.Lineage ?? Lineage.Patrilineal;
        return lineage == Lineage.Matrilineal ? mother : father;
    }

    public static string ChooseAmbition(Character c, Rng rng) => c.Role switch
    {
        Role.Mage => Ambitions.Magic,
        Role.Knight or Role.Adventurer or Role.Rebel => Ambitions.Renown,
        Role.Merchant => Ambitions.Wealth,
        _ when c.Has(Traits.Greedy) || c.Has(Traits.Ambitious) => rng.Chance(0.5) ? Ambitions.Artifact : Ambitions.Renown,
        _ when c.Has(Traits.Brave) => Ambitions.Renown,
        _ when c.Attributes.Lore >= 14 => Ambitions.Magic,
        _ => Ambitions.Family,
    };

    /// <summary>Born of parents, a child takes after them; anyone else after their race's gifts (sub-project 13).</summary>
    private static Attributes RollAttributes(Rng rng, Role role, Character? father, Character? mother, Race? race = null)
    {
        var attributes = new Attributes();
        foreach (var name in Attributes.Names)
        {
            var value = father is not null && mother is not null
                ? (father.Attributes.Get(name) + mother.Attributes.Get(name)) / 2 + rng.NextInt(-3, 4)
                : rng.NextInt(1, 11) + rng.NextInt(0, 10);
            var gift = father is null || mother is null ? race?.Gifts.GetValueOrDefault(name) ?? 0 : 0;
            attributes.Set(name, Math.Max(1, value + RoleBonus(role, name) + gift));
        }
        return attributes;
    }

    private static int RoleBonus(Role role, string attribute) => (role, attribute) switch
    {
        (Role.Lord, "stewardship" or "diplomacy") => 3,
        (Role.Knight, "martial" or "prowess") => 4,
        (Role.Adventurer or Role.Rebel, "prowess") => 3,
        (Role.Mage, "lore") => 5,
        (Role.Priest, "lore") => 3,
        (Role.Priest, "diplomacy") => 2,
        (Role.Merchant, "stewardship") => 4,
        (Role.Elder, "stewardship") => 2,
        _ => 0,
    };

    private static List<string> RollTraits(Rng rng, Character? father, Character? mother)
    {
        var traits = new List<string>();
        void TryAdd(string trait)
        {
            if (!traits.Contains(trait) && (Traits.OppositeOf(trait) is not { } o || !traits.Contains(o))) traits.Add(trait);
        }
        foreach (var parent in new[] { father, mother })
            if (parent is not null && rng.Chance(0.5))
            {
                var inheritable = parent.Traits.Where(t => Traits.OppositeOf(t) is not null).ToList();
                if (inheritable.Count > 0) TryAdd(inheritable[rng.NextInt(0, inheritable.Count)]);
            }
        var wanted = rng.NextInt(3, 5);
        for (var attempt = 0; traits.Count < wanted && attempt < 20; attempt++)
        {
            var (a, b) = Traits.Opposites[rng.NextInt(0, Traits.Opposites.Count)];
            TryAdd(rng.Chance(0.5) ? a : b);
        }
        traits.Sort(StringComparer.Ordinal);
        return traits;
    }
}

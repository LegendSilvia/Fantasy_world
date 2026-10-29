using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum Sex { Female, Male }

public enum LifeStage { Child, Adult, Elder, Dead }

/// <summary>A notable's place in society. Lords are the settlement's ruler (Settlement.LordId is the source of truth).</summary>
public enum Role { None, Lord, Knight, Mage, Priest, Merchant, Elder, Adventurer, Rebel }

/// <summary>The six attributes, each 0–20.</summary>
public sealed class Attributes
{
    public int Martial { get; set; }
    public int Diplomacy { get; set; }
    public int Stewardship { get; set; }
    public int Intrigue { get; set; }
    public int Lore { get; set; }
    public int Prowess { get; set; }

    public static readonly IReadOnlyList<string> Names =
        new[] { "martial", "diplomacy", "stewardship", "intrigue", "lore", "prowess" };

    public int Get(string name) => name switch
    {
        "martial" => Martial,
        "diplomacy" => Diplomacy,
        "stewardship" => Stewardship,
        "intrigue" => Intrigue,
        "lore" => Lore,
        "prowess" => Prowess,
        _ => throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name)),
    };

    public void Set(string name, int value)
    {
        value = Math.Clamp(value, 0, 20);
        switch (name)
        {
            case "martial": Martial = value; break;
            case "diplomacy": Diplomacy = value; break;
            case "stewardship": Stewardship = value; break;
            case "intrigue": Intrigue = value; break;
            case "lore": Lore = value; break;
            case "prowess": Prowess = value; break;
            default: throw new ArgumentException($"Unknown attribute '{name}'.", nameof(name));
        }
    }
}

/// <summary>A remembered event. About is the character it concerns (0 for none); Feeling −100…100; Weight decays yearly.</summary>
public sealed record Memory(long EventId, long About, double Feeling, double Weight);

/// <summary>A named, individually simulated person (spec §3 "Notables").</summary>
public sealed class Character
{
    public const string Kind = "char";
    public const int AdultAge = 16;
    public const int ElderAge = 60;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long HouseId { get; set; }
    public Sex Sex { get; init; }
    public long BirthDay { get; init; }
    public WorldDate? Died { get; set; }
    public string? DeathCause { get; set; }

    public long CultureId { get; init; }
    public long FaithId { get; set; }

    /// <summary>The settlement the character lives in.</summary>
    public long SettlementId { get; set; }

    public long FatherId { get; init; }
    public long MotherId { get; init; }
    public long SpouseId { get; set; }

    /// <summary>The event that began the current marriage (cited by births).</summary>
    public long MarriageEventId { get; set; }

    public List<long> Children { get; init; } = new();

    public Attributes Attributes { get; init; } = new();
    public List<string> Traits { get; init; } = new();
    public string Ambition { get; set; } = Model.Ambitions.Family;

    /// <summary>Whom the ambition targets (a rival for vengeance), 0 for none.</summary>
    public long AmbitionTarget { get; set; }

    public Role Role { get; set; }
    public List<Memory> Memories { get; init; } = new();

    /// <summary>Lasting injuries, e.g. "sword arm".</summary>
    public List<string> Wounds { get; init; } = new();

    /// <summary>Their own signature ability, made by Claude to suit them once the chronicle airs; null: their calling's.</summary>
    public Ability? Signature { get; set; }

    /// <summary>Their own weapon, made by Claude to suit them once the chronicle airs; null: their calling's (a relic outranks both).</summary>
    public Gear? Weapon { get; set; }

    /// <summary>0–100. Grows with deeds; raises the importance of their events.</summary>
    public double Renown { get; set; }

    /// <summary>Their calling (<see cref="Classes"/>), taken at coming of age; empty until then.</summary>
    public string Class { get; set; } = "";

    /// <summary>The abilities won through what befell them (<see cref="Model.Abilities"/>); the signature one is not stored.</summary>
    public List<Ability> Abilities { get; init; } = new();

    /// <summary>Their companion, beast or sworn (<see cref="Model.Companion"/>); null for none.</summary>
    public Companion? Companion { get; set; }

    /// <summary>
    /// The deed whose awakening sleeps within them, untold, until it wakes (on its day, or in a fight; the owner,
    /// 2026-10-29); 0 for none.
    /// </summary>
    public long Dormant { get; set; }

    /// <summary>The last day what befell them changed them (<see cref="Systems.TransformSystem"/>); 0 for never.</summary>
    public long ChangedDay { get; set; }

    /// <summary>The event that declared them a saint after death (0 if not one).</summary>
    public long SaintEventId { get; set; }

    /// <summary>No longer individually followed: absorbed into the settlement's populace.</summary>
    public bool Faded { get; set; }

    [JsonIgnore] public bool IsAlive => Died is null;

    /// <summary>Alive and still a notable.</summary>
    [JsonIgnore] public bool IsActive => Died is null && !Faded;

    public int Age(WorldDate today) => (int)((today.Day - BirthDay) / WorldDate.DaysPerYear);

    public LifeStage Stage(WorldDate today) => Died is not null
        ? LifeStage.Dead
        : Age(today) switch { < AdultAge => LifeStage.Child, < ElderAge => LifeStage.Adult, _ => LifeStage.Elder };

    public bool Has(string trait) => Traits.Contains(trait);

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

/// <summary>A family line (spec "house/dynasty").</summary>
public sealed class House
{
    public const string Kind = "house";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long FounderId { get; set; }
    public long SeatSettlementId { get; set; }
    public long CultureId { get; init; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

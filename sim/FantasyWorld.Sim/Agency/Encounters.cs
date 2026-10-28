using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Agency;

public enum Fate { Unharmed, Wounded, Maimed, Dead }

/// <summary>A beat of a fight's course, on a day of it (1 = the day it began), as Claude told it (sub-project 8 §5).</summary>
public sealed record FightChapter(int Day, string Text)
{
    /// <summary>The chapters falling on a day of the fight, as one account (null when none do).</summary>
    public static string? On(IReadOnlyList<FightChapter> chapters, int day) =>
        chapters.Where(c => c.Day == day).Select(c => c.Text).ToList() is { Count: > 0 } texts ? string.Join(" ", texts) : null;
}

/// <summary>A legal result of an encounter, with its base probability from the odds.</summary>
public sealed record EncounterOutcome(string Key, double Probability, bool AttackerWins, Fate AttackerFate, Fate DefenderFate)
{
    /// <summary>How it happened, when an LLM arbitrated (spec §8.3): stored as the event's account.</summary>
    public string? Account { get; init; }

    /// <summary>How many days the fight lasts, its outcome applied on the last; the rules settle every fight in one.</summary>
    public int Days { get; init; } = 1;

    /// <summary>The fight's course, when Claude decided it: its beats in order of their days.</summary>
    public IReadOnlyList<FightChapter> Chapters { get; init; } = [];
}

/// <summary>
/// A dramatic personal conflict (spec §8.3). The engine sets the stage (strengths) and enumerates the
/// legal outcomes with probabilities summing to 1; an arbiter picks one. DefenderId 0 means the wilds
/// (monsters, a labyrinth) rather than a person.
/// </summary>
public sealed record Encounter(
    string Kind,
    long AttackerId,
    long DefenderId,
    double AttackerStrength,
    double DefenderStrength,
    bool ToTheDeath,
    IReadOnlyList<EncounterOutcome> Outcomes)
{
    /// <summary>Outcomes below this base probability are implausible; the LLM arbiter (Plan 5) may not choose them.</summary>
    public const double PlausibilityFloor = 0.05;

    public double AttackerOdds => AttackerStrength / (AttackerStrength + DefenderStrength);

    public bool IsPlausible(EncounterOutcome outcome) => outcome.Probability >= PlausibilityFloor;
}

public interface IArbiter
{
    EncounterOutcome Resolve(Encounter encounter, Rng rng);

    /// <summary>A battle decided whole (sub-project 8 §5), or null for the rules' roll (offline, over budget, a rules arbiter).</summary>
    BattlePlan? PlanBattle(BattleStage stage) => null;
}

/// <summary>Seeded roll on the base probabilities: the offline fallback and the test baseline.</summary>
public sealed class RulesArbiter : IArbiter
{
    public EncounterOutcome Resolve(Encounter encounter, Rng rng)
    {
        var roll = rng.NextDouble();
        foreach (var outcome in encounter.Outcomes)
        {
            roll -= outcome.Probability;
            if (roll < 0) return outcome;
        }
        return encounter.Outcomes[^1];
    }
}

/// <summary>Builds encounters: strengths from attributes, traits, wounds, renown and artifacts; legal outcome sets.</summary>
public static class Encounters
{
    public const string Duel = "duel";
    public const string Hunt = "hunt";
    public const string Delve = "delve";

    /// <summary>Fighting strength: prowess + martial/2 + artifact bonuses + traits − wounds + renown/20 (min 1).</summary>
    public static double Strength(World world, Character c)
    {
        var artifacts = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList();
        var strength = c.Attributes.Prowess + artifacts.Sum(a => a.Bonus("prowess"))
                       + (c.Attributes.Martial + artifacts.Sum(a => a.Bonus("martial"))) / 2.0
                       + c.Renown / 20;
        if (c.Has(Traits.Brave)) strength += 2;
        if (c.Has(Traits.Craven)) strength -= 3;
        strength -= 2 * Math.Min(3, c.Wounds.Count);
        var age = c.Age(world.Date);
        if (age < 18) strength -= 4;
        if (age >= 55) strength -= (age - 50) / 5;
        return Math.Max(1, strength) * (Systems.PrayerSystem.Favoured(world, c.Ref) ? Systems.PrayerSystem.FavourStrength : 1);
    }

    public static Encounter DuelBetween(World world, Character attacker, Character defender, bool toTheDeath)
    {
        // A calling that counters the other's fights 15% stronger (the owner, 2026-10-28).
        var (ka, kd) = (Classes.Of(world, attacker), Classes.Of(world, defender));
        var a = Strength(world, attacker) * Classes.Edge(ka, kd);
        var d = Strength(world, defender) * Classes.Edge(kd, ka);
        var p = a / (a + d);
        var outcomes = toTheDeath
            ? new List<EncounterOutcome>
            {
                new("attacker_kills", p * 0.97 * 0.6, true, Fate.Unharmed, Fate.Dead),
                new("attacker_maims", p * 0.97 * 0.2, true, Fate.Unharmed, Fate.Maimed),
                new("attacker_wounds", p * 0.97 * 0.2, true, Fate.Wounded, Fate.Wounded),
                new("defender_kills", (1 - p) * 0.97 * 0.6, false, Fate.Dead, Fate.Unharmed),
                new("defender_maims", (1 - p) * 0.97 * 0.2, false, Fate.Maimed, Fate.Unharmed),
                new("defender_wounds", (1 - p) * 0.97 * 0.2, false, Fate.Wounded, Fate.Wounded),
                new("both_die", 0.03, false, Fate.Dead, Fate.Dead),
            }
            : new List<EncounterOutcome>
            {
                new("attacker_first_blood", p * 0.8, true, Fate.Unharmed, Fate.Wounded),
                new("attacker_yields_win", p * 0.2, true, Fate.Unharmed, Fate.Unharmed),
                new("defender_first_blood", (1 - p) * 0.8, false, Fate.Wounded, Fate.Unharmed),
                new("defender_yields_win", (1 - p) * 0.2, false, Fate.Unharmed, Fate.Unharmed),
            };
        return new Encounter(Duel, attacker.Id, defender.Id, a, d, toTheDeath, outcomes);
    }

    /// <summary>Hunting the monsters of a region: the wilds' strength is danger / 4.</summary>
    public static Encounter HuntIn(World world, Character hunter, Region region) =>
        Wilds(Hunt, world, hunter, Math.Max(1, region.Danger / 4), deathShare: 0.15);

    /// <summary>Hunting a named beast: its strength is half its might, and losing to it is often fatal.</summary>
    public static Encounter HuntBeast(World world, Character hunter, Monster beast) =>
        Wilds(Hunt, world, hunter, Math.Max(1, beast.Might / 2), deathShare: 0.25);

    /// <summary>Delving for a lost artifact: the wilds' strength is danger / 3; failure is deadlier.</summary>
    /// <summary>Delving one level of a labyrinth: each level down is harder than the land above it.</summary>
    public static Encounter DelveLevel(World world, Character delver, Region region, int level) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3 + level * 2), deathShare: 0.3);

    public static Encounter DelveIn(World world, Character delver, Region region) =>
        Wilds(Delve, world, delver, Math.Max(1, region.Danger / 3), deathShare: 0.3);

    private static Encounter Wilds(string kind, World world, Character attacker, double wilds, double deathShare)
    {
        var a = Strength(world, attacker);
        var p = a / (a + wilds);
        var outcomes = new List<EncounterOutcome>
        {
            new("success", p * 0.7, true, Fate.Unharmed, Fate.Unharmed),
            new("success_wounded", p * 0.3, true, Fate.Wounded, Fate.Unharmed),
            new("failure_wounded", (1 - p) * (1 - deathShare) * 0.7, false, Fate.Wounded, Fate.Unharmed),
            new("failure_maimed", (1 - p) * (1 - deathShare) * 0.3, false, Fate.Maimed, Fate.Unharmed),
            new("failure_death", (1 - p) * deathShare, false, Fate.Dead, Fate.Unharmed),
        };
        return new Encounter(kind, attacker.Id, 0, a, wilds, true, outcomes);
    }
}

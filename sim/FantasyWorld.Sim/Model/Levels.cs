using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// Levels (the owner, 2026-10-30): every character, beast and labyrinth has one, from 1 to 10. A character earns experience
/// by living and by deeds; past the soft cap (5) each level costs far more, so most reach 4 or 5 in a life and only the
/// greatest go beyond. A beast's level follows its might (an elder dragon is 10); a labyrinth's is the level it asks of
/// those who would delve it. A higher level fights harder: <see cref="Power"/>.
/// </summary>
public static class Levels
{
    public const int Max = 10;

    /// <summary>Past this level, each one costs far more experience.</summary>
    public const int SoftCap = 5;

    /// <summary>The experience each level needs (index: the level).</summary>
    private static readonly double[] Needed = { 0, 0, 25, 60, 110, 180, 400, 700, 1100, 1600, 2300 };

    /// <summary>Experience from a year lived as an adult; a fighting calling trains harder.</summary>
    public const double YearOfLife = 2.5, YearOfArms = 4;

    /// <summary>The level an amount of experience reaches.</summary>
    public static int Of(double experience)
    {
        var level = 1;
        while (level < Max && experience >= Needed[level + 1]) level++;
        return level;
    }

    /// <summary>The experience a level needs.</summary>
    public static double NeededFor(int level) => Needed[Math.Clamp(level, 1, Max)];

    /// <summary>How much harder one of this level fights than one of level 1: a tenth more for each level.</summary>
    public static double Power(int level) => 1 + 0.1 * (Math.Clamp(level, 1, Max) - 1);

    /// <summary>A character's level; one never yet levelled (a child, or a world made before levels) is 1.</summary>
    public static int Of(Character c) => Math.Max(1, c.Level);

    /// <summary>A beast's level: one level for every fifteen of its might, a legend one higher (at most 9; only an elder dragon is 10).</summary>
    public static int Of(Monster m) => m.Level > 0 ? m.Level : Math.Clamp(1 + (int)(m.Might / 15) + (m.Legendary ? 1 : 0), 1, Max - 1);

    /// <summary>
    /// The level a labyrinth asks of those who delve it: deeper ones ask more (a great labyrinth sets its own).
    /// </summary>
    public static int Required(Structure maze) => maze.RequiredLevel > 0 ? maze.RequiredLevel : Math.Clamp((Math.Max(maze.Levels, 3) + 1) / 2, 2, 5);

    /// <summary>Whether a character may go down into a labyrinth: no more than one level short of what it asks.</summary>
    public static bool MayDelve(Character c, Structure maze) => Of(c) >= Required(maze) - 1;

    /// <summary>A town's level is its fortification, and goes no higher than this.</summary>
    public const int TownMax = 5;

    /// <summary>
    /// A town's level (the owner, 2026-10-30): its fortification. 1 for open ground, one more for each level of walls (a
    /// palisade, stone walls, great walls: towns raise two, cities three) and one more for a standing fortress, at most 5.
    /// </summary>
    public static int Of(World world, Settlement s) =>
        Math.Clamp(1 + s.Walls + (world.Structures.Values.Any(x => x.Type == StructureKind.Fortification && x.State == StructureState.Standing && x.SettlementId == s.Id) ? 1 : 0),
            1, TownMax);

    /// <summary>How much of a raid's harm a town of this level still suffers: three-twentieths less for each level.</summary>
    public static double Ward(int townLevel) => 1 - 0.15 * (Math.Clamp(townLevel, 1, TownMax) - 1);

    /// <summary>A beast no higher than a town's level (its walls) is driven off and does no harm; this many levels below suffices.</summary>
    public const int RepelMargin = 0;

    /// <summary>
    /// How willing one is to pick a fight with what stands above them (the owner, 2026-10-30: the lower avoid the higher): whole
    /// against their own level or below, half as willing for each level the other stands above them.
    /// </summary>
    public static double Wariness(int own, int other) => other > own ? Math.Pow(0.5, other - own) : 1;

    /// <summary>The renown at which a character's level is known to others: their name goes before them.</summary>
    public const int KnownAt = 40;

    /// <summary>
    /// The level one takes another to be (the owner, 2026-10-30: they only know an enemy is higher if the enemy has high
    /// renown): their true level when they are renowned, otherwise no higher than the watcher's own.
    /// </summary>
    public static int Seen(Character watcher, Character other) => other.Renown >= KnownAt ? Of(other) : Math.Min(Of(other), Of(watcher));

    /// <summary>How willing one is to pick a fight with another, by the level they take the other to be.</summary>
    public static double Wariness(Character own, Character other) => Wariness(Of(own), Seen(own, other));

    /// <summary>The chance, in a hundred, that one sought out by a higher level slips away from the fight: a fifth for each level between, at most four in five.</summary>
    public static int EvadeChance(int sought, int seeker) => seeker > sought ? Math.Min(80, 20 * (seeker - sought)) : 0;

    /// <summary>
    /// Whether one sought out by a higher level avoids the fight (the owner, 2026-10-30: the higher have less chance to fight
    /// the lower, who avoid them); rolled from the two and the moment, not the world's dice.
    /// </summary>
    public static bool Evades(Character sought, Character seeker, long moment) =>
        Hash.Percent(sought.Id * 7919 + seeker.Id, 7601, moment) < EvadeChance(Of(sought), Seen(sought, seeker));

    /// <summary>Those whose calling is fighting, who train harder each year.</summary>
    private static bool Armed(Character c) => c.Role is Role.Knight or Role.Adventurer or Role.Lord or Role.Rebel
                                              || c.Class is Classes.Warrior or Classes.Ranger or Classes.Paladin;

    /// <summary>A year's training: what an adult learns by living (more for those who live by arms).</summary>
    public static double Training(Character c) => Armed(c) ? YearOfArms : YearOfLife;

    /// <summary>
    /// The experience an event gives a character in it, by the part they played (the same table for the yearly tally and
    /// for a world made before levels, so both agree).
    /// </summary>
    public static double For(WorldEvent e, string role)
    {
        bool Won() => e.Payload.GetValueOrDefault("outcome") is { } o && (o.StartsWith("attacker", StringComparison.Ordinal) || o.StartsWith("success", StringComparison.Ordinal));
        return e.Type switch
        {
            "DuelFought" => (role == "challenger") == Won() ? (e.Payload.GetValueOrDefault("stakes") == "to the death" ? 30 : 18) : 8,
            "MonsterSlain" => 30,
            "MonstersSlain" => 12,
            "MonsterHuntFailed" => 5,
            "LabyrinthDelved" => 8 + 2 * (int.TryParse(e.Payload.GetValueOrDefault("level"), out var l) ? l : 1),
            "LabyrinthDelveFailed" or "DelveFailed" => 4,
            "LabyrinthCleared" => 40,
            "ArtifactFound" => 10,
            "BattleFought" => role.Contains("commander", StringComparison.Ordinal) ? 20 : role.Contains("knight", StringComparison.Ordinal) ? 14 : 0,
            "WoundedInBattle" => 6,
            "HeroChosen" => 15,
            "DemonLordSlain" or "DemonLordSealed" or "DemonLordDriven" => 120,
            "HeroFell" => 20,
            "ExpeditionEnded" => e.Payload.GetValueOrDefault("outcome") == "cleared" ? 40 : 10,
            "ElderDragonSlain" => 200,
            "DragonWounded" => 40,
            "DragonHuntFailed" => 15,
            _ => 0,
        };
    }

    /// <summary>What each character in an event earns from it: the most of the parts they played in it (a leader is also its delver).</summary>
    public static IEnumerable<(EntityRef Who, double Experience)> Earned(WorldEvent e) =>
        e.Actors.Where(a => a.Value.Kind == Character.Kind).GroupBy(a => a.Value).Select(g => (g.Key, g.Max(a => For(e, a.Key))));

    /// <summary>
    /// Levels for a world made before them (the owner, 2026-10-30: "add some migration"): each character's experience is
    /// what their years and their deeds in the chronicle would have earned, and their level follows. Characters already
    /// levelled are kept as they are.
    /// </summary>
    public static int Backfill(World world, EventLog log)
    {
        if (world.Levelled) return 0;
        world.Levelled = true;
        var unlevelled = world.Characters.Values.Where(c => c.Level == 0).ToDictionary(c => c.Ref);
        if (unlevelled.Count == 0) return 0;
        var earned = unlevelled.Keys.ToDictionary(r => r, _ => 0.0);
        foreach (var e in log.All)
            foreach (var (who, xp) in Earned(e))
                if (earned.ContainsKey(who)) earned[who] += xp;
        foreach (var (who, c) in unlevelled)
        {
            var end = c.Died ?? world.Date;
            var adultYears = Math.Max(0, (int)((end.Day - c.BirthDay) / WorldDate.DaysPerYear) - Character.AdultAge);
            c.Experience = earned[who] + adultYears * Training(c);
            c.Level = Of(c.Experience);
        }
        return unlevelled.Count;
    }
}

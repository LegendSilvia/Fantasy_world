namespace FantasyWorld.Sim.Agency;

/// <summary>
/// Two hosts met in battle, as the arbiter sees them (sub-project 8 §5): the war, the armies, the field, their strengths
/// after home ground and terrain, and the commanders and knights present on each side.
/// </summary>
public sealed record BattleStage(
    long WarId,
    long AttackerArmyId,
    long DefenderArmyId,
    long RegionId,
    double AttackerStrength,
    double DefenderStrength,
    IReadOnlyList<long> AttackerNotables,
    IReadOnlyList<long> DefenderNotables)
{
    public double AttackerOdds => AttackerStrength / Math.Max(1, AttackerStrength + DefenderStrength);
}

/// <summary>
/// How a battle goes: who wins, how many days it lasts (its end applied on the last), its course in dated chapters, the
/// share of each side's troops lost (within the rules' ranges) and the fates of the notables Claude named (the rest are
/// rolled by the rules).
/// </summary>
public sealed record BattlePlan(
    bool AttackerWins,
    int Days,
    IReadOnlyList<FightChapter> Chapters,
    double WinnerLosses,
    double LoserLosses,
    IReadOnlyDictionary<long, Fate> Fates);

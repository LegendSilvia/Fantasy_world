using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>Why a war is fought (spec §6). Every war cites the event that gave it its casus belli.</summary>
public static class CasusBelli
{
    public const string Claim = "claim";
    public const string HolyWar = "holy war";
    public const string Revenge = "revenge";
    public const string Reconquest = "reconquest";
    public const string ResourceNeed = "resource need";
    public const string ArtifactRecovery = "artifact recovery";
    public const string Independence = "independence";
    public const string BorderDispute = "border dispute";
    public const string Glory = "glory";

    /// <summary>A realm's people rose under a leader and hold their towns against the crown (sub-project 12).</summary>
    public const string Revolution = "revolution";

    /// <summary>The old hatred between two races (sub-project 13).</summary>
    public const string AncientHatred = "ancient hatred";

    /// <summary>A demon lord's hunger for the living lands (sub-project 13).</summary>
    public const string DarkConquest = "dark conquest";
}

public sealed class War
{
    public const string Kind = "war";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long AttackerId { get; init; }
    public long DefenderId { get; init; }
    public List<long> Attackers { get; init; } = new();
    public List<long> Defenders { get; init; } = new();
    public string CasusBelli { get; init; } = "";
    public long CasusBelliEventId { get; init; }
    public long TargetRegionId { get; init; }
    public long TargetArtifactId { get; init; }
    public long DeclaredEventId { get; set; }
    public long StartDay { get; init; }

    /// <summary>A declared war musters for a year and a day before any host is raised (sub-project 8 §4), so the
    /// declaration is news in one episode and the fighting begins in the next. 0 (wars declared before this) fights at once.</summary>
    public long HostilitiesDay { get; init; }

    public const int MusterDays = Core.WorldDate.DaysPerYear + 1;

    /// <summary>Still mustering: declared, but no host may yet be raised.</summary>
    public bool Mustering(Core.WorldDate date) => date.Day < HostilitiesDay;

    /// <summary>The day the fighting could begin: the end of the muster, or the declaration for older wars.</summary>
    [JsonIgnore] public long OpenDay => Math.Max(StartDay, HostilitiesDay);

    /// <summary>−100…100, positive favours the attackers: occupations plus BattleScore.</summary>
    public double Score { get; set; }

    /// <summary>Accumulated battle results, positive favours the attackers.</summary>
    public double BattleScore { get; set; }

    public int Battles { get; set; }
    public bool Ended { get; set; }
    public long EndedEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);

    public bool IsAttacker(long realm) => Attackers.Contains(realm);
    public bool Involves(long realm) => Attackers.Contains(realm) || Defenders.Contains(realm);
    public long LeaderOf(long realm) => IsAttacker(realm) ? AttackerId : DefenderId;
    public IReadOnlyList<long> EnemiesOf(long realm) => IsAttacker(realm) ? Defenders : Attackers;
}

public enum ArmyObjective { Defend, Siege, Return }

/// <summary>An army in the field (spec §6 "Armies"). Levies are borrowed from settlements and returned on disbanding.</summary>
public sealed class Army
{
    public const string Kind = "army";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long RealmId { get; init; }
    public long WarId { get; init; }
    public long CommanderId { get; set; }
    public int Levies { get; set; }
    public int MenAtArms { get; set; }
    public List<long> Knights { get; init; } = new();
    public int Mages { get; set; }
    public double Morale { get; set; } = 70;
    public double Supply { get; set; } = 100;
    public long RegionId { get; set; }
    public List<long> Path { get; init; } = new();

    /// <summary>Travel days left before reaching the next region on the path.</summary>
    public double DaysToNext { get; set; }

    public ArmyObjective Objective { get; set; }
    public long TargetRegionId { get; set; }

    /// <summary>Settlement id → levies raised there (returned when the army disbands).</summary>
    public SortedDictionary<long, int> RaisedFrom { get; init; } = new();

    public bool Disbanded { get; set; }

    /// <summary>The last day of the battle the army is locked in (sub-project 8 §5); it holds its ground until then. 0 when free.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long EngagedUntil { get; set; }

    /// <summary>Why its commander chose the move under way (an LLM's reasoning), told with the move when it is made.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Orders { get; set; }

    /// <summary>The army is held on the field by a battle Claude made longer than a day.</summary>
    public bool Engaged(Core.WorldDate date) => EngagedUntil >= date.Day;

    [JsonIgnore] public int Soldiers => Levies + MenAtArms + Knights.Count + Mages;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

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

    [JsonIgnore] public int Soldiers => Levies + MenAtArms + Knights.Count + Mages;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

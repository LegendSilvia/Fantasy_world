using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Core;

public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>A world calendar date: days since Year 1, Month 1, Day 1.</summary>
public readonly record struct WorldDate(long Day) : IComparable<WorldDate>
{
    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;

    [JsonIgnore] public int Year => (int)(Day / DaysPerYear) + 1;
    [JsonIgnore] public int Month => (int)(Day % DaysPerYear / DaysPerMonth) + 1;
    [JsonIgnore] public int DayOfMonth => (int)(Day % DaysPerMonth) + 1;
    [JsonIgnore] public Season Season => (Season)((Month - 1) / 3);
    [JsonIgnore] public bool IsFirstOfMonth => Day % DaysPerMonth == 0;
    [JsonIgnore] public bool IsFirstOfYear => Day % DaysPerYear == 0;

    public static WorldDate FromYmd(int year, int month, int day) =>
        new((long)(year - 1) * DaysPerYear + (month - 1) * DaysPerMonth + (day - 1));

    public WorldDate AddDays(long days) => new(Day + days);

    public int CompareTo(WorldDate other) => Day.CompareTo(other.Day);

    public static bool operator <(WorldDate a, WorldDate b) => a.Day < b.Day;
    public static bool operator >(WorldDate a, WorldDate b) => a.Day > b.Day;
    public static bool operator <=(WorldDate a, WorldDate b) => a.Day <= b.Day;
    public static bool operator >=(WorldDate a, WorldDate b) => a.Day >= b.Day;

    public override string ToString() => $"Y{Year} M{Month} D{DayOfMonth}";
}

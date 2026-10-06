using System.Globalization;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Cli;

public static class DurationParser
{
    private static readonly Regex Pattern = new(@"^(\d+)(d|mo|y)$", RegexOptions.CultureInvariant);

    /// <summary>Debug fast-forwards beyond this are almost certainly typos (and would run for hours).</summary>
    public const long MaxDays = 1000L * WorldDate.DaysPerYear;

    public static long ParseDays(string text)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
            throw new FormatException($"Bad duration '{text}'. Use e.g. 30d, 4mo or 7y.");
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            throw new FormatException($"Duration '{text}' is too long.");
        var days = match.Groups[2].Value switch
        {
            "d" => n,
            "mo" => n > MaxDays ? MaxDays + 1 : n * WorldDate.DaysPerMonth,
            _ => n > MaxDays ? MaxDays + 1 : n * WorldDate.DaysPerYear,
        };
        if (days > MaxDays) throw new FormatException($"Duration '{text}' is longer than 1000 years.");
        return days;
    }
}

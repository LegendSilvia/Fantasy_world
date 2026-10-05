using System.Globalization;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Cli;

public static class DurationParser
{
    private static readonly Regex Pattern = new(@"^(\d+)(d|mo|y)$", RegexOptions.CultureInvariant);

    public static long ParseDays(string text)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
            throw new FormatException($"Bad duration '{text}'. Use e.g. 30d, 4mo or 7y.");
        var n = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "d" => n,
            "mo" => n * WorldDate.DaysPerMonth,
            _ => n * WorldDate.DaysPerYear,
        };
    }
}

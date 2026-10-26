using System.Globalization;

namespace FantasyWorld.Broadcast;

/// <summary>Dated world backups (<c>backups/2026-10-27.fw.db</c>): the last N days, and the first of every month.</summary>
public static class BackupRetention
{
    public const string Prefix = "backups/";
    public const string Suffix = ".fw.db";

    public static string KeyFor(string date) => $"{Prefix}{date}{Suffix}";

    /// <summary>The backup keys to delete on <paramref name="today"/>. Keys that are not dated backups are never touched.</summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> keys, string today, int keepDays)
    {
        var cutoff = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1 - keepDays);
        var dated = keys
            .Select(k => (Key: k, Date: DateOf(k)))
            .Where(b => b.Date is not null)
            .Select(b => (b.Key, Date: b.Date!.Value))
            .OrderBy(b => b.Date)
            .ToList();
        var firstOfMonth = dated.GroupBy(b => (b.Date.Year, b.Date.Month)).Select(g => g.First().Key).ToHashSet();
        return dated.Where(b => b.Date < cutoff && !firstOfMonth.Contains(b.Key)).Select(b => b.Key).ToList();
    }

    private static DateOnly? DateOf(string key) =>
        key.StartsWith(Prefix, StringComparison.Ordinal) && key.EndsWith(Suffix, StringComparison.Ordinal)
        && DateOnly.TryParseExact(key[Prefix.Length..^Suffix.Length], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

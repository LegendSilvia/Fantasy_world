using System.Globalization;
using System.Text.RegularExpressions;

namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// Keeps the whole bucket (live, staging and anything else in it) under the owner's limit (10 GB, the owner's word,
/// 2026-10-27). Once it holds more than <see cref="PruneAbove"/> of the limit, objects are deleted, the least needed first,
/// until it holds no more than <see cref="PruneTo"/>, leaving a day's media room again:
/// <list type="number">
/// <item>film stretches of past days (the joined film is made from them on their own day);</item>
/// <item>past days' media outside the live prefix (dry runs);</item>
/// <item>live episode videos older than <see cref="KeepVideoDays"/> days (YouTube keeps them);</item>
/// <item>face-off images, the longest unused first (one is made again if it is needed again);</item>
/// <item>live maps and narrations older than the feed reaches (<see cref="KeepAudioDays"/> days);</item>
/// <item>dated backups, oldest first, never the newest of a prefix.</item>
/// </list>
/// The worlds, manifests, feeds, scripts and the website's bundle are never deleted.
/// </summary>
public static partial class StorageBudget
{
    public const double PruneAbove = 0.9, PruneTo = 0.8;

    public const int KeepVideoDays = 7, KeepAudioDays = Publishing.Feed.MaxItems;

    public static long Bytes(double gigabytes) => (long)(gigabytes * 1_000_000_000);

    /// <summary>Whether <paramref name="adding"/> more bytes still leaves the bucket under the pruning line.</summary>
    public static bool Room(long used, long adding, long limit) => used + adding <= limit * PruneAbove;

    /// <summary>
    /// The keys to delete on <paramref name="today"/> (yyyy-MM-dd), in order, from the bucket's objects (keys from the
    /// bucket's root). <paramref name="lastUsed"/> gives each face-off image's last use (yyyy-MM-dd), if known.
    /// </summary>
    public static IReadOnlyList<string> ToDelete(IReadOnlyList<StoredObject> objects, string today, long limit, string livePrefix,
        IReadOnlyDictionary<string, string>? lastUsed = null)
    {
        var total = objects.Sum(o => o.Bytes);
        if (total <= limit * PruneAbove) return [];
        var day = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Ago(int days) => day.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var episodes = objects.Select(o => (Object: o, Match: EpisodeFile().Match(o.Key))).Where(x => x.Match.Success)
            .Select(x => (x.Object, Prefix: x.Match.Groups["prefix"].Value, Date: x.Match.Groups["date"].Value, File: x.Match.Groups["rest"].Value))
            .OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Object.Key, StringComparer.Ordinal).ToList();
        static bool Media(string file) => file.EndsWith(".mp4", StringComparison.Ordinal) || file.EndsWith(".mp3", StringComparison.Ordinal)
                                          || file.EndsWith(".png", StringComparison.Ordinal);
        var backups = objects.Select(o => (Object: o, Match: Backup().Match(o.Key))).Where(x => x.Match.Success)
            .GroupBy(x => x.Match.Groups["prefix"].Value)
            .SelectMany(g => g.OrderBy(x => x.Match.Groups["date"].Value, StringComparer.Ordinal).SkipLast(1).Select(x => x.Object));

        var tiers = new IEnumerable<StoredObject>[]
        {
            episodes.Where(e => e.File.StartsWith("film/chunk-", StringComparison.Ordinal) && string.CompareOrdinal(e.Date, today) < 0).Select(e => e.Object),
            episodes.Where(e => e.Prefix != livePrefix && Media(e.File) && string.CompareOrdinal(e.Date, today) < 0).Select(e => e.Object),
            episodes.Where(e => e.Prefix == livePrefix && e.File == "episode.mp4" && string.CompareOrdinal(e.Date, Ago(KeepVideoDays)) < 0).Select(e => e.Object),
            objects.Where(o => Image().IsMatch(o.Key)).OrderBy(o => lastUsed?.GetValueOrDefault(o.Key) ?? "", StringComparer.Ordinal)
                .ThenBy(o => o.Key, StringComparer.Ordinal),
            episodes.Where(e => e.Prefix == livePrefix && e.File is "map.png" or "narration.mp3" && string.CompareOrdinal(e.Date, Ago(KeepAudioDays)) < 0)
                .Select(e => e.Object),
            backups,
        };
        var doomed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in tiers.SelectMany(t => t))
        {
            if (total <= limit * PruneTo) break;
            if (!seen.Add(o.Key)) continue;
            doomed.Add(o.Key);
            total -= o.Bytes;
        }
        return doomed;
    }

    [GeneratedRegex(@"^(?<prefix>(?:[^/]+/)?)episodes/(?<date>\d{4}-\d{2}-\d{2})/(?<rest>.+)$")]
    private static partial Regex EpisodeFile();

    [GeneratedRegex(@"^(?<prefix>(?:[^/]+/)?)backups/(?<date>\d{4}-\d{2}-\d{2})\.fw\.db$")]
    private static partial Regex Backup();

    [GeneratedRegex(@"^(?:[^/]+/)?images/[^/]+\.png$")]
    private static partial Regex Image();
}

using System.Text.Json;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>One chapter of a narrated episode: its title and where it starts, in whole seconds.</summary>
public sealed record Chapter(string Title, int Start);

/// <summary>A narration's chapters and its whole length in seconds.</summary>
public sealed record Timeline(int Length, IReadOnlyList<Chapter> Chapters)
{
    public static readonly Timeline None = new(0, []);

    /// <summary>The chapters as YouTube takes them (see <see cref="Speech.Chapters.ForYouTube"/>).</summary>
    public IReadOnlyList<Chapter> ForYouTube() => Speech.Chapters.ForYouTube(Chapters, Length);
}

/// <summary>
/// The chapters of a narrated episode (sub-project 7 §3), one per section, timed from the narration and kept beside the
/// episode (<c>episodes/&lt;date&gt;/chapters.json</c>), where YouTube and Discord read them.
/// </summary>
public static class Chapters
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>YouTube makes chapters only of at least three, the first at 0:00, each at least ten seconds long.</summary>
    public const int YouTubeMinimum = 3, YouTubeShortest = 10;

    public static string Key(string date) => $"episodes/{date}/chapters.json";

    /// <summary>The day's timeline; none for an episode narrated before chapters existed (or a file that cannot be read).</summary>
    public static async Task<Timeline> LoadAsync(IObjectStore store, string date, CancellationToken ct = default)
    {
        if (await store.GetAsync(Key(date), ct).ConfigureAwait(false) is not { } bytes) return Timeline.None;
        try
        {
            return JsonSerializer.Deserialize<Timeline>(bytes, Json) ?? Timeline.None;
        }
        catch (JsonException)
        {
            return Timeline.None;
        }
    }

    public static Task SaveAsync(IObjectStore store, string date, Timeline timeline, CancellationToken ct = default) =>
        store.PutAsync(Key(date), JsonSerializer.SerializeToUtf8Bytes(timeline, Json), "application/json", ct);

    /// <summary>The chapters from each section's title and its share of the narration, in bytes of a constant-bit-rate MP3.</summary>
    public static Timeline Timed(IReadOnlyList<string> titles, IReadOnlyList<long> bytes, int kbps)
    {
        if (titles.Count != bytes.Count) throw new ArgumentException("One title per section is needed.", nameof(titles));
        var chapters = new List<Chapter>();
        long before = 0;
        for (var i = 0; i < titles.Count; i++)
        {
            chapters.Add(new Chapter(titles[i], Voiceover.Seconds(before, kbps)));
            before += bytes[i];
        }
        return new Timeline(Voiceover.Seconds(before, kbps), chapters);
    }

    /// <summary>"1:05" (or "1:02:05").</summary>
    public static string Stamp(int seconds) => seconds >= 3600
        ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
        : $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>
    /// The chapters as YouTube takes them: each at least ten seconds long (a short chapter folds into the one before it; a
    /// short first chapter takes in the one after it), the first at 0:00, and at least three, else none.
    /// <paramref name="length"/> is the whole narration in seconds.
    /// </summary>
    public static IReadOnlyList<Chapter> ForYouTube(IReadOnlyList<Chapter> chapters, int length)
    {
        var kept = chapters.Select((c, i) => i == 0 ? c with { Start = 0 } : c).ToList();
        int End(int i) => i + 1 < kept.Count ? kept[i + 1].Start : length;
        for (var i = 0; i < kept.Count && kept.Count > 1;)
            if (End(i) - kept[i].Start >= YouTubeShortest) i++;
            else if (i == 0) kept.RemoveAt(1);
            else kept.RemoveAt(i--);
        return kept.Count >= YouTubeMinimum ? kept : [];
    }

    /// <summary>The chapter list, one "0:00 Title" line each.</summary>
    public static string Lines(IEnumerable<Chapter> chapters) =>
        string.Join("\n", chapters.Select(c => $"{Stamp(c.Start)} {c.Title}"));
}

using System.Globalization;
using System.Text;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>When each character of a spoken part was said, as ElevenLabs aligned it (seconds from the part's start).</summary>
public sealed record Alignment(IReadOnlyList<string> Characters, IReadOnlyList<double> Starts, IReadOnlyList<double> Ends);

/// <summary>A spoken part: its audio, and its alignment when the service gave one.</summary>
public sealed record SpokenPart(byte[] Audio, Alignment? Alignment);

/// <summary>One subtitle: what is said, from when to when (seconds into the narration).</summary>
public sealed record Cue(double Start, double End, string Text);

/// <summary>
/// The narration's subtitles (the owner, 2026-10-29: "subtitles, in sync with the narration"): built from the times
/// ElevenLabs gives each character it speaks, so every line appears as it is said, never by estimate. Lines of at most
/// <see cref="MaxLine"/> characters, broken at a sentence's end, or at a comma once a line is long enough.
/// </summary>
public static class Subtitles
{
    public const int MaxLine = 42;
    public const double MinSeconds = 1.0;

    public static string Key(string date) => $"episodes/{date}/subtitles.srt";

    /// <summary>The cues of one part, <paramref name="offset"/> seconds into the narration.</summary>
    public static IReadOnlyList<Cue> Cues(Alignment alignment, double offset)
    {
        var count = Math.Min(alignment.Characters.Count, Math.Min(alignment.Starts.Count, alignment.Ends.Count));
        var words = new List<(string Text, double Start, double End)>();
        var word = new StringBuilder();
        double start = 0, end = 0;
        for (var i = 0; i <= count; i++)
        {
            var c = i < count ? alignment.Characters[i] : " ";
            if (string.IsNullOrWhiteSpace(c))
            {
                if (word.Length > 0) words.Add((word.ToString(), start, end));
                word.Clear();
                continue;
            }
            if (word.Length == 0) start = alignment.Starts[i];
            word.Append(c);
            end = alignment.Ends[i];
        }
        var cues = new List<Cue>();
        var line = new List<(string Text, double Start, double End)>();
        void Flush()
        {
            if (line.Count == 0) return;
            cues.Add(new Cue(offset + line[0].Start, offset + line[^1].End, string.Join(" ", line.Select(w => w.Text))));
            line.Clear();
        }
        foreach (var w in words)
        {
            var length = line.Sum(x => x.Text.Length + 1) + w.Text.Length;
            if (line.Count > 0 && length > MaxLine) Flush();
            line.Add(w);
            var text = string.Join(" ", line.Select(x => x.Text));
            if (w.Text[^1] is '.' or '!' or '?' || w.Text[^1] is ',' or ';' or ':' && text.Length >= MaxLine / 2) Flush();
        }
        Flush();
        return cues;
    }

    /// <summary>Each cue held long enough to read, but never over the next.</summary>
    public static IReadOnlyList<Cue> Paced(IReadOnlyList<Cue> cues) =>
        cues.Select((c, i) => c with
        {
            End = Math.Min(Math.Max(c.End, c.Start + MinSeconds), i + 1 < cues.Count ? cues[i + 1].Start - 0.05 : double.MaxValue),
        }).ToList();

    public static string Srt(IReadOnlyList<Cue> cues)
    {
        static string Stamp(double s)
        {
            var t = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, s) * 1000));
            return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}");
        }
        var srt = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
            srt.Append(CultureInfo.InvariantCulture, $"{i + 1}\n{Stamp(cues[i].Start)} --> {Stamp(cues[i].End)}\n{cues[i].Text}\n\n");
        return srt.ToString();
    }

    public static Task SaveAsync(IObjectStore store, string date, IReadOnlyList<Cue> cues, CancellationToken ct = default) =>
        store.PutAsync(Key(date), Encoding.UTF8.GetBytes(Srt(Paced(cues))), "application/x-subrip", ct);

    public static async Task<string?> LoadAsync(IObjectStore store, string date, CancellationToken ct = default) =>
        await store.GetAsync(Key(date), ct).ConfigureAwait(false) is { Length: > 0 } bytes ? Encoding.UTF8.GetString(bytes) : null;
}

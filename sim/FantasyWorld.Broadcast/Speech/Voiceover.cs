using System.Text.RegularExpressions;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// Turning a script into one MP3 (M3): split on paragraphs (then sentences) under the model's per-request limit, each
/// request told the text around it for a continuous read, the parts joined frame to frame.
/// </summary>
public static partial class Voiceover
{
    /// <summary>The script in parts of at most <paramref name="limit"/> characters, breaking between paragraphs where it can.</summary>
    public static IReadOnlyList<string> Chunks(string script, int limit)
    {
        if (limit < 100) throw new ArgumentOutOfRangeException(nameof(limit), "A part must hold at least 100 characters.");
        var pieces = script.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(p => p.Length <= limit ? [p] : Sentences(p, limit)).ToList();
        var chunks = new List<string>();
        var current = "";
        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 2 + piece.Length > limit)
            {
                chunks.Add(current);
                current = "";
            }
            current = current.Length == 0 ? piece : current + "\n\n" + piece;
        }
        if (current.Length > 0) chunks.Add(current);
        return chunks;
    }

    private static IEnumerable<string> Sentences(string paragraph, int limit)
    {
        var current = "";
        foreach (var sentence in SentenceEnd().Split(paragraph).Where(s => s.Length > 0))
        {
            // A sentence longer than a whole part (none are, in practice) is cut at a space.
            var rest = sentence;
            while (rest.Length > limit)
            {
                var cut = rest.LastIndexOf(' ', limit - 1) is var space and > 0 ? space : limit;
                if (current.Length > 0) { yield return current; current = ""; }
                yield return rest[..cut];
                rest = rest[cut..].TrimStart();
            }
            if (current.Length > 0 && current.Length + 1 + rest.Length > limit) { yield return current; current = ""; }
            current = current.Length == 0 ? rest : current + " " + rest;
        }
        if (current.Length > 0) yield return current;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    /// <summary>Joins MP3 parts into one stream, dropping an ID3 tag from the start of every part but the first.</summary>
    public static byte[] Join(IReadOnlyList<byte[]> parts)
    {
        using var joined = new MemoryStream();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var skip = i > 0 && part.Length >= 10 && part[0] == 'I' && part[1] == 'D' && part[2] == '3'
                ? 10 + (part[6] << 21 | part[7] << 14 | part[8] << 7 | part[9])
                : 0;
            joined.Write(part, Math.Min(skip, part.Length), part.Length - Math.Min(skip, part.Length));
        }
        return joined.ToArray();
    }

    /// <summary>Seconds of audio in a constant-bit-rate MP3 of <paramref name="bytes"/> at <paramref name="kbps"/>.</summary>
    public static int Seconds(long bytes, int kbps) => (int)Math.Round(bytes * 8.0 / (kbps * 1000));

    /// <summary>The bit rate an output format names (<c>mp3_44100_128</c> → 128).</summary>
    public static int Kbps(string outputFormat) =>
        outputFormat.Split('_') is ["mp3", _, var rate] && int.TryParse(rate, out var kbps) ? kbps
            : throw new FormatException($"Only MP3 output formats are used (got '{outputFormat}').");

    /// <summary>The audio of a whole script: each part spoken with its neighbours as context.</summary>
    public static async Task<byte[]> SpeakAsync(ISpeech speech, string script, BroadcastVoice voice, IReadOnlyList<DictionaryRef> dictionaries,
        CancellationToken ct = default) => (await SpeakSectionsAsync(speech, [script], voice, dictionaries, ct).ConfigureAwait(false)).Audio;

    /// <summary>
    /// As <see cref="SpeakSectionsAsync"/>, with the subtitles: each part is spoken with its timing, and its cues are placed at
    /// the part's exact start in the joined audio (its bytes before it, at the constant bit rate). Parts the service did not
    /// time have no cues.
    /// </summary>
    public static async Task<(byte[] Audio, IReadOnlyList<long> SectionBytes, IReadOnlyList<Cue> Cues)> SpeakTimedAsync(ISpeech speech,
        IReadOnlyList<string> sections, BroadcastVoice voice, IReadOnlyList<DictionaryRef> dictionaries, CancellationToken ct = default)
    {
        var chunks = sections.SelectMany((text, section) => Chunks(text, voice.MaxCharactersPerRequest).Select(chunk => (section, chunk))).ToList();
        var parts = new List<SpokenPart>();
        for (var i = 0; i < chunks.Count; i++)
            parts.Add(await speech.SpeakTimedAsync(new SpeechRequest(voice.VoiceId, voice.Model, chunks[i].chunk, i > 0 ? chunks[i - 1].chunk : null,
                i + 1 < chunks.Count ? chunks[i + 1].chunk : null, dictionaries, voice.OutputFormat), ct).ConfigureAwait(false));
        var audio = Join(parts.Select(p => p.Audio).ToList());
        var kbps = Kbps(voice.OutputFormat);
        var bytes = new long[sections.Count];
        var cues = new List<Cue>();
        // The sound before each part, in bytes: the joined parts before it, less the first part's ID3 tag (not sound).
        var tag = parts.Count > 0 ? parts[0].Audio.Length - Join([[], parts[0].Audio]).Length : 0;
        long before = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var joined = i == 0 ? parts[i].Audio.Length : Join([[], parts[i].Audio]).Length;
            if (parts[i].Alignment is { } alignment) cues.AddRange(Subtitles.Cues(alignment, Math.Max(0, before - tag) * 8.0 / (kbps * 1000)));
            bytes[chunks[i].section] += joined;
            before += joined;
        }
        return (audio, bytes, cues);
    }

    /// <summary>
    /// The audio of an episode section by section (sub-project 7 §6): each section is spoken in parts of its own, so it
    /// starts on a fresh request, but every part hears the text around it (across sections too) for a continuous read.
    /// Returns the joined MP3 and each section's share of it in bytes, from which the chapters are timed.
    /// </summary>
    public static async Task<(byte[] Audio, IReadOnlyList<long> SectionBytes)> SpeakSectionsAsync(ISpeech speech, IReadOnlyList<string> sections,
        BroadcastVoice voice, IReadOnlyList<DictionaryRef> dictionaries, CancellationToken ct = default)
    {
        var chunks = sections.SelectMany((text, section) => Chunks(text, voice.MaxCharactersPerRequest).Select(chunk => (section, chunk))).ToList();
        var parts = new List<byte[]>();
        for (var i = 0; i < chunks.Count; i++)
            parts.Add(await speech.SpeakAsync(new SpeechRequest(voice.VoiceId, voice.Model, chunks[i].chunk, i > 0 ? chunks[i - 1].chunk : null,
                i + 1 < chunks.Count ? chunks[i + 1].chunk : null, dictionaries, voice.OutputFormat), ct).ConfigureAwait(false));
        var audio = Join(parts);
        // Each part's bytes once joined: a part after the first loses its ID3 tag, as Join drops it.
        var bytes = new long[sections.Count];
        for (var i = 0; i < parts.Count; i++) bytes[chunks[i].section] += i == 0 ? parts[i].Length : Join([[], parts[i]]).Length;
        return (audio, bytes);
    }
}

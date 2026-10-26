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
        CancellationToken ct = default)
    {
        var chunks = Chunks(script, voice.MaxCharactersPerRequest);
        var parts = new List<byte[]>();
        for (var i = 0; i < chunks.Count; i++)
            parts.Add(await speech.SpeakAsync(new SpeechRequest(voice.VoiceId, voice.Model, chunks[i], i > 0 ? chunks[i - 1] : null,
                i + 1 < chunks.Count ? chunks[i + 1] : null, dictionaries, voice.OutputFormat), ct).ConfigureAwait(false));
        return Join(parts);
    }
}

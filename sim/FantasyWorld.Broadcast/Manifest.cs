using System.Text;
using System.Text.Json;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>What one broadcast date has done: each step is marked the moment it finishes, so a rerun skips it.</summary>
public sealed class ManifestDay
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
    public int Events { get; set; }
    public string? Headline { get; set; }
    public SortedDictionary<string, DateTimeOffset> Steps { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The narration's size and length, for the feed (M3).</summary>
    public long AudioBytes { get; set; }
    public int AudioSeconds { get; set; }

    public bool Done(string step) => Steps.ContainsKey(step);
}

/// <summary>
/// <c>manifest.json</c>: per broadcast date, which steps finished and when. The rule that keeps reruns safe: a channel
/// is posted to only when its step is not marked, and the mark is written back straight after.
/// </summary>
public sealed class Manifest
{
    public const string Key = "manifest.json";
    public const string EpisodeStep = "episode";
    public const string DiscordStep = "discord";
    public const string AudioStep = "audio";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, ManifestDay> Episodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How each invented name is said (word → respelling; a word said as written maps to itself). Never changed once set.</summary>
    public SortedDictionary<string, string> Pronunciations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Words asked about without a usable answer, and how often; after a few tries one is said as written.</summary>
    public SortedDictionary<string, int> PronunciationTries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The ElevenLabs dictionary holding the respellings, its current version, and the words already in it.</summary>
    public string? DictionaryId { get; set; }
    public string? DictionaryVersionId { get; set; }
    public SortedSet<string> DictionaryWords { get; set; } = new(StringComparer.Ordinal);

    public ManifestDay Day(string date) => Episodes.TryGetValue(date, out var day) ? day : Episodes[date] = new ManifestDay();

    public void Record(string date, Episode episode)
    {
        var day = Day(date);
        (day.FromYear, day.ToYear, day.Events, day.Headline) = (episode.FromYear, episode.ToYear, episode.EventCount, episode.Headline?.Text);
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static Manifest FromBytes(byte[]? bytes) =>
        bytes is null ? new Manifest() : JsonSerializer.Deserialize<Manifest>(bytes, Json) ?? new Manifest();
}

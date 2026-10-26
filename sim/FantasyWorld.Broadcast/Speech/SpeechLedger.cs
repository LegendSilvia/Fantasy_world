using System.Text;
using System.Text.Json;

namespace FantasyWorld.Broadcast.Speech;

/// <summary>
/// Characters sent to the speech service, by calendar month (yyyy-MM), at the bucket's root: live and dry runs share
/// it, so the monthly budget holds the whole account. Characters are counted before each request, so a run that stops
/// mid-request has still counted what it may have been billed.
/// </summary>
public sealed class SpeechLedger
{
    public const string Key = "speech-ledger.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SortedDictionary<string, long> Characters { get; set; } = new(StringComparer.Ordinal);

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    public static SpeechLedger FromBytes(byte[]? bytes) =>
        bytes is null ? new SpeechLedger() : JsonSerializer.Deserialize<SpeechLedger>(bytes, Json) ?? new SpeechLedger();
}

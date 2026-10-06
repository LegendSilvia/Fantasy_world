using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Large JSON documents (the snapshot, the geography) are stored gzip-compressed (spec §9 "compressed JSON");
/// files written before compression hold plain text, which still loads.
/// </summary>
public static class Packed
{
    public static byte[] Pack<T>(T value)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            JsonSerializer.Serialize(gzip, value, SimJson.Options);
        return buffer.ToArray();
    }

    public static T? Unpack<T>(object stored)
    {
        switch (stored)
        {
            case byte[] bytes:
                using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
                    return JsonSerializer.Deserialize<T>(gzip, SimJson.Options);
            case string json:
                return JsonSerializer.Deserialize<T>(json, SimJson.Options);
            default:
                throw new InvalidOperationException($"Unexpected stored value of type {stored.GetType().Name}.");
        }
    }
}

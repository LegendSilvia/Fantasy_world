using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FantasyWorld.Broadcast.Media;

/// <summary>
/// A minimal PNG encoder (8-bit RGB, no interlace): signature, IHDR, one zlib-compressed IDAT, IEND. Enough for the
/// broadcast's map image without an imaging package.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a];

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Encodes <paramref name="rgb"/> (three bytes a pixel, rows top to bottom).</summary>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        if (width <= 0 || height <= 0 || rgb.Length != width * height * 3)
            throw new ArgumentException($"Expected {width}×{height} RGB pixels.");
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // filter: none
                zlib.Write(rgb, y * width * 3, width * 3);
            }

        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    public static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = 0xffffffffu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffffu;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        png.Write(number);
    }
}

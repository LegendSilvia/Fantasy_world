using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Client;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class MapImageTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    private static IEnumerable<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            yield return (type, png[(at + 8)..(at + 8 + length)], BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)));
            at += 12 + length;
        }
    }

    [Fact]
    public void Png_IsAValidRgbImage_ThatHoldsThePixels()
    {
        var rgb = Enumerable.Range(0, 3 * 2 * 3).Select(i => (byte)(i * 10)).ToArray();
        var png = Png.Encode(3, 2, rgb);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, png[..8]);
        var chunks = Chunks(png).ToList();
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(c => c.Type));
        Assert.All(chunks, c => Assert.Equal(Png.Crc(Encoding.ASCII.GetBytes(c.Type).Concat(c.Data).ToArray()), c.Crc));
        var header = chunks[0].Data;
        Assert.Equal((3, 2, 8, 2), (BinaryPrimitives.ReadInt32BigEndian(header), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)), (int)header[8], (int)header[9]));

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress)) zlib.CopyTo(inflated);
        var rows = inflated.ToArray();
        Assert.Equal(new byte[] { 0 }.Concat(rgb[..9]).Concat(new byte[] { 0 }).Concat(rgb[9..]), rows);
        Assert.Equal(0xcbf43926u, Png.Crc("123456789"u8)); // the standard CRC-32 check value
        Assert.Throws<ArgumentException>(() => Png.Encode(3, 2, new byte[5]));
    }

    [Fact]
    public void TheMap_ShowsSeaTownsAndRealms_TheSameEveryTime()
    {
        var raster = MapPainter.Paint(Aged.World, Aged.Log);
        var terrain = Aged.World.Geography!.Terrain;
        var scale = MapPainter.DefaultWidth / terrain.Width;
        Assert.Equal((terrain.Width * scale, terrain.Height * scale), (raster.Width, raster.Height));
        Assert.Equal(raster.Rgb, MapPainter.Paint(Aged.World, Aged.Log).Rgb);

        // A deep-water tile reads as sea: more blue than red.
        var sea = Enumerable.Range(0, terrain.Count).First(t => !terrain.IsLand(t));
        var (r, _, b) = raster.At(sea % terrain.Width * scale + scale / 2, sea / terrain.Width * scale + scale / 2);
        Assert.True(b > r);

        // A town is an ink dot on the map.
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var town = map.Settlements.First(s => s.Position is not null);
        Assert.Equal((Byte(Palette.Ink.X), Byte(Palette.Ink.Y), Byte(Palette.Ink.Z)),
            raster.At((int)(town.Position!.X * raster.Width), (int)(town.Position.Y * raster.Height)));

        // The map has realms to wash, and its PNG is the painted raster.
        Assert.Contains(map.Regions, r => r.Owner is not null);
        var png = MapPainter.Png(Aged.World, Aged.Log);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
}

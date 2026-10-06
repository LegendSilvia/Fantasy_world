using System.Text.Json.Serialization;

namespace FantasyWorld.Sim.Geo;

/// <summary>
/// The tile layer: one byte per tile per channel, row-major (index = y * Width + x).
/// Static after world generation; rendered by the Godot client.
/// </summary>
public sealed class TerrainGrid
{
    private static readonly (int Dx, int Dy)[] Offsets4 = { (0, -1), (-1, 0), (1, 0), (0, 1) };
    private static readonly (int Dx, int Dy)[] Offsets8 =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    public TerrainGrid(int width, int height)
        : this(width, height, 0, new byte[width * height], new byte[width * height],
            new byte[width * height], new byte[width * height], new byte[width * height])
    {
    }

    [JsonConstructor]
    public TerrainGrid(int width, int height, byte seaLevel, byte[] elevation, byte[] temperature,
        byte[] moisture, byte[] biomes, byte[] flags)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Grid must not be empty.");
        var n = width * height;
        foreach (var channel in new[] { elevation, temperature, moisture, biomes, flags })
            if (channel.Length != n) throw new ArgumentException($"Every channel must have {n} tiles.");
        Width = width;
        Height = height;
        SeaLevel = seaLevel;
        Elevation = elevation;
        Temperature = temperature;
        Moisture = moisture;
        Biomes = biomes;
        Flags = flags;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Highest elevation that is water at generation time.</summary>
    public byte SeaLevel { get; set; }

    public byte[] Elevation { get; }
    public byte[] Temperature { get; }
    public byte[] Moisture { get; }
    public byte[] Biomes { get; }
    public byte[] Flags { get; }

    [JsonIgnore] public int Count => Width * Height;

    public int Index(int x, int y) => y * Width + x;
    public TileCoord Coord(int index) => new(index % Width, index / Width);
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Biome BiomeAt(int index) => (Biome)Biomes[index];
    public void SetBiome(int index, Biome biome) => Biomes[index] = (byte)biome;
    public bool IsLand(int index) => Biomes[index] != (byte)Biome.Ocean;

    public bool Has(int index, TileFlags flag) => ((TileFlags)Flags[index] & flag) != 0;
    public void Set(int index, TileFlags flag) => Flags[index] |= (byte)flag;

    public IEnumerable<int> Neighbours4(int index) => Neighbours(index, Offsets4);
    public IEnumerable<int> Neighbours8(int index) => Neighbours(index, Offsets8);

    private IEnumerable<int> Neighbours(int index, (int Dx, int Dy)[] offsets)
    {
        var x = index % Width;
        var y = index / Width;
        foreach (var (dx, dy) in offsets)
            if (InBounds(x + dx, y + dy))
                yield return Index(x + dx, y + dy);
    }
}

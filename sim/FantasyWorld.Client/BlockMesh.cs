using System.Numerics;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Client;

/// <summary>Plain mesh arrays (positions, normals, colours, triangle indices) a renderer copies into its own mesh type.</summary>
public sealed record MeshData(Vector3[] Positions, Vector3[] Normals, Vector3[] Colors, int[] Indices)
{
    public int Triangles => Indices.Length / 3;
}

/// <summary>
/// The diorama's floating world block (sub-project 4 §3): the terrain grid as a heightfield coloured by biome, a flat
/// water plane at sea level, and four side walls cut through grass, soil and rock like the design's isometric block.
/// The block spans x and z in [-1, 1]; sea level is y = 0.
/// </summary>
public static class BlockMesh
{
    public const float HeightScale = 0.22f;
    public const float SeaFloor = -0.04f;
    public const float BaseDepth = 0.32f;

    private static readonly Dictionary<Biome, Vector3> BiomeColours = new()
    {
        [Biome.Ocean] = Hex(0x6fa9b8), [Biome.Tundra] = Hex(0xe4e8e1), [Biome.Taiga] = Hex(0x7f9f7a), [Biome.Grassland] = Hex(0xb7d39a),
        [Biome.Forest] = Hex(0x6b9655), [Biome.Swamp] = Hex(0x8a9a6b), [Biome.Desert] = Hex(0xe3cf98), [Biome.Savanna] = Hex(0xcdbf7f),
        [Biome.Jungle] = Hex(0x5e8a4a), [Biome.Hills] = Hex(0xa7b48a), [Biome.Mountain] = Hex(0x9aa096),
    };

    public static readonly Vector3 River = Hex(0x7fc4d4);
    public static readonly Vector3 Water = Hex(0x7fc4d4);
    public static readonly (float Depth, Vector3 Colour)[] Strata = { (0.02f, Hex(0x7f9f5e)), (0.14f, Hex(0xbf9468)), (BaseDepth, Hex(0x8c7765)) };

    public static Vector3 Hex(int rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);

    public static Vector3 ColourOf(Biome biome) => BiomeColours[biome];

    /// <summary>Height of the surface at map position (u, v) in [0, 1]: land above sea level, sea floor below it.</summary>
    public static float HeightAt(TerrainGrid grid, double u, double v)
    {
        var x = (int)Math.Clamp(Math.Round(u * (grid.Width - 1)), 0, grid.Width - 1);
        var y = (int)Math.Clamp(Math.Round(v * (grid.Height - 1)), 0, grid.Height - 1);
        return TileHeight(grid, grid.Index(x, y));
    }

    public static float TileHeight(TerrainGrid grid, int index)
    {
        if (!grid.IsLand(index)) return SeaFloor;
        var above = Math.Max(0, grid.Elevation[index] - grid.SeaLevel) / (float)Math.Max(1, 255 - grid.SeaLevel);
        return 0.004f + above * HeightScale;
    }

    /// <summary>The top surface, sampled on a resolution × resolution grid of quads.</summary>
    public static MeshData Top(TerrainGrid grid, int resolution = 128)
    {
        var n = resolution + 1;
        var positions = new Vector3[n * n];
        var colours = new Vector3[n * n];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var (u, v) = (i / (double)resolution, j / (double)resolution);
                var tx = (int)Math.Clamp(Math.Round(u * (grid.Width - 1)), 0, grid.Width - 1);
                var ty = (int)Math.Clamp(Math.Round(v * (grid.Height - 1)), 0, grid.Height - 1);
                var index = grid.Index(tx, ty);
                positions[j * n + i] = new Vector3((float)(u * 2 - 1), TileHeight(grid, index), (float)(v * 2 - 1));
                colours[j * n + i] = grid.Has(index, TileFlags.River) && grid.IsLand(index) ? River : ColourOf(grid.BiomeAt(index));
            }
        var indices = new List<int>(resolution * resolution * 6);
        for (var j = 0; j < resolution; j++)
            for (var i = 0; i < resolution; i++)
            {
                var a = j * n + i;
                indices.AddRange(new[] { a, a + n, a + 1, a + 1, a + n, a + n + 1 });
            }
        var normals = new Vector3[n * n];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var l = positions[j * n + Math.Max(0, i - 1)];
                var r = positions[j * n + Math.Min(n - 1, i + 1)];
                var d = positions[Math.Max(0, j - 1) * n + i];
                var u = positions[Math.Min(n - 1, j + 1) * n + i];
                normals[j * n + i] = Vector3.Normalize(Vector3.Cross(u - d, r - l));
            }
        return new MeshData(positions, normals, colours, indices.ToArray());
    }

    /// <summary>The flat sea, covering the whole block just above the sea floor.</summary>
    public static MeshData WaterPlane() =>
        new(new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) },
            Enumerable.Repeat(Vector3.UnitY, 4).ToArray(), Enumerable.Repeat(Water, 4).ToArray(), new[] { 0, 2, 1, 1, 2, 3 });

    /// <summary>The four walls, each a strip of strata bands following the surface edge down to the block's base.</summary>
    public static MeshData Sides(TerrainGrid grid, int resolution = 128)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Vector3>();
        var indices = new List<int>();
        var edges = new (Func<double, (double U, double V)> At, Vector3 Normal)[]
        {
            (t => (t, 1), Vector3.UnitZ), (t => (1 - t, 0), -Vector3.UnitZ), (t => (1, 1 - t), Vector3.UnitX), (t => (0, t), -Vector3.UnitX),
        };
        foreach (var (at, normal) in edges)
            for (var k = 0; k < resolution; k++)
            {
                var (u0, v0) = at(k / (double)resolution);
                var (u1, v1) = at((k + 1) / (double)resolution);
                var top0 = Math.Max(0, HeightAt(grid, u0, v0));
                var top1 = Math.Max(0, HeightAt(grid, u1, v1));
                var (prev0, prev1) = (top0, top1);
                foreach (var (depth, colour) in Strata)
                {
                    var (low0, low1) = (depth == BaseDepth ? -BaseDepth : top0 - depth, depth == BaseDepth ? -BaseDepth : top1 - depth);
                    var start = positions.Count;
                    positions.AddRange(new[]
                    {
                        new Vector3((float)(u0 * 2 - 1), prev0, (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), prev1, (float)(v1 * 2 - 1)),
                        new Vector3((float)(u0 * 2 - 1), low0, (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), low1, (float)(v1 * 2 - 1)),
                    });
                    normals.AddRange(Enumerable.Repeat(normal, 4));
                    colours.AddRange(Enumerable.Repeat(colour, 4));
                    indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                    (prev0, prev1) = (low0, low1);
                }
            }
        return new MeshData(positions.ToArray(), normals.ToArray(), colours.ToArray(), indices.ToArray());
    }
}

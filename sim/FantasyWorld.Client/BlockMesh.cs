using System.Numerics;
using System.Runtime.CompilerServices;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Client;

/// <summary>Plain mesh arrays (positions, normals, colours, triangle indices) a renderer copies into its own mesh type.</summary>
public sealed record MeshData(Vector3[] Positions, Vector3[] Normals, Vector3[] Colors, int[] Indices)
{
    public int Triangles => Indices.Length / 3;

    /// <summary>Per-vertex opacity (water); null means opaque.</summary>
    public float[]? Alphas { get; init; }

    /// <summary>Per-vertex texture coordinates (across, along) for shaders that animate along a line; null if none.</summary>
    public Vector2[]? Uvs { get; init; }
}

/// <summary>
/// The terrain as continuous fields over the map (u, v in [0, 1]): the tile grid's elevation, biome colours and rivers,
/// blurred so coastlines, slopes and biome edges are soft rather than tile-stepped, then sampled bilinearly. One field
/// per grid, cached, so the mesh, markers and pins all stand on the same surface.
/// </summary>
public sealed class TerrainField
{
    private static readonly ConditionalWeakTable<TerrainGrid, TerrainField> Cache = new();

    private readonly int _w;
    private readonly int _h;
    private readonly float[] _height;
    private readonly Vector3[] _colour;
    private readonly float[] _river;
    private readonly float[] _cold;
    private readonly float[] _relief;
    private readonly Noise2D _noise;

    private TerrainField(TerrainGrid grid)
    {
        (_w, _h) = (grid.Width, grid.Height);
        var n = grid.Count;
        _height = new float[n];
        _colour = new Vector3[n];
        _river = new float[n];
        _cold = new float[n];
        _relief = new float[n];
        _noise = new Noise2D(grid.Elevation.Aggregate(17UL, (h, e) => h * 31 + e));
        for (var i = 0; i < n; i++)
        {
            var e = grid.Elevation[i];
            var land = grid.IsLand(i);
            if (land)
            {
                var above = Math.Max(0, e - grid.SeaLevel) / (float)Math.Max(1, 255 - grid.SeaLevel);
                _height[i] = BlockMesh.Shore + MathF.Pow(above, 1.35f) * BlockMesh.HeightScale;
            }
            else
            {
                var below = Math.Max(0, grid.SeaLevel - e) / (float)Math.Max(1, (int)grid.SeaLevel);
                _height[i] = -BlockMesh.Shore - below * BlockMesh.SeaDepth;
            }
            _colour[i] = BlockMesh.ColourOf(grid.BiomeAt(i));
            _river[i] = land && grid.Has(i, TileFlags.River) ? 1 : 0;
            _cold[i] = 1 - grid.Temperature[i] / 255f;
            _relief[i] = grid.BiomeAt(i) switch { Biome.Mountain => 1f, Biome.Hills => 0.3f, _ => land ? 0.05f : 0 };
        }
        Blur(_height, 2);
        Blur(_colour, 2);
        Blur(_river, 1);
        Blur(_cold, 2);
        Blur(_relief, 2);
        // Rivers cut a shallow bed into the land they cross (never below the sea).
        for (var i = 0; i < n; i++)
            if (_height[i] > 0) _height[i] = Math.Max(_height[i] * 0.35f, _height[i] - _river[i] * BlockMesh.RiverBed);
    }

    public static TerrainField For(TerrainGrid grid) => Cache.GetValue(grid, g => new TerrainField(g));

    /// <summary>
    /// The surface height: the smoothed elevation plus ridged relief where the map says mountains or hills (visual only;
    /// the simulation's elevation is untouched), so ranges read as crags and peaks rather than smooth mounds.
    /// </summary>
    public float Height(double u, double v)
    {
        var baseHeight = Sample(_height, u, v);
        var relief = Sample(_relief, u, v);
        if (relief <= 0.001f || baseHeight <= 0) return baseHeight;
        return baseHeight + relief * BlockMesh.Relief * Ridged(u * 10, v * 10) * Math.Min(1, baseHeight / (BlockMesh.Shore * 4));
    }

    /// <summary>Ridged fractal noise in [0, 1]: sharp crests where value noise crosses its middle.</summary>
    private float Ridged(double x, double y)
    {
        double sum = 0, amplitude = 1, norm = 0, frequency = 1;
        for (var octave = 0; octave < 4; octave++)
        {
            var r = 1 - Math.Abs(2 * _noise.Value(x * frequency, y * frequency, octave) - 1);
            sum += amplitude * r * r;
            norm += amplitude;
            amplitude *= 0.5;
            frequency *= 2.1;
        }
        return (float)(sum / norm);
    }
    public Vector3 BaseColour(double u, double v) => Sample(_colour, u, v);

    /// <summary>Paint-like mottling in [-1, 1] at two scales: meadows and copses up close, never tile-shaped.</summary>
    public float Detail(double u, double v) =>
        (float)(_noise.Fractal(u * 48 + 311, v * 48 + 173, 3) * 1.4 + _noise.Fractal(u * 160 + 71, v * 160 + 29, 2) * 0.6 - 1);
    public float River(double u, double v) => Sample(_river, u, v);
    public float Cold(double u, double v) => Sample(_cold, u, v);

    private float Sample(float[] field, double u, double v)
    {
        var (i, j, fx, fy) = Cell(u, v);
        float At(int x, int y) => field[Math.Clamp(y, 0, _h - 1) * _w + Math.Clamp(x, 0, _w - 1)];
        return Lerp(Lerp(At(i, j), At(i + 1, j), fx), Lerp(At(i, j + 1), At(i + 1, j + 1), fx), fy);
    }

    private Vector3 Sample(Vector3[] field, double u, double v)
    {
        var (i, j, fx, fy) = Cell(u, v);
        Vector3 At(int x, int y) => field[Math.Clamp(y, 0, _h - 1) * _w + Math.Clamp(x, 0, _w - 1)];
        return Vector3.Lerp(Vector3.Lerp(At(i, j), At(i + 1, j), fx), Vector3.Lerp(At(i, j + 1), At(i + 1, j + 1), fx), fy);
    }

    /// <summary>Tile centres sit at (x + 0.5) / width, so the field interpolates between centres.</summary>
    private (int I, int J, float Fx, float Fy) Cell(double u, double v)
    {
        var x = Math.Clamp(u, 0, 1) * _w - 0.5;
        var y = Math.Clamp(v, 0, 1) * _h - 0.5;
        var i = (int)Math.Floor(x);
        var j = (int)Math.Floor(y);
        return (i, j, (float)(x - i), (float)(y - j));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private void Blur(float[] field, int passes)
    {
        var tmp = new float[field.Length];
        for (var p = 0; p < passes; p++)
        {
            for (var y = 0; y < _h; y++)
                for (var x = 0; x < _w; x++)
                    tmp[y * _w + x] = 0.25f * field[y * _w + Math.Max(0, x - 1)] + 0.5f * field[y * _w + x] + 0.25f * field[y * _w + Math.Min(_w - 1, x + 1)];
            for (var y = 0; y < _h; y++)
                for (var x = 0; x < _w; x++)
                    field[y * _w + x] = 0.25f * tmp[Math.Max(0, y - 1) * _w + x] + 0.5f * tmp[y * _w + x] + 0.25f * tmp[Math.Min(_h - 1, y + 1) * _w + x];
        }
    }

    private void Blur(Vector3[] field, int passes)
    {
        var tmp = new Vector3[field.Length];
        for (var p = 0; p < passes; p++)
        {
            for (var y = 0; y < _h; y++)
                for (var x = 0; x < _w; x++)
                    tmp[y * _w + x] = 0.25f * field[y * _w + Math.Max(0, x - 1)] + 0.5f * field[y * _w + x] + 0.25f * field[y * _w + Math.Min(_w - 1, x + 1)];
            for (var y = 0; y < _h; y++)
                for (var x = 0; x < _w; x++)
                    field[y * _w + x] = 0.25f * tmp[Math.Max(0, y - 1) * _w + x] + 0.5f * tmp[y * _w + x] + 0.25f * tmp[Math.Min(_h - 1, y + 1) * _w + x];
        }
    }
}

/// <summary>
/// The diorama's floating world block (sub-project 4 §3): a smooth heightfield shaded like a painted relief model
/// (biomes blended, sandy shores, rock on steep slopes, snow on cold peaks, rivers as soft ribbons), a sea whose
/// colour and opacity follow its depth, and four walls cut through topsoil, clay and bedrock. The block spans x and z
/// in [-1, 1]; sea level is y = 0.
/// </summary>
public static class BlockMesh
{
    public const float HeightScale = 0.17f;
    public const float Shore = 0.006f;
    public const float SeaDepth = 0.06f;
    public const float RiverBed = 0.006f;
    public const float Relief = 0.11f;
    public const float BaseDepth = 0.3f;

    /// <summary>A muted, painted palette: soft greens and sands that sit quietly on the design's warm grey.</summary>
    private static readonly Dictionary<Biome, Vector3> BiomeColours = new()
    {
        [Biome.Ocean] = Hex(0xd9d2b4),
        [Biome.Tundra] = Hex(0xb9bfae),
        [Biome.Taiga] = Hex(0x7d9677),
        [Biome.Grassland] = Hex(0xa9ba86),
        [Biome.Forest] = Hex(0x7f9e62),
        [Biome.Swamp] = Hex(0x8b9870),
        [Biome.Desert] = Hex(0xd8c9a0),
        [Biome.Savanna] = Hex(0xc2bb8c),
        [Biome.Jungle] = Hex(0x6f9458),
        [Biome.Hills] = Hex(0xa3ad7e),
        [Biome.Mountain] = Hex(0xa09a8c),
    };

    public static readonly Vector3 Sand = Hex(0xe3d9bd);
    public static readonly Vector3 Rock = Hex(0x958f84);
    public static readonly Vector3 Snow = Hex(0xf4f3ee);
    public static readonly Vector3 River = Hex(0x7fb7c4);
    public static readonly Vector3 Shallows = Hex(0xb9ddd5);
    public static readonly Vector3 Deep = Hex(0x6a9eab);
    public static readonly Vector3 SeaFloor = Hex(0x8fb0a8);

    /// <summary>The walls' bands from the surface down: topsoil, clay, bedrock (base of the block).</summary>
    public static readonly (float Depth, Vector3 Colour)[] Strata = { (0.018f, Hex(0x8c9e6c)), (0.12f, Hex(0xc2a888)), (BaseDepth, Hex(0xa59584)) };

    public static Vector3 Hex(int rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);

    public static Vector3 ColourOf(Biome biome) => BiomeColours[biome];

    /// <summary>Height of the smoothed surface at map position (u, v) in [0, 1]: land above sea level, sea floor below.</summary>
    public static float HeightAt(TerrainGrid grid, double u, double v) => TerrainField.For(grid).Height(u, v);

    /// <summary>The top surface, sampled on a resolution × resolution grid of quads.</summary>
    public static MeshData Top(TerrainGrid grid, int resolution = 192)
    {
        var field = TerrainField.For(grid);
        var n = resolution + 1;
        var positions = new Vector3[n * n];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var (u, v) = (i / (double)resolution, j / (double)resolution);
                positions[j * n + i] = new Vector3((float)(u * 2 - 1), field.Height(u, v), (float)(v * 2 - 1));
            }
        var normals = Normals(positions, n);
        var colours = new Vector3[n * n];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var (u, v) = (i / (double)resolution, j / (double)resolution);
                colours[j * n + i] = Shade(field, u, v, positions[j * n + i].Y, normals[j * n + i], i, j);
            }
        return new MeshData(positions, normals, colours, Grid(resolution));
    }

    /// <summary>The colour of the surface at a point: biome base, then shore, slope, snow and river, with a little grain.</summary>
    private static Vector3 Shade(TerrainField field, double u, double v, float height, Vector3 normal, int i, int j)
    {
        Vector3 colour;
        if (height <= 0)
        {
            // Under water: pale sand in the shallows darkening to sea floor in the deep (the water tints it further).
            colour = Vector3.Lerp(Sand, SeaFloor, Smooth(0, SeaDepth * 0.6f, -height));
        }
        else
        {
            colour = field.BaseColour(u, v);
            colour = Vector3.Lerp(colour, Sand, 1 - Smooth(0, Shore * 2.2f, height));
            colour = Vector3.Lerp(colour, Rock, Smooth(0.35f, 0.7f, 1 - normal.Y) * 0.85f);
            var alpine = Smooth(HeightScale * 0.4f, HeightScale * 0.75f, height);
            colour = Vector3.Lerp(colour, Rock, alpine * 0.6f);
            // Snow only on the highest, flattest ground, more of it where the climate is cold.
            var snowLine = HeightScale * (0.85f - 0.3f * field.Cold(u, v));
            colour = Vector3.Lerp(colour, Snow, Smooth(snowLine, snowLine + HeightScale * 0.08f, height) * Smooth(0.3f, 0.08f, 1 - normal.Y));
            colour = Vector3.Lerp(colour, River, Smooth(0.25f, 0.6f, field.River(u, v)));
        }
        var detail = field.Detail(u, v);
        // Land is mottled (lighter meadows, darker copses); the sea floor gets a gentler ripple.
        var grain = 1 + detail * (height > 0 ? 0.07f : 0.03f) + (Hash(i, j) - 0.5f) * 0.03f;
        return Vector3.Clamp(colour * grain, Vector3.Zero, Vector3.One);
    }

    /// <summary>The sea: a sheet at sea level whose colour deepens and opacity rises with the depth below it; clear over land.</summary>
    public static MeshData Water(TerrainGrid grid, int resolution = 96)
    {
        var field = TerrainField.For(grid);
        var n = resolution + 1;
        var positions = new List<Vector3>(n * n);
        var colours = new List<Vector3>(n * n);
        var alphas = new List<float>(n * n);
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var (u, v) = (i / (double)resolution, j / (double)resolution);
                var depth = -field.Height(u, v);
                var t = Smooth(0, SeaDepth * 0.7f, depth);
                positions.Add(new Vector3((float)(u * 2 - 1), 0, (float)(v * 2 - 1)));
                colours.Add(Vector3.Lerp(Shallows, Deep, t));
                alphas.Add(depth <= 0 ? 0 : 0.35f + 0.55f * t);
            }
        var indices = new List<int>(Grid(resolution));
        var normals = Enumerable.Repeat(Vector3.UnitY, positions.Count).ToList();
        // The water's own walls where the sea meets the block's edge, from the surface down to the sea floor.
        foreach (var (at, normal) in Edges)
            for (var k = 0; k < resolution; k++)
            {
                var (u0, v0) = at(k / (double)resolution);
                var (u1, v1) = at((k + 1) / (double)resolution);
                var (f0, f1) = (field.Height(u0, v0), field.Height(u1, v1));
                if (f0 >= 0 && f1 >= 0) continue;
                var start = positions.Count;
                positions.AddRange(new[]
                {
                    new Vector3((float)(u0 * 2 - 1), 0, (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), 0, (float)(v1 * 2 - 1)),
                    new Vector3((float)(u0 * 2 - 1), Math.Min(0, f0), (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), Math.Min(0, f1), (float)(v1 * 2 - 1)),
                });
                normals.AddRange(Enumerable.Repeat(normal, 4));
                colours.AddRange(new[] { Shallows, Shallows, Deep, Deep });
                alphas.AddRange(new[] { 0.55f, 0.55f, 0.85f, 0.85f });
                indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
        return new MeshData(positions.ToArray(), normals.ToArray(), colours.ToArray(), indices.ToArray()) { Alphas = alphas.ToArray() };
    }

    /// <summary>The four walls, each a strip of strata bands following the surface edge down to the block's base.</summary>
    public static MeshData Sides(TerrainGrid grid, int resolution = 192)
    {
        var field = TerrainField.For(grid);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Vector3>();
        var indices = new List<int>();
        foreach (var (at, normal) in Edges)
            for (var k = 0; k < resolution; k++)
            {
                var (u0, v0) = at(k / (double)resolution);
                var (u1, v1) = at((k + 1) / (double)resolution);
                var top0 = field.Height(u0, v0);
                var top1 = field.Height(u1, v1);
                var (prev0, prev1) = (top0, top1);
                foreach (var (depth, colour) in Strata)
                {
                    var bottom = depth == BaseDepth;
                    var (low0, low1) = (bottom ? -BaseDepth : Math.Max(-BaseDepth, top0 - depth), bottom ? -BaseDepth : Math.Max(-BaseDepth, top1 - depth));
                    // Under the sea the topsoil is sand; the bedrock darkens toward the base so the block reads as solid.
                    var band = depth == Strata[0].Depth && Math.Min(top0, top1) <= 0 ? Sand : colour;
                    var lower = bottom ? band * 0.78f : band * 0.94f;
                    var start = positions.Count;
                    positions.AddRange(new[]
                    {
                        new Vector3((float)(u0 * 2 - 1), prev0, (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), prev1, (float)(v1 * 2 - 1)),
                        new Vector3((float)(u0 * 2 - 1), low0, (float)(v0 * 2 - 1)), new Vector3((float)(u1 * 2 - 1), low1, (float)(v1 * 2 - 1)),
                    });
                    normals.AddRange(Enumerable.Repeat(normal, 4));
                    colours.AddRange(new[] { band, band, lower, lower });
                    indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                    (prev0, prev1) = (low0, low1);
                }
            }
        return new MeshData(positions.ToArray(), normals.ToArray(), colours.ToArray(), indices.ToArray());
    }

    private static readonly (Func<double, (double U, double V)> At, Vector3 Normal)[] Edges =
    {
        (t => (t, 1), Vector3.UnitZ), (t => (1 - t, 0), -Vector3.UnitZ), (t => (1, 1 - t), Vector3.UnitX), (t => (0, t), -Vector3.UnitX),
    };

    private static int[] Grid(int resolution)
    {
        var n = resolution + 1;
        var indices = new int[resolution * resolution * 6];
        var k = 0;
        for (var j = 0; j < resolution; j++)
            for (var i = 0; i < resolution; i++)
            {
                var a = j * n + i;
                (indices[k], indices[k + 1], indices[k + 2], indices[k + 3], indices[k + 4], indices[k + 5]) = (a, a + n, a + 1, a + 1, a + n, a + n + 1);
                k += 6;
            }
        return indices;
    }

    private static Vector3[] Normals(Vector3[] positions, int n)
    {
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
        return normals;
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A stable 0–1 value per grid point, for paint-like grain.</summary>
    public static float Hash(int i, int j)
    {
        var h = (uint)(i * 374761393 + j * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }
}

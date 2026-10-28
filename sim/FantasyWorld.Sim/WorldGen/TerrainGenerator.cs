using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.WorldGen;

public sealed record TerrainOptions(
    int Width = 256,
    int Height = 256,
    double LandFraction = 0.55,
    double MountainFraction = 0.08,
    double HillFraction = 0.14,
    int MinIslandTiles = 150,
    int Continents = 0);

/// <summary>
/// Seed → elevation, temperature, moisture and biome for every tile. A floating block ringed by sea: one landmass, or
/// with <see cref="TerrainOptions.Continents"/> that many continents parted by open sea (sub-project 13).
/// </summary>
public static class TerrainGenerator
{
    public const byte SeaLevel = 100;
    private const int BorderWidth = 2;

    public static TerrainGrid Generate(ulong seed, TerrainOptions options)
    {
        var grid = new TerrainGrid(options.Width, options.Height) { SeaLevel = SeaLevel };
        var raw = RawElevation(grid, new Noise2D(StableHash.Derive(seed, "terrain/height")),
            options.Continents > 1 ? ContinentCentres(options.Continents, Rng.ForStream(seed, "terrain/continents", 0)) : null);
        var landLevel = Percentile(raw, 1 - options.LandFraction);
        WriteElevation(grid, raw, landLevel);
        SinkSmallIslands(grid, options.MinIslandTiles);

        var land = Enumerable.Range(0, grid.Count).Where(i => grid.Elevation[i] > SeaLevel).ToArray();
        var landElevations = land.Select(i => (double)grid.Elevation[i]).ToArray();
        var mountainLevel = Percentile(landElevations, 1 - options.MountainFraction);
        var hillLevel = Percentile(landElevations, 1 - options.MountainFraction - options.HillFraction);

        WriteTemperature(grid, new Noise2D(StableHash.Derive(seed, "terrain/temperature")));
        WriteMoisture(grid, new Noise2D(StableHash.Derive(seed, "terrain/moisture")));
        foreach (var i in land)
            grid.SetBiome(i, Classify(grid.Elevation[i], mountainLevel, hillLevel,
                grid.Temperature[i] / 255.0, grid.Moisture[i] / 255.0));
        return grid;
    }

    public static Biome Classify(double elevation, double mountainLevel, double hillLevel, double temperature, double moisture)
    {
        if (elevation >= mountainLevel) return Biome.Mountain;
        if (elevation >= hillLevel) return Biome.Hills;
        if (temperature < 0.3) return moisture < 0.45 ? Biome.Tundra : Biome.Taiga;
        if (temperature < 0.65) return moisture < 0.35 ? Biome.Grassland : moisture < 0.62 ? Biome.Forest : Biome.Swamp;
        return moisture < 0.32 ? Biome.Desert : moisture < 0.55 ? Biome.Savanna : Biome.Jungle;
    }

    /// <summary>Where each continent lies, in the block's −1…1 coordinates, and how far it reaches: spread evenly round the
    /// middle at a random turn, a little jittered.</summary>
    public static IReadOnlyList<(double X, double Y, double Reach)> ContinentCentres(int count, Rng rng)
    {
        var turn = rng.NextDouble() * Math.PI * 2;
        var (ring, reach) = count switch { 2 => (0.55, 0.42), 3 => (0.56, 0.36), _ => (0.6, 0.31) };
        return Enumerable.Range(0, count).Select(k =>
        {
            var angle = turn + k * Math.PI * 2 / count + (rng.NextDouble() - 0.5) * 0.4;
            var r = ring * (0.85 + rng.NextDouble() * 0.3);
            return (Math.Cos(angle) * r, Math.Sin(angle) * r, reach * (0.85 + rng.NextDouble() * 0.3));
        }).ToList();
    }

    /// <summary>
    /// How far a point lies from the nearest continent's heart, measured in coordinates warped by noise (so coasts are
    /// ragged, with bays and peninsulas), capped in the open sea so the noise can raise islands there, and rising
    /// steeply near the block's edge.
    /// </summary>
    private static double ContinentFalloff(IReadOnlyList<(double X, double Y, double Reach)> continents, double dx, double dy,
        Noise2D noise, double u, double v)
    {
        var wx = dx + (noise.Fractal(u + 17.3, v + 5.1, 3) - 0.5) * 0.7;
        var wy = dy + (noise.Fractal(u + 41.7, v + 29.9, 3) - 0.5) * 0.7;
        var near = continents.Min(c => ((wx - c.X) * (wx - c.X) + (wy - c.Y) * (wy - c.Y)) / (c.Reach * c.Reach));
        return Math.Min(0.75, 0.3 * near) + 6 * Math.Pow(Math.Max(0, Math.Max(Math.Abs(dx), Math.Abs(dy)) - 0.75), 2);
    }

    private static double[] RawElevation(TerrainGrid grid, Noise2D noise, IReadOnlyList<(double X, double Y, double Reach)>? continents = null)
    {
        var raw = new double[grid.Count];
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var dx = (x + 0.5) / grid.Width * 2 - 1;
                var dy = (y + 0.5) / grid.Height * 2 - 1;
                // Land rises towards the middle, or towards the nearest continent's heart.
                var falloff = continents is null
                    ? 0.5 * (dx * dx + dy * dy)
                    : ContinentFalloff(continents, dx, dy, noise, x * 2.0 / grid.Width, y * 2.0 / grid.Height);
                var edge = x < BorderWidth || y < BorderWidth || x >= grid.Width - BorderWidth || y >= grid.Height - BorderWidth;
                raw[grid.Index(x, y)] = edge
                    ? -1
                    : noise.Fractal(x * 4.0 / grid.Width, y * 4.0 / grid.Height) - falloff;
            }
        return raw;
    }

    private static void WriteElevation(TerrainGrid grid, double[] raw, double landLevel)
    {
        var min = raw.Min();
        var max = raw.Max();
        for (var i = 0; i < raw.Length; i++)
        {
            grid.Elevation[i] = raw[i] >= landLevel && raw[i] > -1
                ? (byte)(SeaLevel + 1 + (int)((raw[i] - landLevel) / Math.Max(1e-9, max - landLevel) * (254 - SeaLevel)))
                : (byte)((raw[i] - min) / Math.Max(1e-9, landLevel - min) * SeaLevel);
            grid.SetBiome(i, grid.Elevation[i] > SeaLevel ? Biome.Grassland : Biome.Ocean);
        }
    }

    /// <summary>Land masses smaller than minTiles (4-connected) become sea.</summary>
    private static void SinkSmallIslands(TerrainGrid grid, int minTiles)
    {
        foreach (var component in LandComponents(grid))
            if (component.Count < minTiles)
                foreach (var i in component)
                {
                    grid.Elevation[i] = SeaLevel;
                    grid.SetBiome(i, Biome.Ocean);
                }
    }

    /// <summary>4-connected land components, ordered by their lowest tile index.</summary>
    public static List<List<int>> LandComponents(TerrainGrid grid)
    {
        var seen = new bool[grid.Count];
        var components = new List<List<int>>();
        for (var start = 0; start < grid.Count; start++)
        {
            if (seen[start] || !grid.IsLand(start)) continue;
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                component.Add(i);
                foreach (var n in grid.Neighbours4(i))
                    if (!seen[n] && grid.IsLand(n))
                    {
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
            }
            components.Add(component);
        }
        return components;
    }

    private static void WriteTemperature(TerrainGrid grid, Noise2D noise)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var i = grid.Index(x, y);
                var latitude = (double)y / (grid.Height - 1);
                var height = Math.Max(0, grid.Elevation[i] - SeaLevel) / (255.0 - SeaLevel);
                var t = 0.2 + 0.75 * latitude - 0.4 * height + (noise.Fractal(x * 3.0 / grid.Width, y * 3.0 / grid.Height, 3) - 0.5) * 0.4;
                grid.Temperature[i] = ToByte(t);
            }
    }

    private static void WriteMoisture(TerrainGrid grid, Noise2D noise)
    {
        var distance = DistanceToSea(grid);
        var coastReach = grid.Width / 10.0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var i = grid.Index(x, y);
                var coastal = Math.Max(0, 1 - distance[i] / coastReach);
                var m = (noise.Fractal(x * 4.0 / grid.Width, y * 4.0 / grid.Height, 4) - 0.5) * 2.0 + 0.4 + 0.2 * coastal;
                grid.Moisture[i] = ToByte(m);
            }
    }

    /// <summary>4-neighbour steps from each tile to the nearest ocean tile (0 for ocean).</summary>
    public static int[] DistanceToSea(TerrainGrid grid)
    {
        var distance = new int[grid.Count];
        Array.Fill(distance, int.MaxValue);
        var queue = new Queue<int>();
        for (var i = 0; i < grid.Count; i++)
            if (!grid.IsLand(i))
            {
                distance[i] = 0;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            foreach (var n in grid.Neighbours4(i))
                if (distance[n] == int.MaxValue)
                {
                    distance[n] = distance[i] + 1;
                    queue.Enqueue(n);
                }
        }
        return distance;
    }

    private static double Percentile(double[] values, double fraction)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)(fraction * sorted.Length), 0, sorted.Length - 1)];
    }

    private static byte ToByte(double unit) => (byte)(Math.Clamp(unit, 0, 1) * 255);
}

using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Geo;

/// <summary>
/// Deterministic value noise with fractal octaves, in [0, 1). Uses only +, -, *, / so results are
/// bit-identical on every platform (Math.Pow/Sin/Exp are not guaranteed to be).
/// </summary>
public sealed class Noise2D
{
    private readonly ulong _seed;

    public Noise2D(ulong seed) => _seed = seed;

    public double Fractal(double x, double y, int octaves = 5, double persistence = 0.5)
    {
        double sum = 0, amplitude = 1, norm = 0, frequency = 1;
        for (var octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * Value(x * frequency, y * frequency, octave);
            norm += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }
        return sum / norm;
    }

    public double Value(double x, double y, int octave = 0)
    {
        var x0 = (long)Math.Floor(x);
        var y0 = (long)Math.Floor(y);
        var sx = Smooth(x - x0);
        var sy = Smooth(y - y0);
        var top = Lerp(Lattice(x0, y0, octave), Lattice(x0 + 1, y0, octave), sx);
        var bottom = Lerp(Lattice(x0, y0 + 1, octave), Lattice(x0 + 1, y0 + 1, octave), sx);
        return Lerp(top, bottom, sy);
    }

    private double Lattice(long x, long y, int octave)
    {
        var h = StableHash.Mix(_seed
                               ^ StableHash.Mix((ulong)x * 0x9E3779B97F4A7C15UL)
                               ^ StableHash.Mix((ulong)y * 0xC2B2AE3D27D4EB4FUL + (ulong)octave));
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

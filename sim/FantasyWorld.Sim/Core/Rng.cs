namespace FantasyWorld.Sim.Core;

/// <summary>SplitMix64 PRNG. Deterministic across platforms and .NET versions.</summary>
public sealed class Rng
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    /// <summary>An independent stream for one system and phase on one world day.</summary>
    public static Rng ForStream(ulong worldSeed, string stream, long tick) =>
        new(Mix(worldSeed ^ Mix(StableHash.Fnv1a64(stream)) ^ Mix((ulong)tick * Golden)));

    public ulong NextULong() => Mix(_state += Golden);

    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range must not be empty.");
        var span = (ulong)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)(NextULong() % span));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

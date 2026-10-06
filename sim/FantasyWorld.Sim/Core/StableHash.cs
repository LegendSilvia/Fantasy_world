namespace FantasyWorld.Sim.Core;

/// <summary>FNV-1a 64-bit over UTF-16 code units. Stable across processes, unlike string.GetHashCode.</summary>
public static class StableHash
{
    public static ulong Fnv1a64(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>SplitMix64 finalizer: a fast, well-distributed 64-bit mix.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Derives an independent seed for a named purpose, e.g. ("terrain/height").</summary>
    public static ulong Derive(ulong seed, string purpose) => Mix(seed ^ Mix(Fnv1a64(purpose)));
}

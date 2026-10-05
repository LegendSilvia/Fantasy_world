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
}

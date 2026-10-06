using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Model;

/// <summary>The sounds a culture's names are built from. Persisted with each culture.</summary>
public sealed record NameStyle(string[] Onsets, string[] Vowels, string[] Codas)
{
    public static readonly NameStyle Default = new(
        new[] { "", "b", "br", "c", "d", "dr", "f", "g", "gr", "h", "k", "l", "m", "n", "r", "s", "st", "t", "th", "v", "w" },
        new[] { "a", "e", "i", "o", "u", "ae", "ia", "ei", "ou" },
        new[] { "", "", "n", "r", "l", "th", "s", "nd", "rn", "ll", "m" });

    /// <summary>A distinct subset of the default sounds, so each culture's names have their own flavour.</summary>
    public static NameStyle Derive(Rng rng) => new(
        Subset(Default.Onsets, 9, rng),
        Subset(Default.Vowels, 5, rng),
        Subset(Default.Codas, 6, rng));

    private static string[] Subset(string[] source, int count, Rng rng)
    {
        var pool = source.Distinct().ToList();
        var chosen = new List<string> { "" };
        if (!pool.Contains("")) chosen.Clear();
        pool.Remove("");
        while (chosen.Count < count && pool.Count > 0)
        {
            var i = rng.NextInt(0, pool.Count);
            chosen.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return chosen.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }
}

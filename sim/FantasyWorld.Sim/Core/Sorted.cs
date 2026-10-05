namespace FantasyWorld.Sim.Core;

internal static class Sorted
{
    public static SortedDictionary<string, T> From<T>(IEnumerable<KeyValuePair<string, T>>? source)
    {
        var result = new SortedDictionary<string, T>(StringComparer.Ordinal);
        if (source is null) return result;
        foreach (var (key, value) in source) result[key] = value;
        return result;
    }
}

namespace FantasyWorld.Sim.Core;

/// <summary>Per-kind entity ids ("region", "settlement", …), each starting at 1. Persisted with the world.</summary>
public sealed class IdGen
{
    private SortedDictionary<string, long> _next = new(StringComparer.Ordinal);

    /// <summary>The next id each kind will receive. Re-sorted ordinally when deserialized.</summary>
    public SortedDictionary<string, long> Next
    {
        get => _next;
        init => _next = Sorted.From(value);
    }

    public long Take(string kind)
    {
        var id = Peek(kind);
        _next[kind] = id + 1;
        return id;
    }

    public long Peek(string kind) => _next.GetValueOrDefault(kind, 1);
}

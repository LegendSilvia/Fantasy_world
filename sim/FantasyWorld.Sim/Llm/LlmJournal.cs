namespace FantasyWorld.Sim.Llm;

/// <summary>One answered request, as stored in the <c>llm_calls</c> table. Note carries job-specific marks (e.g. "upset").</summary>
public sealed record LlmCall(
    string Hash, ModelTier Tier, long Day, string Provider, string Model, string Json,
    long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal CostUsd,
    DateTimeOffset RealUtc, string Note = "");

/// <summary>
/// Every LLM answer the world has used, keyed by request hash (spec §7 "LLM results are stored; replay
/// reads them and never re-queries"). New calls stay pending until the world file saves them, in the same
/// transaction as the month's events.
/// </summary>
public sealed class LlmJournal
{
    private readonly SortedDictionary<string, LlmCall> _calls = new(StringComparer.Ordinal);
    private readonly List<LlmCall> _pending = new();

    public int Count => _calls.Count;

    public IReadOnlyList<LlmCall> Pending => _pending;

    public IEnumerable<LlmCall> All => _calls.Values;

    public bool TryGet(string hash, out LlmCall call) => _calls.TryGetValue(hash, out call!);

    public void Add(LlmCall call)
    {
        if (_calls.TryAdd(call.Hash, call)) _pending.Add(call);
    }

    /// <summary>Loads a call that is already persisted.</summary>
    public void Restore(LlmCall call) => _calls[call.Hash] = call;

    public void MarkSaved() => _pending.Clear();

    public int CountSince(DateTimeOffset since, Func<ModelTier, bool> tiers) =>
        _calls.Values.Count(c => c.RealUtc >= since && tiers(c.Tier));

    public decimal SpendSince(DateTimeOffset since) => _calls.Values.Where(c => c.RealUtc >= since).Sum(c => c.CostUsd);
}

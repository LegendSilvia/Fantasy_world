using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Rule-based placeholder names, unique per world. The LLM invention layer (Plan 5) may rename
/// places with culture-aware names; these remain the offline fallback.
/// </summary>
public sealed class NameGen
{
    private static readonly string[] Onsets =
        { "", "b", "br", "c", "d", "dr", "f", "g", "gr", "h", "k", "l", "m", "n", "r", "s", "st", "t", "th", "v", "w" };
    private static readonly string[] Vowels = { "a", "e", "i", "o", "u", "ae", "ia", "ei", "ou" };
    private static readonly string[] Codas = { "", "", "n", "r", "l", "th", "s", "nd", "rn", "ll", "m" };
    private static readonly string[] RegionSuffixes =
        { "mark", "moor", "vale", "fell", "wold", "reach", "shire", "dale", "march", "holt" };
    private static readonly string[] SettlementSuffixes =
        { "ford", "by", "ton", "wick", "stead", "burg", "haven", "bridge", "mere", "cross" };

    private readonly Rng _rng;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public NameGen(Rng rng) => _rng = rng;

    public string Region() => Unique(() => Root() + (_rng.Chance(0.7) ? Pick(RegionSuffixes) : ""));

    public string Settlement() => Unique(() => Root() + (_rng.Chance(0.6) ? Pick(SettlementSuffixes) : ""));

    private string Unique(Func<string> make)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var name = make();
            if (name.Length >= 3 && _used.Add(name)) return name;
        }
        for (var n = 2; ; n++)
        {
            var name = $"{make()} {n}";
            if (_used.Add(name)) return name;
        }
    }

    private string Root()
    {
        var syllables = _rng.Chance(0.5) ? 1 : 2;
        var text = "";
        for (var i = 0; i < syllables; i++) text += Pick(Onsets) + Pick(Vowels) + Pick(Codas);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private string Pick(string[] options) => options[_rng.NextInt(0, options.Length)];
}

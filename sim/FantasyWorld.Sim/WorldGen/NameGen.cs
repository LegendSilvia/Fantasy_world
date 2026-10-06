using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Rule-based placeholder names. Place and house names are unique per generator; given names repeat.
/// The LLM invention layer (Plan 5) may rename things; these remain the offline fallback.
/// </summary>
public sealed class NameGen
{
    private static readonly string[] RegionSuffixes =
        { "mark", "moor", "vale", "fell", "wold", "reach", "shire", "dale", "march", "holt" };
    private static readonly string[] SettlementSuffixes =
        { "ford", "by", "ton", "wick", "stead", "burg", "haven", "bridge", "mere", "cross" };
    private static readonly string[] HouseSuffixes = { "ar", "en", "is", "ow", "eth", "ard", "ing" };

    private readonly Rng _rng;
    private readonly NameStyle _style;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public NameGen(Rng rng, NameStyle? style = null)
    {
        _rng = rng;
        _style = style ?? NameStyle.Default;
    }

    public string Region() => Unique(() => Root() + (_rng.Chance(0.7) ? Pick(RegionSuffixes) : ""));

    public string Settlement() => Unique(() => Root() + (_rng.Chance(0.6) ? Pick(SettlementSuffixes) : ""));

    public string House() => Unique(() => Root() + (_rng.Chance(0.5) ? Pick(HouseSuffixes) : ""));

    /// <summary>A culture, faith or deity name.</summary>
    public string Proper() => Unique(Root);

    /// <summary>A person's given name. Not unique: many people share a name.</summary>
    public string Given()
    {
        for (var attempt = 0; ; attempt++)
        {
            var name = Root();
            if (name.Length >= 3 || attempt >= 20) return name;
        }
    }

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
        for (var i = 0; i < syllables; i++) text += Pick(_style.Onsets) + Pick(_style.Vowels) + Pick(_style.Codas);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private string Pick(string[] options) => options[_rng.NextInt(0, options.Length)];
}

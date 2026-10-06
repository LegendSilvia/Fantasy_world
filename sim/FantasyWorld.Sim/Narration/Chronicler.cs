using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Narration;

/// <summary>
/// The world's chronicler (sub-project 5 §4): a name, a temperament and a diction every LLM narration is written in.
/// Derived from the world's seed and its first culture, so it is the same every time the world is opened and the
/// world bible stays byte-identical (prompt caching keeps hitting).
/// </summary>
public sealed record Chronicler(string Name, string Temperament, string Diction)
{
    private static readonly (string Temperament, string Diction)[] Voices =
    {
        ("austere", "plain words and short sentences, sparing with praise and blame"),
        ("devout", "measured cadence, mindful of the gods and of omens, never preaching"),
        ("wry", "dry understatement, noticing the vanity of the great without mocking the dead"),
        ("melancholy", "a long view of loss and memory, gentle with the fallen"),
        ("proud", "the stately manner of a court historian, careful with titles and lineages"),
    };

    /// <summary>The house style every narration follows, whoever the chronicler is.</summary>
    public const string StyleGuide = """
        - Short sentences and concrete nouns; say what happened, to whom, where and why.
        - Use names exactly as the brief gives them. Never invent a person, place, title, number or date.
        - No anachronisms and no modern idiom; at most one figure of speech in a paragraph.
        - British spelling. Years are written "Year 12".
        """;

    public static Chronicler Of(World world)
    {
        var rng = new Rng(world.Seed ^ 0x6368726f6e69636cUL); // "chronicl"
        var culture = world.Cultures.Values.FirstOrDefault();
        var names = new NameGen(rng, culture?.Style);
        var (temperament, diction) = Voices[rng.NextInt(0, Voices.Length)];
        var name = culture is null ? names.Given() : $"{names.Given()} of the {culture.Name}";
        return new Chronicler(name, temperament, diction);
    }

    /// <summary>The chronicler as the world bible introduces them.</summary>
    public string Persona => $"You write as {Name}, the world's {Temperament} chronicler: {Diction}.";
}

using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static readonly HashSet<string> ViewOptions = new(StringComparer.Ordinal)
        { "--as-of", "--generations", "--depth", "--from", "--to", "--category", "--min-importance", "--page" };

    /// <summary>
    /// `fw view`: a screen's view model as the JSON the client consumes (sub-project 2 §4). Output is always JSON
    /// (`--json` is accepted for scripts that pass it). Ids may be bare numbers or entities as printed (char#12).
    /// </summary>
    private static int View(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < rest.Length; i++)
        {
            if (rest[i] == "--json") continue;
            if (ViewOptions.Contains(rest[i]))
            {
                if (i + 1 >= rest.Length || rest[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new FormatException($"{rest[i]} needs a value.");
                options[rest[i]] = rest[++i];
            }
            else if (rest[i].StartsWith("--", StringComparison.Ordinal)) throw new FormatException($"Unknown option {rest[i]}.");
            else positional.Add(rest[i]);
        }
        long Id()
        {
            if (positional.Count == 0) throw new FormatException($"`fw view {rest[0]}` needs an id.");
            var text = positional[0];
            return text.Contains('#') ? Importance.ParseRef(text).Id : long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        int? Number(string option, int min, int max)
        {
            if (!options.TryGetValue(option, out var text)) return null;
            var n = int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return n >= min && n <= max ? n : throw new FormatException($"{option} must be between {min} and {max}.");
        }

        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var now = world.Date.Year;
        output.WriteLine(rest[0] switch
        {
            "digest" => ViewJson.Serialize(DigestScreen.ForReader(world, log, store.LoadFollowed())),
            "map" => ViewJson.Serialize(MapScreen.Build(world, log,
                Number("--as-of", 1, now) is { } year ? new WorldDate((long)year * WorldDate.DaysPerYear - 1) : null)),
            "character" => ViewJson.Serialize(CharacterScreen.Build(world, log, Id(), store.LoadFollowed().Contains(new(Sim.Model.Character.Kind, Id())))),
            "family" => ViewJson.Serialize(FamilyTree.Build(world, Id(), Number("--generations", 0, 10) ?? 2)),
            "why" => ViewJson.Serialize(WhyScreen.Build(world, log, Id(), Number("--depth", 1, 20) ?? 4)),
            "chronicle" => ViewJson.Serialize(ChronicleScreen.Build(world, log, Number("--from", int.MinValue, now), Number("--to", int.MinValue, now),
                options.TryGetValue("--category", out var category) ? ParseCategory(category) : null,
                Number("--min-importance", 0, 100) ?? 0, Number("--page", 1, int.MaxValue) ?? 1)),
            "realm" => ViewJson.Serialize(RealmScreen.Build(world, log, Id())),
            "artifact" => ViewJson.Serialize(ArtifactScreen.Build(world, log, Id())),
            _ => throw new FormatException($"Unknown view '{rest[0]}'. Use digest, map, character, family, why, chronicle, realm or artifact."),
        });
        return 0;
    }

    private static Category ParseCategory(string text) =>
        Enum.GetNames<Category>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<Category>(name)
            : throw new FormatException($"Unknown category '{text}'. Use {string.Join(", ", Enum.GetNames<Category>().Select(n => n.ToLowerInvariant()))}.");
}

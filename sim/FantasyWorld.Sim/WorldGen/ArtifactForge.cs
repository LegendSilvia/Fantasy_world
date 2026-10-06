using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// Makes artifacts: a type, a material (rare ones when the region yields them), 1–3 powers fitting
/// the type, sometimes a hidden curse, and a name. The engine owns the mechanics; the LLM invention
/// layer (Plan 5) may later replace the name and write lore within these slots.
/// </summary>
public static class ArtifactForge
{
    public static readonly IReadOnlyList<string> Curses = new[] { "paranoia", "wasting", "bloodlust", "misfortune" };

    private static readonly string[] CommonMaterials = { "steel", "bronze", "oak", "gold", "silver", "obsidian", "bone", "glass" };

    private static readonly IReadOnlyDictionary<ArtifactType, string[]> AttributeFor = new Dictionary<ArtifactType, string[]>
    {
        [ArtifactType.Weapon] = new[] { "prowess", "martial" },
        [ArtifactType.Armor] = new[] { "prowess" },
        [ArtifactType.Crown] = new[] { "diplomacy", "stewardship" },
        [ArtifactType.Tome] = new[] { "lore" },
        [ArtifactType.Relic] = new[] { "diplomacy", "lore" },
        [ArtifactType.Trinket] = new[] { "intrigue" },
    };

    private static readonly IReadOnlyDictionary<ArtifactType, string> TagFor = new Dictionary<ArtifactType, string>
    {
        [ArtifactType.Weapon] = "ward",
        [ArtifactType.Armor] = "ward",
        [ArtifactType.Crown] = "legitimacy",
        [ArtifactType.Tome] = "truthsight",
        [ArtifactType.Relic] = "harvest",
        [ArtifactType.Trinket] = "truthsight",
    };

    private static readonly IReadOnlyDictionary<ArtifactType, string[]> Patterns = new Dictionary<ArtifactType, string[]>
    {
        [ArtifactType.Weapon] = new[] { "{0}, Blade of {1}", "The Spear of {1}", "{0}" },
        [ArtifactType.Armor] = new[] { "The Aegis of {1}", "{0}'s Mail", "The {2} Hauberk" },
        [ArtifactType.Crown] = new[] { "The Crown of {1}", "The {2} Circlet" },
        [ArtifactType.Tome] = new[] { "The Codex of {1}", "{0}'s Grimoire" },
        [ArtifactType.Relic] = new[] { "The {2} Chalice", "The Reliquary of {1}" },
        [ArtifactType.Trinket] = new[] { "The Ring of {1}", "{0}'s Locket" },
    };

    /// <summary>
    /// A new artifact with no location yet. rareMaterials are the rare resources the place of making
    /// yields (ResourceKinds.Rare); one may become the material and strengthen the powers.
    /// </summary>
    public static Artifact Create(Rng rng, IdGen ids, NameGen names, long originEventId,
        ArtifactType? type = null, IReadOnlyList<string>? rareMaterials = null, double fame = 10)
    {
        var kind = type ?? (ArtifactType)rng.NextInt(0, 6);
        var rare = rareMaterials is { Count: > 0 } && rng.Chance(0.6) ? rareMaterials[rng.NextInt(0, rareMaterials.Count)] : null;
        var material = rare ?? CommonMaterials[rng.NextInt(0, CommonMaterials.Length)];

        var powers = new List<ArtifactPower>();
        var attributes = AttributeFor[kind];
        var count = rng.NextInt(1, 4);
        for (var i = 0; i < count; i++)
        {
            if (i == 2 || (i == 1 && rng.Chance(0.4)))
            {
                if (powers.All(p => p.Kind != TagFor[kind])) powers.Add(new ArtifactPower(TagFor[kind], null, 0));
                continue;
            }
            var attribute = attributes[rng.NextInt(0, attributes.Length)];
            var amount = rng.NextInt(1, 4) + (rare is null ? 0 : 1);
            var existing = powers.FindIndex(p => p.Attribute == attribute);
            if (existing >= 0) powers[existing] = powers[existing] with { Amount = Math.Min(5, powers[existing].Amount + amount) };
            else powers.Add(new ArtifactPower("attribute", attribute, amount));
        }

        var pattern = Patterns[kind][rng.NextInt(0, Patterns[kind].Length)];
        var name = string.Format(pattern, names.Proper(), names.Proper(), Capitalise(material));
        return new Artifact
        {
            Id = ids.Take(Artifact.Kind),
            Name = name,
            Type = kind,
            Material = material,
            OriginEventId = originEventId,
            Powers = powers,
            Curse = rng.Chance(0.2) ? Curses[rng.NextInt(0, Curses.Count)] : null,
            Fame = Math.Clamp(fame, 0, 100),
        };
    }

    private static string Capitalise(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}

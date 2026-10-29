using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// The world's legend and its secrets (the owner, 2026-10-29): what the people believe of their world's making, told in
/// brief as every episode opens and in full in the first; and what deeds uncover of it (a labyrinth cleared reveals a
/// secret no one knew, kept on the world and told as news). Claude writes them where it may; the rules here are the
/// engine's own words, used offline.
/// </summary>
public static class Legends
{
    public const int MaxSummary = 320, MaxFull = 2000, MaxSecret = 480;

    /// <summary>So many living beasts make an age of beasts, which the legend and the world's state tell of.</summary>
    public const int BeastAge = 8;

    /// <summary>
    /// The beasts preying on the world now (the owner, 2026-10-29: a world full of beasts shapes its lore): how many, and the
    /// most feared of them with where it lairs; null when there are too few to mark the age.
    /// </summary>
    public static string? Beasts(World world)
    {
        // Only beasts someone has lived to describe (the owner, 2026-10-29).
        var living = world.Monsters.Values.Where(m => m.Alive && m.Revealed).ToList();
        if (living.Count < BeastAge) return null;
        var feared = living.OrderByDescending(m => m.Notoriety).ThenByDescending(m => m.Might).ThenBy(m => m.Id).First();
        var where = world.Regions.GetValueOrDefault(feared.RegionId)?.Name;
        var name = English.InProse(feared.Name);
        return $"This is an age of beasts: {English.Count(living.Count, "beast")} prey on the lands, and the most feared of them is {name}"
               + (where is null ? "." : $", in {where}.");
    }

    /// <summary>What the legend may draw on: the world's lands, peoples, gods, and the shadow it fears.</summary>
    public static string Facts(World world)
    {
        var lines = new List<string> { $"The world is called {world.Name}." };
        if (world.Continents.Count > 0)
            lines.Add($"Its lands: {English.List(world.Continents.Values.OrderBy(c => c.Id).Select(c => c.Name).ToList())}, parted by the sea.");
        if (world.Races.Count > 0)
            lines.Add("Its peoples: " + string.Join("; ", world.Races.Values.OrderBy(r => r.Id).Select(r => $"the {r.Name} ({r.Looks}; {r.Nature})")) + ".");
        if (world.Faiths.Count > 0)
            lines.Add("Its gods: " + string.Join("; ", world.Faiths.Values.OrderBy(f => f.Id).Select(f => $"{f.Deity}, worshipped by the {f.Name}")) + ".");
        var mazes = world.Structures.Values.Count(s => s.Type == StructureKind.Labyrinth);
        if (mazes > 0) lines.Add($"{English.Capitalise(English.Count(mazes, "labyrinth"))} lie beneath its lands, their depths unexplored.");
        lines.Add("Its people fear a demon lord who rises in the dark places of the world, and await a hero to stand against it.");
        if (Beasts(world) is { } beasts) lines.Add(beasts + " The people of this age live in fear of them.");
        // The tales the songs keep (LoreSystem): the legend may weave them in; forgotten ones it must not tell.
        if (world.Tales.Where(t => !t.Forgotten).ToList() is { Count: > 0 } sung)
            lines.Add("The songs remember: " + string.Join(" ", sung.Select(t => $"{t.Title} (Year {t.Year}): {t.Text}")));
        return string.Join("\n", lines);
    }

    /// <summary>The engine's own legend, from the world's facts alone.</summary>
    public static WorldLegend Rules(World world, long sinceEventId)
    {
        var lands = world.Continents.Count > 1
            ? $"{English.Number(world.Continents.Count)} lands parted by the sea, {English.List(world.Continents.Values.OrderBy(c => c.Id).Select(c => c.Name).ToList())}"
            : "lands between the mountains and the sea";
        var peoples = world.Races.Values.OrderBy(r => r.Id).Select(r => "the " + r.Name).ToList();
        var gods = world.Faiths.Values.OrderBy(f => f.Id).Select(f => f.Deity).Where(d => d.Length > 0).Distinct().ToList();
        var mazes = world.Structures.Values.Count(s => s.Type == StructureKind.Labyrinth);
        var summary = $"{world.Name} is a world of {lands}, where {(peoples.Count > 0 ? English.List(peoples) : "many peoples")} live under old gods and older fears.";
        var full = new List<string>
        {
            $"In the beginning, the people of {world.Name} say, the gods{(gods.Count > 0 ? $", {English.List(gods)} among them," : "")} raised {lands} out of the dark waters, and gave each people its share.",
            peoples.Count > 0 ? $"So {English.List(peoples)} came to the world, each believing it was made first, and none has forgotten the wrongs the others did it." : "",
            mazes > 0 ? $"Beneath the lands lie {English.Count(mazes, "labyrinth")}, older than any kingdom; the old songs say each keeps a secret of the world's making, and that no one has yet reached the bottom of one." : "",
            "And in the dark places a shadow waits: a demon lord, the tales say, will rise again, and only a hero chosen by the gods can drive it back.",
        };
        return new WorldLegend(summary, string.Join(" ", full.Where(s => s.Length > 0)), sinceEventId);
    }

    /// <summary>What a secret's reveal may draw on: the legend, the secrets already known, and the deed that uncovered it.</summary>
    public static string SecretFacts(World world, string where, string who)
    {
        var known = world.Secrets.Select(s => s.Title).ToList();
        return (world.Legend is { } legend ? $"The world's legend: {legend.Summary}\n" : "")
               + (known.Count > 0 ? $"Secrets already uncovered: {string.Join("; ", known)}. This one must be new.\n" : "This is the first secret of the world to come to light.\n")
               + $"Uncovered when {who} reached the bottom of {where}. A truth about the world's making, its gods, its peoples or the shadow, "
               + "that the legend never told: it deepens the legend, never contradicts it.";
    }

    /// <summary>The engine's own secrets, in turn by the world's seed, named for where they were found.</summary>
    public static (string Title, string Text)? RulesSecret(World world, string where)
    {
        string[] titles = ["The Sealed Tablets", "The Drowned Hymn", "The Ninth Name", "The Broken Covenant", "The First Fire", "The Sleeping Choir"];
        string[] texts =
        [
            "Carved at the bottom were the gods' own words: the world was not raised from the waters, but walled against something beneath them.",
            "A hymn sung by no living people tells that the first kings bargained with the dark for their crowns, and that the debt is not yet paid.",
            "The gods were once nine, the walls say; the ninth was cast down, and its name is the one the demon lord answers to.",
            "The peoples of the world were once one, the carvings show, until a covenant broken in the first age set them against each other.",
            "Deep below burns a fire older than the gods, and every labyrinth was dug to keep it from waking.",
            "Below, in ranks, stand the gods' first servants, asleep; the walls say they will wake when the last labyrinth falls silent.",
        ];
        // A truth already told is never told again (the owner, 2026-10-29: "many duplicates in the lore").
        var told = world.Secrets.Select(s => s.Text).ToHashSet(StringComparer.Ordinal);
        var start = (int)((world.Seed + (ulong)world.Secrets.Count) % (ulong)titles.Length);
        var left = Enumerable.Range(0, titles.Length).Select(k => (start + k) % titles.Length).Where(k => !told.Contains(texts[k])).ToList();
        // All told: none is made up twice (an inventor may still write a new one).
        if (left.Count == 0) return null;
        return ($"{titles[left[0]]} of {English.InProse(where)}", texts[left[0]]);
    }

    /// <summary>The legend the episode tells: in full until an episode that told it so has aired, else its summary.</summary>
    public static string? Told(World world, long sinceEventId, out bool full)
    {
        full = world.Legend is { Told: false };
        return world.Legend is null ? null : full ? world.Legend.Full : world.Legend.Summary;
    }
}

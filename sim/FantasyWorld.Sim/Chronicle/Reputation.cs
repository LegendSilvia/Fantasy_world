using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>
/// What the world knows a character for (the owner, 2026-10-29: "known as the best archer who ever lived"): the best of
/// their calling among the living, the legends they slew, the labyrinths they conquered, the secrets they uncovered, the
/// duels and champions' clashes they won, the calling they awakened into. Read from the world and its log, at most a few.
/// </summary>
public sealed class Reputation
{
    public const int MostTold = 3;

    private readonly Dictionary<long, List<string>> _known = new();

    public IReadOnlyList<string> Of(long characterId) => _known.TryGetValue(characterId, out var k) ? k : [];

    /// <summary>"Known as the finest archer living, slayer of the Golem of Danmund."</summary>
    public string? Line(long characterId) => Of(characterId) is { Count: > 0 } k ? $"known as {English.List(k)}" : null;

    public static Reputation Of(World world, EventLog log)
    {
        var r = new Reputation();
        void Add(long id, string what)
        {
            if (id == 0 || !world.Characters.ContainsKey(id)) return;
            var list = r._known.TryGetValue(id, out var l) ? l : r._known[id] = new List<string>();
            if (list.Count < MostTold && !list.Contains(what)) list.Add(what);
        }
        long Actor(WorldEvent e, string role) => e.Actors.TryGetValue(role, out var a) && a.Kind == Character.Kind ? a.Id : 0;
        var narrator = new TemplateNarrator(world);
        string Name(EntityRef e) => narrator.NameOf(e) ?? "a nameless foe";

        // The first of each calling among the living.
        var living = world.Characters.Values.Where(c => c.IsActive && c.Class.Length > 0).ToList();
        foreach (var (calling, measure, title) in new (string, Func<Character, double>, string)[]
                 {
                     (Classes.Ranger, c => c.Attributes.Prowess * 2 + c.Renown / 10, "the finest archer living"),
                     (Classes.Warrior, c => c.Attributes.Prowess * 2 + c.Attributes.Martial + c.Renown / 10, "the mightiest warrior of the age"),
                     (Classes.Mage, c => c.Attributes.Lore * 2 + c.Renown / 10, "the greatest mage of the age"),
                     (Classes.Rogue, c => c.Attributes.Intrigue * 2 + c.Renown / 10, "the most cunning blade in the world"),
                     (Classes.Paladin, c => c.Attributes.Martial + c.Attributes.Prowess + c.Renown / 10, "the most steadfast of the holy knights"),
                     (Classes.Warlock, c => c.Attributes.Lore + c.Attributes.Intrigue + c.Renown / 10, "the most feared of the warlocks"),
                 })
            if (living.Where(c => Classes.Base(c.Class) == calling).OrderByDescending(measure).ThenBy(c => c.Id).FirstOrDefault() is { } best)
                Add(best.Id, title);
        foreach (var c in living.Where(c => Classes.IsAwakened(c.Class))) Add(c.Id, $"the {Classes.Title(c.Class)}");

        var duels = new Dictionary<long, int>();
        foreach (var e in log.All.Reverse())
            switch (e.Type)
            {
                case "MonsterSlain" when e.Actors.TryGetValue("monster", out var m) && world.Monsters.TryGetValue(m.Id, out var beast) && (beast.Legendary || beast.Might >= 60):
                    Add(Actor(e, "hunter"), $"slayer of {Name(m)}");
                    break;
                case "LabyrinthCleared" when e.Actors.TryGetValue("structure", out var s):
                    Add(Actor(e, "delver"), $"conqueror of {Name(s)}");
                    break;
                case "SecretRevealed" when e.Payload.TryGetValue("title", out var secret):
                    Add(Actor(e, "delver"), $"who uncovered {secret}");
                    break;
                case "DuelFought":
                    {
                        var won = (e.Payload.GetValueOrDefault("outcome") ?? "").StartsWith("attacker", StringComparison.Ordinal) ? Actor(e, "challenger") : Actor(e, "defender");
                        duels[won] = duels.GetValueOrDefault(won) + 1;
                        break;
                    }
                case "ChampionsClashed":
                    Add(Actor(e, "victor"), "a champion of the hosts");
                    break;
            }
        foreach (var (id, wins) in duels.Where(d => d.Value >= 3).OrderByDescending(d => d.Value).ThenBy(d => d.Key))
            Add(id, wins == duels.Values.Max() ? $"the deadliest duellist of the age, victor of {English.Count(wins, "duel")}" : $"victor of {English.Count(wins, "duel")}");
        return r;
    }
}

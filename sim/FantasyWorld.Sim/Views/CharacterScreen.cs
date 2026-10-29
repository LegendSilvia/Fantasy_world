using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum Tone { Warm, Neutral, Hostile }

public sealed record Place(EntityRef Ref, string Label, MapPoint? Position);

public sealed record TimelineRow(int Year, LinkedText Text, long EventId, bool Highlight);

/// <summary>
/// One of a character's connections (the owner, 2026-10-29): who, how they are bound, and the bond between them, how much the
/// character feels for them (−100…100), kept so the strength of a friendship can one day count in a fight.
/// </summary>
public sealed record Tie(EntityRef Ref, string Name, string Relation, string Qualifier, Tone Tone, int Bond = 0);

/// <summary>
/// The *Character page* (sub-project 2 §3.3): who they are, their traits and ambition, where they are, the timeline of
/// their life (minor events folded) and their ties — family, liege, and the people they love or hate most.
/// </summary>
public sealed record CharacterScreen(
    EntityRef Ref, string Name, EntityRef? House, string Kicker, string Line, IReadOnlyList<string> Traits, string Ambition,
    Place? Location, IReadOnlyList<TimelineRow> Timeline, int Folded, int Earlier, IReadOnlyList<Tie> Ties, bool Followed,
    Mention? Faith = null, bool Saint = false, string? Class = null, Mention? AmbitionTarget = null, IReadOnlyList<string>? FormerNames = null)
{
    public const int TimelineImportance = 20;

    /// <summary>A long life shows its most recent rows; the rest are counted in <c>Earlier</c>.</summary>
    public const int MaxTimeline = 150;

    public static CharacterScreen Build(World world, EventLog log, long characterId, bool followed = false)
    {
        var c = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var nameOf = Names.Of(world);
        var narrator = new TemplateNarrator(world);
        var house = world.Houses.GetValueOrDefault(c.HouseId);
        var life = log.Involving(c.Ref);
        var born = life.FirstOrDefault(e => e.Type == "CharacterBorn" && e.Actors.GetValueOrDefault("child") == c.Ref);
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);

        var line = new List<string> { (c.SaintEventId != 0 ? "Saint · " : "") + RoleLine(world, c) };
        var today = c.Died ?? world.Date;
        if (Peoples.Of(world, c) is { } race) line.Add(race.Name);
        line.Add($"age {c.Age(today)}");
        var birthYear = new Core.WorldDate(c.BirthDay).Year;
        line.Add(born?.Place is { } birthplace && nameOf(birthplace) is { } bp
            ? $"born Year {birthYear} in {bp}"
            : birthYear < 1 ? "born before recorded history" : $"born Year {birthYear}");
        if (c.Died is { } died) line.Add($"died Year {died.Year}{(c.DeathCause is { } cause ? $" of {cause}" : "")}");

        var notable = life.Where(e => e.Importance >= TimelineImportance).ToList();
        var highlight = c.IsAlive
            ? notable.Where(e => world.Date.Day - e.Date.Day < Core.WorldDate.DaysPerYear).OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault()
            : notable.OrderByDescending(e => e.Importance).ThenBy(e => e.Id).FirstOrDefault();
        var earlier = Math.Max(0, notable.Count - MaxTimeline);
        var timeline = notable.Skip(earlier).Select(e => new TimelineRow(e.Date.Year,
            LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf), e.Id, e.Id == highlight?.Id)).ToList();

        Place? location = home is null ? null
            : new Place(home.Ref, c.IsActive ? $"Now at {home.Name}" : $"Last at {home.Name}", MapPoint.Of(world, home.Ref));
        return new CharacterScreen(c.Ref, Names.Full(world, c), house?.Ref, house is null ? "Character" : $"Character · House {house.Name}",
            string.Join(" · ", line), c.Traits.ToList(), AmbitionText(world, c), location, timeline, life.Count - notable.Count, earlier,
            TiesOf(world, c), followed,
            world.Faiths.TryGetValue(c.FaithId, out var faith) ? new Mention(faith.Ref, faith.Name) : null, c.SaintEventId != 0,
            c.Class.Length > 0 ? c.Class : null,
            c.Ambition == Ambitions.Vengeance && world.Characters.TryGetValue(c.AmbitionTarget, out var foe) ? new Mention(foe.Ref, Names.Full(world, foe)) : null,
            c.FormerNames.Count > 0 ? c.FormerNames.ToList() : null);
    }

    private static string RoleLine(World world, Character c)
    {
        var home = world.Settlements.GetValueOrDefault(c.SettlementId);
        var realm = home is null ? null : Realms.SovereignOf(world, home);
        var role = c.Role switch
        {
            Role.Lord when home is not null => $"Lord of {home.Name}",
            Role.Knight when realm is not null => $"Knight of {realm.Name}",
            Role.None => home is null ? "Commoner" : $"Of {home.Name}",
            _ => home is null ? c.Role.ToString() : $"{c.Role} of {home.Name}",
        };
        return c.IsAlive || c.Role == Role.None ? role : "Once " + char.ToLowerInvariant(role[0]) + role[1..];
    }

    private static string AmbitionText(World world, Character c) => c.Ambition switch
    {
        Ambitions.Renown => "Ambition: renown",
        Ambitions.Artifact => "Ambition: a great artifact",
        Ambitions.Magic => "Ambition: mastery of magic",
        Ambitions.Wealth => "Ambition: wealth",
        Ambitions.Vengeance when world.Characters.TryGetValue(c.AmbitionTarget, out var foe) => $"Ambition: vengeance on {Names.Full(world, foe)}",
        Ambitions.Vengeance => "Ambition: vengeance",
        _ => "Ambition: a family of their own",
    };

    /// <summary>The most friends and rivals shown beside family and companions.</summary>
    public const int MostFeelings = 6;

    /// <summary>
    /// A character's connections (the owner, 2026-10-29): liege, spouse, parents, children, siblings, sworn companion, and the
    /// people they love or hate most, each with the bond between them.
    /// </summary>
    private static List<Tie> TiesOf(World world, Character c)
    {
        var ties = new List<Tie>();
        int Bond(Character other) => (int)Math.Round(Relations.Opinion(c, other));
        void Add(Character? other, string relation, string qualifier, Tone tone)
        {
            if (other is null || other.Id == c.Id || ties.Any(t => t.Ref == other.Ref)) return;
            var q = other.Died is { } d ? (qualifier.Length > 0 ? $"{qualifier}, died Year {d.Year}" : $"died Year {d.Year}") : qualifier;
            ties.Add(new Tie(other.Ref, Names.Full(world, other), relation, q, tone, Bond(other)));
        }
        Tone ToneOf(Character other) => Relations.Opinion(c, other) is var o && o >= Relations.FriendAt ? Tone.Warm : o <= Relations.RivalAt ? Tone.Hostile : Tone.Neutral;
        string Feeling(Character other) => ToneOf(other) switch { Tone.Warm => "loyal", Tone.Hostile => "resentful", _ => "" };

        // Only the living have a liege today; the dead's lords are in their timeline.
        if (c.IsActive && world.Settlements.TryGetValue(c.SettlementId, out var home) && Realms.SovereignOf(world, home) is { } realm
            && Realms.RulerOf(world, realm) is { } liege)
            Add(liege, "Liege", Feeling(liege), ToneOf(liege));
        Add(world.Characters.GetValueOrDefault(c.SpouseId), "Spouse", "", Tone.Warm);
        if (c.Companion is { Kind: Companion.Sworn, CharacterId: not 0 } sworn)
            Add(world.Characters.GetValueOrDefault(sworn.CharacterId), "Sworn companion", "", Tone.Warm);
        Add(world.Characters.GetValueOrDefault(c.FatherId), "Father", "", ToneOf2(world.Characters.GetValueOrDefault(c.FatherId)));
        Add(world.Characters.GetValueOrDefault(c.MotherId), "Mother", "", ToneOf2(world.Characters.GetValueOrDefault(c.MotherId)));
        foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>())
            Add(child, "Child", "", ToneOf2(child));
        foreach (var sibling in Fates.Siblings(world, c))
            Add(sibling, "Sibling", "", ToneOf2(sibling));
        foreach (var other in c.Memories.Select(m => m.About).Distinct().Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()
                     .Select(o => (Other: o, Opinion: Relations.Opinion(c, o)))
                     .Where(x => x.Opinion >= Relations.FriendAt || x.Opinion <= Relations.RivalAt)
                     .OrderByDescending(x => Math.Abs(x.Opinion)).ThenBy(x => x.Other.Id).Take(MostFeelings))
            Add(other.Other, other.Opinion > 0 ? "Friend" : "Rival", "", other.Opinion > 0 ? Tone.Warm : Tone.Hostile);
        return ties;

        // Kin are neutral unless love or hate has grown between them.
        Tone ToneOf2(Character? other) => other is null ? Tone.Neutral : ToneOf(other);
    }
}

public sealed record FamilyNode(EntityRef Ref, string Name, int Born, int? Died, EntityRef? House);

public sealed record FamilyEdge(EntityRef From, EntityRef To, string Kind);

/// <summary>A character's family over a few generations up and down: nodes, parent and spouse edges.</summary>
public sealed record FamilyTree(EntityRef Root, IReadOnlyList<FamilyNode> Nodes, IReadOnlyList<FamilyEdge> Edges)
{
    public static FamilyTree Build(World world, long characterId, int generations = 2)
    {
        var root = world.Characters.TryGetValue(characterId, out var found) ? found : throw new KeyNotFoundException($"No character #{characterId}.");
        var people = new SortedDictionary<long, Character>();
        void Up(Character c, int depth)
        {
            people[c.Id] = c;
            if (depth == 0) return;
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Up(parent, depth - 1);
        }
        void Down(Character c, int depth)
        {
            people[c.Id] = c;
            if (world.Characters.TryGetValue(c.SpouseId, out var spouse)) people[spouse.Id] = spouse;
            if (depth == 0) return;
            foreach (var child in c.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>()) Down(child, depth - 1);
        }
        Up(root, generations);
        Down(root, generations);
        var nodes = people.Values.Select(c => new FamilyNode(c.Ref, Names.Full(world, c), new Core.WorldDate(c.BirthDay).Year, c.Died?.Year,
            world.Houses.TryGetValue(c.HouseId, out var h) ? h.Ref : null)).ToList();
        var edges = new List<FamilyEdge>();
        foreach (var c in people.Values)
        {
            foreach (var parent in new[] { c.FatherId, c.MotherId }.Where(people.ContainsKey))
                edges.Add(new FamilyEdge(people[parent].Ref, c.Ref, "parent"));
            if (people.TryGetValue(c.SpouseId, out var spouse) && c.Id < spouse.Id) edges.Add(new FamilyEdge(c.Ref, spouse.Ref, "spouse"));
        }
        return new FamilyTree(root.Ref, nodes, edges);
    }
}

using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Narration;

/// <summary>Turns events into prose (spec §8.7).</summary>
public interface INarrator
{
    /// <summary>One event as a sentence or two.</summary>
    string Narrate(WorldEvent ev);

    /// <summary>A why-chain as a short passage: the event, then what led to it.</summary>
    string NarrateChain(ChainNode chain);

    /// <summary>The morning digest: what happened while the reader was away.</summary>
    string Digest(DigestView digest);
}

/// <summary>
/// Per-event-type templates with connectives ("because", "which followed") — the offline narrator and the test
/// baseline. Placeholders: <c>{role}</c> names an actor, <c>{@place}</c> the place, <c>{=key}</c> a payload value.
/// A sentence whose placeholder is missing falls back to a generic line, so new event types always read.
/// </summary>
public sealed partial class TemplateNarrator(World world) : INarrator
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllianceBroken"] = "The alliance between {a} and {b} was broken.",
        ["AllianceFormed"] = "{a} and {b} swore an alliance.",
        ["AllyJoinedWar"] = "{ally} took up arms in {war}.",
        ["ArmyRaised"] = "{realm} raised {army}, {=soldiers} strong, for {war}.",
        ["ArmyRouted"] = "{army} broke and fled at {@place}.",
        ["ArtifactClaimed"] = "{claimant} claimed {artifact} from the treasury of {@place}.",
        ["ArtifactCurseRevealed"] = "The curse of {artifact} showed itself in {holder}: {=curse}.",
        ["ArtifactForged"] = "{maker} forged {artifact} at {@place}.",
        ["ArtifactFound"] = "{delver} found {artifact} in the depths of {@place}.",
        ["ArtifactInherited"] = "{heir} inherited {artifact}.",
        ["ArtifactStolen"] = "{thief} stole {artifact} from {victim}.",
        ["BattleFought"] = "At {@place}, {victor} defeated {vanquished} in a battle of {war}.",
        ["MonsterRose"] = "{monster}, a {=breed}, rose in {@place}.",
        ["MonsterRaid"] = "{monster} fell on {@place}; {=dead} died.",
        ["MonsterLegendary"] = "{monster} became a legend of terror in {@place}.",
        ["MonsterSlumbered"] = "{monster} sank into slumber in {@place}.",
        ["MonsterWoke"] = "{monster} woke in {@place}.",
        ["MonsterSlain"] = "{hunter} slew {monster} in {@place}.",
        ["RouteOpened"] = "Merchants opened {route} between {from} and {to}.",
        ["RouteSevered"] = "{route} was cut {=cut}.",
        ["RouteRestored"] = "Trade returned to {route}.",
        ["RouteClosed"] = "{route} fell out of use.",
        ["OccupationLapsed"] = "{realm} gave up its hold on {@place}.",
        ["SchismBegan"] = "{heresiarch} broke from {old} and founded {faith}.",
        ["SaintDeclared"] = "{faith} declared {saint} a saint.",
        ["WildMagic"] = "Wild magic broke loose at {@place}: a {=kind} that killed {=dead}.",
        ["MageTrained"] = "{mage} was trained in the mage tower of {@place}.",
        ["MagicLawChanged"] = "{realm} changed its law on magic from {=from} to {=law}.",
        ["MageFled"] = "{mage} fled the law against magic to {@place}.",
        ["WorksDelayed"] = "Work on {structure} stopped.",
        ["WorksCollapsed"] = "{structure} collapsed while it was being built; {=dead} died.",
        ["FortificationBegun"] = "{builder} began {structure} at {@place}.",
        ["FortificationCompleted"] = "{structure} was completed at {@place}.",
        ["RestorationBegun"] = "{builder} began to restore {structure}.",
        ["LabyrinthDelved"] = "{delver} reached level {=level} of {structure}.",
        ["LabyrinthDelveFailed"] = "{delver} was driven back from level {=level} of {structure}.",
        ["LabyrinthCleared"] = "{delver} cleared {structure}.",
        ["RegionConverted"] = "The people of {@place} turned from {old} to {faith}.",
        ["RealmConverted"] = "{realm} took up {faith}.",
        ["HolySiteLost"] = "{faith} lost its holy land of {@place} to {realm}.",
        ["HolySiteRegained"] = "{faith} regained its holy land of {@place}.",
        ["Knighted"] = "{liege} knighted {knight} in the service of {realm}.",
        ["DistinguishedInBattle"] = "{character} won glory in the battle at {@place}.",
        ["WoundedInBattle"] = "{character} was {=wound} in the battle at {@place}.",
        ["CapitalMoved"] = "{realm} moved its seat to {@place}.",
        ["CharacterBorn"] = "{child} was born at {@place} to {mother} and {father}.",
        ["CharacterDied"] = "{deceased} {=death}, aged {=age}.",
        ["ComingOfAge"] = "{character} came of age, dreaming of {=ambition}.",
        ["ChronicleCompacted"] = "the chronicle of {=period} was condensed ({=events} minor events: {=most common}).",
        ["CouncilAppointed"] = "{member} became {=seat} of {realm}.",
        ["DelveFailed"] = "{delver} sought {artifact} in {@place} and failed.",
        ["DuelFought"] = "{challenger} fought {defender} {=stakes} at {@place}.",
        ["EraBegan"] = "{=era} began.",
        ["FoodShortageBegan"] = "Food ran short in {@place}.",
        ["FriendshipFormed"] = "{a} and {b} became friends.",
        ["HolyWarProclaimed"] = "{a} proclaimed a holy war against {b} in the name of {=faith}.",
        ["LabyrinthOpened"] = "{structure} opened in {@place}.",
        ["LandmarkBegun"] = "{builder} began raising {structure} at {@place}.",
        ["LandmarkCompleted"] = "{structure} was completed at {@place}.",
        ["IndependenceDeclared"] = "{realm} threw off the rule of {former liege}.",
        ["MarriageWed"] = "{spouse} and {partner} were wed at {@place}.",
        ["MonsterHuntFailed"] = "{hunter} hunted the monsters of {@place} and was driven back.",
        ["MonstersSlain"] = "{hunter} slew the monsters of {@place}.",
        ["NewLordRose"] = "{lord} rose to rule {@place}.",
        ["PeaceSigned"] = "{war} ended in victory for {victor} over {vanquished}: {=terms}.",
        ["PlagueOutbreak"] = "Plague broke out in {@place}.",
        ["RealmFell"] = "{realm} fell.",
        ["RegionCeded"] = "{@place} passed from {from} to {to}.",
        ["RegionLiberated"] = "{realm} freed {@place}.",
        ["RegionOccupied"] = "{army} took {@place}.",
        ["RevoltBegan"] = "{@place} rose in revolt.",
        ["RivalryBegan"] = "{a} and {b} became rivals over {=over}.",
        ["RivalryDeclared"] = "{a} and {b} declared each other rivals.",
        ["SettlementAbandoned"] = "{@place} was abandoned.",
        ["SettlementSacked"] = "{commander} sacked {@place}; {=dead} died.",
        ["SiegeBegan"] = "{army} laid siege to {@place}.",
        ["SiegeEnded"] = "The siege of {@place} ended: it {=outcome}.",
        ["StructureRuined"] = "{@place} fell into ruin.",
        ["SuccessionOccurred"] = "{heir} succeeded {predecessor} at {@place}.",
        ["TheftFoiled"] = "{victim} caught {thief} reaching for {artifact}.",
        ["WarDeclared"] = "{attacker} declared war on {defender} {=why}, and so began {war}.",
    };

    /// <summary>Templates for events whose payload changes how they read (nobody died; a white peace).</summary>
    private static readonly IReadOnlyList<(string Type, Func<WorldEvent, bool> When, string Template)> Variants = new (string, Func<WorldEvent, bool>, string)[]
    {
        ("MonsterRaid", e => e.Payload.GetValueOrDefault("dead") == "0", "{monster} fell on {@place}."),
        ("WildMagic", e => e.Payload.GetValueOrDefault("dead") is null or "0", "Wild magic broke loose at {@place}: a {=kind}."),
        ("WorksCollapsed", e => e.Payload.GetValueOrDefault("dead") == "0", "{structure} collapsed while it was being built."),
        ("PeaceSigned", e => e.Payload.GetValueOrDefault("outcome") == "white peace", "{war} ended with neither side the victor."),
        ("CharacterDied", e => e.Payload.GetValueOrDefault("age") == "0", "{deceased} {=death} in infancy."),
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    private string? TemplateFor(WorldEvent ev) =>
        Variants.FirstOrDefault(v => v.Type == ev.Type && v.When(ev)).Template ?? Templates.GetValueOrDefault(ev.Type);

    /// <summary>A name as it reads in a sentence: realms take their article, and "The …" names lower it.</summary>
    private string? Prose(EntityRef entity) => _nameOf(entity) is not { } name ? null
        : entity.Kind == Realm.Kind ? "the " + name : English.InProse(name);

    /// <summary>A payload value in words; some keys stand for phrases (a death, a cause of war, peace terms, a cut road).</summary>
    private string? Value(WorldEvent ev, string key) => key switch
    {
        // Lost in a labyrinth reads "died in the Deeps of Tael", not "was killed by" it.
        "death" => !ev.Payload.TryGetValue("cause", out var cause) ? null
            : world.Structures.Values.Any(s => s.Name == cause) ? $"died in {English.InProse(cause)}" : English.Death(cause),
        "age" => ev.Payload.GetValueOrDefault("age"),
        "stakes" => ev.Payload.GetValueOrDefault("stakes") is { } stakes ? stakes.StartsWith("to ", StringComparison.Ordinal) ? stakes : "to " + stakes : null,
        "ambition" => ev.Payload.GetValueOrDefault("ambition") is { } dream ? English.Ambition(dream) : null,
        "why" => ev.Payload.TryGetValue("casus belli", out var kind) ? English.CasusBelli(kind) : null,
        "terms" => ev.Payload.TryGetValue("terms", out var terms) ? English.Terms(terms) : null,
        "cut" => ev.Payload.GetValueOrDefault("reason") switch
        {
            "war" => "by war",
            "occupation" => "where an enemy held the way",
            "danger" => "by danger on the way",
            "raid" => "by a beast's raids",
            { } other => $"by {other}",
            null => null,
        },
        _ => ev.Payload.TryGetValue(key, out var value) ? English.Value(value) : null,
    };

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = TemplateFor(ev) is { } template && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return English.Capitalise(sentence);
    }

    public string Narrate(WorldEvent ev)
    {
        var sentence = TemplateFor(ev) is { } template && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));
        var text = new StringBuilder($"In Year {ev.Date.Year}, {sentence}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true);
        return text.ToString();
    }

    public string Digest(DigestView digest)
    {
        if (digest.EventCount == 0) return "Nothing of note has happened since you last looked.";
        var text = new StringBuilder($"From {digest.From} to {digest.To}, {English.Count(digest.EventCount, "event")} {(digest.EventCount == 1 ? "was" : "were")} recorded. The chronicle remembers:");
        foreach (var group in digest.Groups)
        {
            if (group.Label.Length > 0) text.Append($"\n{group.Label}:");
            foreach (var entry in group.Entries) text.Append($"\n- {Narrate(entry.Event)} [#{entry.Event.Id}]");
        }
        return text.ToString();
    }

    private void Causes(StringBuilder text, ChainNode node, bool first)
    {
        foreach (var cause in node.Causes)
        {
            var connective = first ? "This happened because" : "That followed from what came before:";
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}");
            Causes(text, cause, first: false);
        }
    }

    private string? Fill(string template, WorldEvent ev)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            string? value = key switch
            {
                "@place" => ev.Place is { } place ? Prose(place) : null,
                _ when key.StartsWith('=') => Value(ev, key[1..]),
                _ => ev.Actors.TryGetValue(key, out var actor) ? Prose(actor) : null,
            };
            missing |= value is null;
            return value ?? "";
        });
        return missing ? null : result;
    }

    private string Generic(WorldEvent ev)
    {
        var who = ev.Actors.Select(a => Prose(a.Value)).OfType<string>().ToList();
        var text = Briefs.Spaced(ev.Type);
        if (who.Count > 0) text += $" involving {English.List(who)}";
        if (ev.Place is { } place && Prose(place) is { } where) text += $" at {where}";
        return English.Capitalise(text) + ".";
    }

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}

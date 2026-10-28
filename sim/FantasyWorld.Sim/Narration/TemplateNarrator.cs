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
        ["MonsterRose"] = "{monster}, {=a:breed}, rose in {@place}.",
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
        ["WildMagic"] = "Wild magic broke loose at {@place}: {=a:kind} that killed {=dead}.",
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
        ["DuelDeclared"] = "{challenger} challenged {defender} to a duel ({=stakes}), to be fought at {@place} in a year and a day.",
        ["DuelCalledOff"] = "The duel between {challenger} and {defender} was called off.",
        ["DuelBegun"] = "{challenger} and {defender} began their duel {=stakes} at {@place}, and it was not settled in a day.",
        ["DuelTurned"] = "The duel between {challenger} and {defender} at {@place} went on.",
        ["BattleJoined"] = "At {@place}, {attacker} and {defender} joined a battle of {war} that was not decided in a day.",
        ["BattleTurned"] = "The battle between {attacker} and {defender} at {@place} raged on.",
        ["BattleBrokenOff"] = "The battle between {attacker} and {defender} at {@place} was broken off.",
        ["ArmyMoved"] = "{army} marched from {from} into {@place}.",
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
        ["RevolutionBegan"] = "{leader} led {@place} and its lands in revolution against {realm}, raising {rebels} against the crown.",
        ["RevolutionEnded"] = "The revolution against {realm} ended with {rebels} free of the crown.",
        ["FledIntoExile"] = "{character} fled {from} into exile at {@place}.",
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
        // The land and the towns.
        ["Drought"] = "Drought parched {@place}.",
        ["DroughtEnded"] = "The rains returned to {@place}.",
        ["Flood"] = "The rivers of {@place} burst their banks.",
        ["Blight"] = "Blight struck the fields of {@place}.",
        ["BlightEnded"] = "The blight passed from {@place}.",
        ["PlagueEnded"] = "The plague left {@place}.",
        ["PlagueSpread"] = "Plague spread from {from} to {@place}.",
        ["LeySurge"] = "The ley lines of {@place} surged.",
        ["DangerRose"] = "{@place} grew dangerous.",
        ["DangerSubsided"] = "{@place} grew safer.",
        ["FertilityExhausted"] = "The soil of {@place} was worked out.",
        ["FertilityRecovered"] = "The soil of {@place} recovered.",
        ["FoodShortageEnded"] = "Food was plentiful again in {@place}.",
        ["Earthquake"] = "An earthquake shook {@place}.",
        ["ResourceDiscovered"] = "{=resource} was found in {@place}.",
        ["MonstersMigrated"] = "Monsters from {from} came down into {@place}.",
        ["ElderChosen"] = "{character} was chosen as an elder of {@place}.",
        ["Exodus"] = "{=people} left {@place} for {to}.",
        ["NotablesFled"] = "{=notables} of {from} fled to {@place}.",
        ["RealmRose"] = "{realm} grew into a {=rank}.",
        ["RealmDiminished"] = "{realm} was reduced to a {=rank}.",
        ["StabilityCollapsed"] = "Order broke down in {realm}.",
        ["StabilityRestored"] = "Order returned to {realm}.",
        ["RebelLeaderRose"] = "{character} rose to lead the rebels of {@place}.",
        ["RevoltEnded"] = "The revolt in {@place} ended.",
        ["SettlementGrew"] = "{@place} grew into a {=tier}.",
        ["SettlementDeclined"] = "{@place} dwindled to a {=tier}.",
        ["BuildingCompleted"] = "{@place} finished its {=building}.",
        ["WallsRaised"] = "{@place} raised its walls higher.",
    };

    /// <summary>
    /// Other ways to tell the most common events (sub-project 5 §3). The phrasing is chosen by the event's id, so a
    /// world always reads the same and neighbouring entries of one kind read differently.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Alternates = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["MarriageWed"] = new[] { "{spouse} married {partner} at {@place}.", "At {@place}, {spouse} took {partner} in marriage." },
        ["CharacterBorn"] = new[] { "At {@place}, {mother} bore {father} a child, {child}." },
        ["CharacterDied"] = new[] { "{deceased} {=death} at the age of {=age}." },
        ["WoundedInBattle"] = new[] { "{character} came away {=wound} from the battle at {@place}." },
        ["ComingOfAge"] = new[] { "{character} came of age with dreams of {=ambition}." },
        ["RivalryBegan"] = new[] { "{a} and {b} fell out over {=over}." },
        ["DistinguishedInBattle"] = new[] { "{character} fought with distinction at {@place}." },
        ["BattleFought"] = new[] { "{victor} beat {vanquished} at {@place} in {war}." },
        ["DuelFought"] = new[] { "At {@place}, {challenger} met {defender} {=stakes}." },
        ["DuelDeclared"] = new[] { "{challenger} called {defender} out ({=stakes}); they will meet at {@place} in a year and a day." },
        ["DuelBegun"] = new[] { "At {@place}, {challenger} and {defender} crossed blades {=stakes}, and neither would yield that day." },
        ["DuelTurned"] = new[] { "At {@place}, {challenger} and {defender} fought on." },
        ["BattleJoined"] = new[] { "{attacker} and {defender} met at {@place} in {war}, and the field was not won that day." },
        ["BattleTurned"] = new[] { "At {@place}, the hosts of {attacker} and {defender} fought on." },
        ["BattleBrokenOff"] = new[] { "At {@place}, the fighting between {attacker} and {defender} broke off." },
        ["ArmyMoved"] = new[] { "{army} left {from} and came to {@place}." },
        ["FriendshipFormed"] = new[] { "{a} and {b} grew close.", "A friendship grew between {a} and {b}." },
        ["RegionOccupied"] = new[] { "{@place} fell to {army}." },
        ["ArtifactInherited"] = new[] { "{artifact} passed to {heir}." },
        ["RegionCeded"] = new[] { "{to} gained {@place} from {from}." },
        ["Drought"] = new[] { "No rain fell on {@place}.", "The fields of {@place} withered in a drought." },
        ["Flood"] = new[] { "Floodwaters covered {@place}." },
        ["LeySurge"] = new[] { "Power welled up in the ley lines of {@place}." },
        ["SuccessionOccurred"] = new[] { "At {@place}, {heir} took the seat of {predecessor}." },
        ["PlagueSpread"] = new[] { "The plague reached {@place} from {from}." },
        ["PlagueOutbreak"] = new[] { "Sickness swept through {@place}." },
        ["MonsterRaid"] = new[] { "{monster} attacked {@place}; {=dead} died." },
        ["RegionLiberated"] = new[] { "{@place} was freed by {realm}." },
        ["Knighted"] = new[] { "{knight} was knighted by {liege} to serve {realm}." },
        ["TheftFoiled"] = new[] { "{thief} was caught trying to steal {artifact} from {victim}." },
        ["RegionConverted"] = new[] { "{@place} turned from {old} to {faith}." },
        ["ElderChosen"] = new[] { "{@place} chose {character} as an elder." },
        ["SiegeBegan"] = new[] { "{army} besieged {@place}." },
        ["WarDeclared"] = new[] { "{attacker} went to war against {defender} {=why}: {war}." },
        ["PeaceSigned"] = new[] { "{war} ended; {victor} prevailed over {vanquished}: {=terms}." },
        ["ArtifactStolen"] = new[] { "{artifact} was stolen from {victim} by {thief}." },
        ["Earthquake"] = new[] { "The earth shook in {@place}." },
        ["ResourceDiscovered"] = new[] { "Prospectors found {=resource} in {@place}." },
        ["RouteSevered"] = new[] { "Trade on {route} stopped, cut {=cut}." },
        ["RouteRestored"] = new[] { "Merchants took to {route} again." },
        ["LabyrinthDelveFailed"] = new[] { "{delver} could not get past level {=level} of {structure}." },
        ["MonstersSlain"] = new[] { "{hunter} cleared {@place} of monsters.", "{hunter} hunted down the monsters of {@place}." },
    };

    /// <summary>Short headline titles (present tense, no full stop) for events that lead a digest.</summary>
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WarDeclared"] = "{war} begins",
        ["PeaceSigned"] = "{war} ends",
        ["BattleFought"] = "Battle at {@place}",
        ["RealmFell"] = "{realm} falls",
        ["RealmRose"] = "{realm} rises",
        ["EraBegan"] = "{=era} begins",
        ["IndependenceDeclared"] = "{realm} breaks free",
        ["RegionCeded"] = "{@place} changes hands",
        ["SiegeEnded"] = "The siege of {@place} ends",
        ["SettlementSacked"] = "{@place} is sacked",
        ["SuccessionOccurred"] = "A new lord at {@place}",
        ["CharacterDied"] = "{deceased} is dead",
        ["MonsterRose"] = "A beast rises in {@place}",
        ["MonsterLegendary"] = "{monster} becomes a legend",
        ["MonsterSlain"] = "{monster} is slain",
        ["MonsterRaid"] = "{monster} raids {@place}",
        ["HolyWarProclaimed"] = "A holy war on {b}",
        ["SchismBegan"] = "A schism in {old}",
        ["SaintDeclared"] = "{saint} is made a saint",
        ["RealmConverted"] = "{realm} takes up {faith}",
        ["HolySiteLost"] = "{faith} loses its holy land",
        ["WildMagic"] = "Wild magic at {@place}",
        ["LandmarkCompleted"] = "{structure} stands complete",
        ["WorksCollapsed"] = "{structure} collapses",
        ["LabyrinthOpened"] = "{structure} opens",
        ["LabyrinthCleared"] = "{structure} is cleared",
        ["PlagueOutbreak"] = "Plague in {@place}",
        ["RevoltBegan"] = "Revolt in {@place}",
        ["RevolutionBegan"] = "Revolution in {realm}",
        ["RevolutionEnded"] = "The revolution in {realm} ends",
        ["ArtifactStolen"] = "{artifact} is stolen",
        ["ArtifactForged"] = "{artifact} is forged",
    };

    /// <summary>How a cause is joined to what it caused, by depth (the first layer, then further back).</summary>
    private static readonly string[] FirstConnectives = { "This happened because", "It came about because" };

    private static readonly string[] DeeperConnectives = { "Before that,", "That came about because", "Earlier still," };

    /// <summary>Templates for events whose payload changes how they read (nobody died; a white peace).</summary>
    private static readonly IReadOnlyList<(string Type, Func<WorldEvent, bool> When, string Template)> Variants = new (string, Func<WorldEvent, bool>, string)[]
    {
        ("MonsterRaid", e => e.Payload.GetValueOrDefault("dead") == "0", "{monster} fell on {@place}."),
        ("ArmyMoved", e => e.Payload.GetValueOrDefault("into") == "enemy" && e.Importance > 20, "{army} crossed out of {from} into the enemy's land at {@place}."),
        ("WildMagic", e => e.Payload.GetValueOrDefault("dead") is null or "0", "Wild magic broke loose at {@place}: {=a:kind}."),
        ("WorksCollapsed", e => e.Payload.GetValueOrDefault("dead") == "0", "{structure} collapsed while it was being built."),
        ("PeaceSigned", e => e.Payload.GetValueOrDefault("outcome") == "white peace", "{war} ended with neither side the victor."),
        ("RevolutionEnded", e => e.Payload.GetValueOrDefault("outcome") == "crushed", "{realm} crushed the revolution, and the lands of {rebels} returned to the crown."),
        ("RevolutionEnded", e => e.Payload.GetValueOrDefault("outcome") == "overthrow" && e.Payload.ContainsKey("government") && e.Actors.ContainsKey("ruler"),
            "{leader} overthrew {ruler}, took the throne of {realm} and remade it as {=a:government}."),
        ("RevolutionEnded", e => e.Payload.GetValueOrDefault("outcome") == "overthrow" && e.Actors.ContainsKey("ruler"), "{leader} overthrew {ruler} and took the throne of {realm}."),
        ("RevolutionEnded", e => e.Payload.GetValueOrDefault("outcome") == "overthrow", "{leader} took the throne of {realm}."),
        ("RevolutionEnded", e => e.Actors.ContainsKey("leader") && e.Payload.GetValueOrDefault("outcome") == "breakaway", "The revolution against {realm} ended with {rebels} free of the crown under {leader}."),
        ("CharacterDied", e => e.Payload.GetValueOrDefault("age") == "0", "{deceased} {=death} in infancy."),
        ("MonsterHuntFailed", e => e.Actors.ContainsKey("monster"), "{hunter} hunted {monster} in {@place} and was driven back."),
        ("WallsRaised", e => e.Payload.GetValueOrDefault("level") == "1", "{@place} raised walls."),
        ("MonsterHuntFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death" && e.Actors.ContainsKey("monster"), "{hunter} went after {monster} in {@place} and did not come back."),
        ("MonsterHuntFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{hunter} hunted the monsters of {@place} and was killed."),
        ("LabyrinthDelveFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{delver} went down to level {=level} of {structure} and never returned."),
        ("DelveFailed", e => e.Payload.GetValueOrDefault("outcome") == "failure_death", "{delver} sought {artifact} in {@place} and died there."),
        ("ArtifactInherited", e => !e.Actors.ContainsKey("heir"), "With no heir to claim it, {artifact} went to the treasury of {@place}."),
    };

    private readonly Func<EntityRef, string?> _nameOf = Names.Of(world);

    private string? TemplateFor(WorldEvent ev)
    {
        if (Variants.FirstOrDefault(v => v.Type == ev.Type && v.When(ev)).Template is { } variant) return variant;
        if (!Templates.TryGetValue(ev.Type, out var template)) return null;
        if (!Alternates.TryGetValue(ev.Type, out var others)) return template;
        var pick = (int)(ev.Id % (others.Length + 1));
        return pick == 0 ? template : others[pick - 1];
    }

    /// <summary>A name as it reads in a sentence: realms take their article, and "The …" names lower it.</summary>
    /// <summary>Where the event happened, in prose ("Kesburg", "the Mire of Aul"), if it has a place.</summary>
    public string? PlaceOf(WorldEvent ev) => ev.Place is { } place ? Prose(place) : null;

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
        "over" => ev.Payload.TryGetValue("over", out var over) ? Over(over) : null,
        _ when key.StartsWith("a:", StringComparison.Ordinal) => ev.Payload.TryGetValue(key[2..], out var noun) ? English.WithArticle(noun) : null,
        "people" => ev.Payload.TryGetValue("people", out var many) && long.TryParse(many, out var n) ? n == 1 ? "one person" : $"{English.Number(n)} people" : null,
        "notables" => ev.Payload.TryGetValue("people", out var few) && long.TryParse(few, out var k) ? English.Count(k, "notable") : null,
        "most common" => ev.Payload.TryGetValue("most common", out var common)
            ? English.List(common.Split(", ").Select(part => part.Split(' ', 2) is [var count, var type] ? $"{count} {Briefs.Spaced(type).ToLowerInvariant()}" : part).ToList())
            : null,
        "tier" or "building" or "resource" or "rank" => ev.Payload.TryGetValue(key, out var word) ? word.ToLowerInvariant() : null,
        _ => ev.Payload.TryGetValue(key, out var value) ? English.Value(value) : null,
    };

    /// <summary>The event as one sentence, without date or annotations (headlines, lists).</summary>
    public string Sentence(WorldEvent ev)
    {
        var sentence = TemplateFor(ev) is { } template && Fill(template, ev) is { } filled ? filled : Generic(ev);
        return English.Capitalise(sentence);
    }

    /// <summary>Whether the event type has a headline title of its own (the kinds of news that lead a broadcast).</summary>
    public static bool IsHeadline(string type) => Titles.ContainsKey(type);

    /// <summary>A short headline title ("The War of the Ash begins"), else the event's sentence without its full stop.</summary>
    public string Title(WorldEvent ev)
    {
        var title = Titles.TryGetValue(ev.Type, out var template) && Fill(template, ev) is { } filled ? filled : Sentence(ev).TrimEnd('.');
        return English.Capitalise(title);
    }

    /// <summary>
    /// What a headline's title leaves out: the event told in full (when the title is short), what led to it, and the
    /// engine's note on why or how — never the title again. Without the cause when a story of why follows.
    /// </summary>
    public string Body(WorldEvent ev, EventLog log, bool withCause = true)
    {
        var parts = new List<string>();
        // The full sentence only when it names someone or somewhere the title leaves out.
        var title = Title(ev);
        var sentence = Sentence(ev);
        var names = ev.Actors.Values.Concat(ev.Place is { } at ? new[] { at } : Array.Empty<EntityRef>()).Select(Prose).OfType<string>();
        // ... or when it tells how (a death's cause).
        if (Titles.ContainsKey(ev.Type) && (ev.Payload.ContainsKey("cause")
                || names.Any(n => sentence.Contains(n, StringComparison.OrdinalIgnoreCase) && !title.Contains(n, StringComparison.OrdinalIgnoreCase))))
            parts.Add(sentence);
        if (withCause && ev.Causes.OrderByDescending(c => c.Weight).ThenBy(c => c.EventId).Select(c => log.TryGet(c.EventId, out var cause) ? cause : null)
                .FirstOrDefault(c => c is not null) is { } first)
        {
            parts.Add($"{(first.Date.Year == ev.Date.Year ? "Earlier that year," : $"Before it, in Year {first.Date.Year},")} {Clause(first)}");
        }
        if ((ev.Payload.GetValueOrDefault("motive") ?? ev.Payload.GetValueOrDefault("account") ?? ev.Payload.GetValueOrDefault("lore")) is { } note) parts.Add(note);
        return parts.Count == 0 ? $"{FantasyWorld.Sim.Chronicle.Digest.SeasonLabel(ev.Date)}." : string.Join(" ", parts);
    }

    /// <summary>
    /// The story of why (sub-project 5 §3): a chain told as one paragraph, oldest cause first, ending with the outcome.
    /// </summary>
    public string Story(IReadOnlyList<WorldEvent> chain)
    {
        var ordered = chain.DistinctBy(e => e.Id).OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        if (ordered.Count == 0) return "";
        var text = new StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            var ev = ordered[i];
            var lead = i == 0 ? $"In Year {ev.Date.Year}," : i == ordered.Count - 1 ? (ev.Date.Year == ordered[i - 1].Date.Year ? "And so" : $"And so, in Year {ev.Date.Year},")
                : ev.Date.Year == ordered[i - 1].Date.Year ? "That same year," : $"In Year {ev.Date.Year},";
            if (text.Length > 0) text.Append(' ');
            text.Append(lead).Append(' ').Append(Clause(ev));
        }
        return text.ToString();
    }

    /// <summary>The event as a clause to follow "In Year 12," or "And so": names keep their case, other openings lower theirs.</summary>
    public string Clause(WorldEvent ev) =>
        TemplateFor(ev) is { } template && Fill(template, ev) is { } filled
            ? template.StartsWith('{') ? filled : LowerFirst(filled)
            : LowerFirst(Generic(ev));

    public string Narrate(WorldEvent ev)
    {
        var text = new StringBuilder($"In Year {ev.Date.Year}, {Clause(ev)}");
        if (ev.Payload.TryGetValue("motive", out var motive)) text.Append($" ({motive})");
        if (ev.Payload.TryGetValue("account", out var account)) text.Append($" {account}");
        return text.ToString();
    }

    public string NarrateChain(ChainNode chain)
    {
        var text = new StringBuilder(Narrate(chain.Event));
        Causes(text, chain, first: true, new HashSet<long> { chain.Event.Id });
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

    /// <summary>Each cause once, joined by a connective that varies with depth and event, so a chain does not repeat itself.</summary>
    private void Causes(StringBuilder text, ChainNode node, bool first, HashSet<long> told)
    {
        var second = false;
        foreach (var cause in node.Causes)
        {
            if (!told.Add(cause.Event.Id)) continue;
            // A second cause of the same event is joined as such; otherwise the connective varies with depth and event.
            var choices = first ? FirstConnectives : DeeperConnectives;
            var connective = second ? "And also because" : choices[(int)(cause.Event.Id % choices.Length)];
            text.Append($" {connective} {LowerFirst(Narrate(cause.Event))}"); // "In Year …" becomes "in Year …"
            second = true;
            Causes(text, cause, first: false, told);
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

    /// <summary>What a rivalry is over: "calm vs wrathful" reads as a clash of natures.</summary>
    private static string Over(string over) =>
        over.Split(" vs ") is [var mine, var theirs] ? $"the clash of {English.WithArticle(mine)} nature with {English.WithArticle(theirs)} one" : English.InProse(over);

    /// <summary>Sentences that open with a name keep their capital; others continue the "In year N," clause.</summary>
    private static string LowerFirst(string sentence) =>
        sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..] : sentence;

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Placeholder();
}

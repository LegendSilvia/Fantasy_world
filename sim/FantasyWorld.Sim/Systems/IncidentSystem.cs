using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The beginnings of history (the owner, 2026-10-29: "the root of the problem"), yearly, three to six a year: a council that
/// sees a neighbour weakened and decides whether to strike, stores lost to fire or theft, conspirators meeting in secret, a
/// plot against a life, a labyrinth spilling a beast into the land. Each has a cause, any event of the world or one never
/// told before, and may have a culprit, known at once or hidden; Claude chooses them and tells them (the rules do, offline).
/// What follows grows from them: a war declared, a shortage, a plot carried out or foiled a year later, a beast raiding, and
/// a hidden culprit unmasked in a later year, whom the wronged then hate as much as if they had known all along. The rolls
/// come from the beginning itself, never the world's dice.
/// </summary>
public sealed class IncidentSystem(IDecider? decider = null, IInventor? inventor = null) : ISimSystem
{
    private readonly IDecider _decider = decider ?? new RulesDecider();
    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    /// <summary>The fewest and most beginnings a year, and the most of one kind offered to choose from.</summary>
    public const int Fewest = 3, Most = 6, OfAKind = 4;

    /// <summary>A world with this many independent realms or more has its full three to six beginnings a year.</summary>
    public const int FullWorldRealms = 24;

    /// <summary>A hidden culprit's chance a year of being found out, in a hundred.</summary>
    public const int UnmaskChance = 30;

    /// <summary>How far back a beginning's cause may lie, and how many events are offered as causes.</summary>
    public const int HistoryYears = 10, HistoryLines = 40;

    public string Name => "incidents";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        FollowUp(ctx);
        var world = ctx.World;
        var candidates = Candidates(ctx);
        if (candidates.Count == 0) return;
        // Three to six a year in a world of many realms; a small world has as many as its size allows.
        var wanted = (Fewest + Hash.Percent(Seed(world), 9100, ctx.Date.Year) % (Most - Fewest + 1))
                     * Math.Min(1.0, Realms.Independent(world).Count() / (double)FullWorldRealms);
        var count = (int)wanted + (Hash.Percent(Seed(world), 9108, ctx.Date.Year) < (wanted - (int)wanted) * 100 ? 1 : 0);
        if (count == 0) return;
        var brief = new IncidentBrief(ctx.Date.Year, Math.Min(count, candidates.Count), candidates,
            _inventor is RulesInventor ? new List<HistoryLine>() : History(ctx));
        var chosen = Valid(brief, _inventor.PlanIncidents(brief)) ?? Rules(world, brief);
        foreach (var choice in chosen) Begin(ctx, candidates[choice.Candidate], choice);
    }

    /// <summary>The beginnings the world allows this year, at most <see cref="OfAKind"/> of each kind, varied by the year.</summary>
    public List<IncidentCandidate> Candidates(SimContext ctx)
    {
        var world = ctx.World;
        var year = ctx.Date.Year;
        var found = new List<(string Kind, string Situation, List<IncidentSuspect> Suspects, long Cause, string Title, long Key, string Subject)>();
        var adults = world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child).ToList();

        // A council that sees a neighbour weakened: at war, short of something, or ruled by a child or no one.
        foreach (var realm in Realms.Independent(world).Where(r => !DiplomacySystem.AtWar(world, r)).OrderBy(r => r.Id))
        {
            if (Realms.RulerOf(world, realm) is not { } ruler || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var weak = Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id))
                .Select(n => (Realm: n, Why: Weakness(ctx, n))).FirstOrDefault(n => n.Why != 0);
            if (weak.Realm is null) continue;
            found.Add((Incident.Council, $"the council of {Statecraft.Title(realm)} sees its neighbour {Statecraft.Title(weak.Realm)} weakened "
                                         + $"({Told(ctx, weak.Why)}), and may decide to strike", [], weak.Why, $"The Council of {realm.Name}", realm.Id, realm.Ref.ToString()));
        }

        // Stores lost: a realm's richest stock burnt, flooded or stolen.
        foreach (var realm in Realms.Independent(world).OrderBy(r => r.Id))
        {
            var kind = Stocks.Kinds.Where(k => Stocks.Stock(realm, k) >= 1 && !Stocks.Short(realm, k)).OrderByDescending(k => Stocks.Stock(realm, k)).FirstOrDefault();
            if (kind is null) continue;
            var suspects = Realms.Neighbours(world, realm).Where(n => realm.Has(TreatyKind.Rivalry, n.Id) || DiplomacySystem.Opinion(world, realm, n) <= -20)
                .Select(n => new IncidentSuspect(n.Ref.ToString(), $"the rival realm {Statecraft.Title(n)}")).ToList();
            if (Realms.RulerOf(world, realm) is { } ruler)
                suspects.AddRange(Haters(adults, ruler).Take(2).Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, who hates its ruler {ruler.Name}")));
            var domain = Realms.Domain(world, realm).Select(r => r.Id).ToHashSet();
            if (kind is ResourceKinds.Grain or ResourceKinds.Horses or ResourceKinds.Fish)
                suspects.AddRange(world.Monsters.Values.Where(m => m.State == MonsterState.Roaming && domain.Contains(m.RegionId)).Take(2)
                .Select(m => new IncidentSuspect(m.Ref.ToString(), $"{m.Name}, a {m.Breed} roaming its land")));
            found.Add((Incident.Loss, $"{Statecraft.Title(realm)} may lose its stores of {kind} (fire, flood, blight or theft)", suspects,
                ctx.Log.Involving(realm.Ref).LastOrDefault()?.Id ?? 0, $"The Loss of {realm.Name}'s {Capital(kind)}", realm.Id + 100_000, realm.Ref.ToString()));
        }

        // A plot against a life, by one who hates; and conspirators who share that hatred, meeting in secret.
        foreach (var plotter in adults.OrderBy(c => c.Id))
        {
            var grudge = plotter.Memories.Where(m => m.Feeling <= -50 && world.Characters.TryGetValue(m.About, out var t) && t.IsActive && t.Id != plotter.Id)
                .OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is null) continue;
            var target = world.Characters[grudge.About];
            found.Add((Incident.Plot, $"{plotter.Name} ({Classes.Title(Classes.Of(world, plotter))}), who hates {target.Name}, may plot against their life",
                [new(plotter.Ref.ToString(), plotter.Name)], grudge.EventId, $"The Plot against {target.Name}", plotter.Id + 200_000, target.Ref.ToString()));
            if (adults.FirstOrDefault(o => o.Id > plotter.Id && o.Memories.Any(m => m.About == target.Id && m.Feeling <= -40)) is { } other)
                found.Add((Incident.Meeting, $"{plotter.Name} and {other.Name}, who both hate {target.Name}, may meet in secret",
                    [new(plotter.Ref.ToString(), plotter.Name), new(other.Ref.ToString(), other.Name)], grudge.EventId,
                    $"The Meeting at {world.Settlements[plotter.SettlementId].Name}", plotter.Id + 300_000, target.Ref.ToString()));
        }

        // A labyrinth spilling a beast: woken by those who delved it, or by nothing anyone knows.
        foreach (var deeps in world.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth && s.State == StructureState.Standing && s.ClearedEventId == 0)
                     .OrderBy(s => s.Id))
        {
            var region = world.Regions[deeps.RegionId];
            if (world.Monsters.Values.Any(m => m.State == MonsterState.Roaming && m.RegionId == region.Id)) continue;
            var delvers = ctx.Log.Involving(deeps.Ref).SelectMany(e => e.Actors.Values).Where(r => r.Kind == Character.Kind).Distinct()
                .Select(r => world.Characters.GetValueOrDefault(r.Id)).OfType<Character>().Where(c => c.IsActive).Take(3)
                .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, who delved its depths")).ToList();
            found.Add((Incident.Outbreak, $"a beast may burst from {deeps.Name} into {region.Name}", delvers,
                ctx.Log.Involving(deeps.Ref).LastOrDefault()?.Id ?? deeps.StartedEventId, $"The Outbreak of {Lower(deeps.Name)}", deeps.Id + 400_000, deeps.Ref.ToString()));
        }

        return found.GroupBy(f => f.Kind).SelectMany(g => g.OrderBy(f => Hash.Percent(f.Key, 9101, year)).ThenBy(f => f.Key).Take(OfAKind))
            .Select((f, i) => new IncidentCandidate(i, f.Kind, f.Situation, f.Suspects.DistinctBy(s => s.Ref).ToList(), f.Cause, f.Title, f.Subject)).ToList();
    }

    /// <summary>What weakens a realm, as the event that did it (0 if it stands strong).</summary>
    private static long Weakness(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        if (world.Wars.Values.Where(w => !w.Ended && w.Involves(realm.Id)).OrderByDescending(w => w.StartDay).FirstOrDefault() is { } war)
            return war.DeclaredEventId;
        var hungry = Stocks.Shortages(realm).Select(k => realm.Conditions.GetValueOrDefault(Stocks.ShortPrefix + k)).FirstOrDefault(id => id != 0);
        if (hungry != 0) return hungry;
        var ruler = Realms.RulerOf(world, realm);
        return ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child
            ? ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type is "SuccessionOccurred" or "CharacterDied")?.Id ?? 0
            : 0;
    }

    private static IEnumerable<Character> Haters(IEnumerable<Character> adults, Character of) =>
        adults.Where(c => c.Id != of.Id && c.Memories.Any(m => m.About == of.Id && m.Feeling <= -40)).OrderBy(c => c.Id);

    private static string Told(SimContext ctx, long eventId) =>
        ctx.Log.TryGet(eventId, out var ev) ? new TemplateNarrator(ctx.World).Sentence(ev).TrimEnd('.') : "troubled";

    /// <summary>A name as it reads inside another: "The Deeps of Monreach" → "the Deeps of Monreach".</summary>
    private static string Lower(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name[4..] : name;

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The world's recent great events, any of which may be named as a cause.</summary>
    private static List<HistoryLine> History(SimContext ctx)
    {
        var since = ctx.Date.Day - HistoryYears * WorldDate.DaysPerYear;
        var narrator = new TemplateNarrator(ctx.World);
        var lines = new List<WorldEvent>();
        for (var i = ctx.Log.All.Count - 1; i >= 0 && ctx.Log.All[i].Date.Day >= since; i--)
            if (ctx.Log.All[i].Importance >= 40) lines.Add(ctx.Log.All[i]);
        return lines.OrderByDescending(e => e.Importance).ThenByDescending(e => e.Id).Take(HistoryLines).OrderBy(e => e.Id)
            .Select(e => new HistoryLine(e.Id, $"Year {e.Date.Year}: {narrator.Sentence(e)}")).ToList();
    }

    /// <summary>Claude's choices, if they hold: known candidates, each once, a suspect or no one, a cause from the history or none.</summary>
    private static IReadOnlyList<IncidentChoice>? Valid(IncidentBrief brief, IReadOnlyList<IncidentChoice>? choices)
    {
        if (choices is null || choices.Count == 0 || choices.Count > brief.Count) return null;
        var history = brief.History.Select(h => h.Id).ToHashSet();
        var seen = new HashSet<int>();
        foreach (var c in choices)
            if (c.Candidate < 0 || c.Candidate >= brief.Candidates.Count || !seen.Add(c.Candidate)
                || c.Culprit.Length > 0 && brief.Candidates[c.Candidate].Suspects.All(s => s.Ref != c.Culprit)
                || c.CauseEventId != 0 && !history.Contains(c.CauseEventId) && c.CauseEventId != brief.Candidates[c.Candidate].SuggestedCause
                || c.Title.Length is 0 or > 80)
                return null;
        return choices;
    }

    /// <summary>The rules' own: the first candidates as the year orders them; the cause the rules see; a suspect, often hidden.</summary>
    private static List<IncidentChoice> Rules(World world, IncidentBrief brief) =>
        brief.Candidates.OrderBy(c => Hash.Percent(Seed(world) + c.Index, 9102, brief.Year)).ThenBy(c => c.Index).Take(brief.Count)
            .Select(c =>
            {
                var roll = Hash.Percent(Seed(world) + c.Index, 9103, brief.Year);
                var culprit = c.Suspects.Count > 0 && (c.Kind is Incident.Plot or Incident.Meeting || roll < 60) ? c.Suspects[roll % c.Suspects.Count].Ref : "";
                return new IncidentChoice(c.Index, c.SuggestedCause, null, culprit, culprit.Length > 0 && roll % 2 == 0, c.Title, null);
            }).ToList();

    private void Begin(SimContext ctx, IncidentCandidate candidate, IncidentChoice choice)
    {
        var world = ctx.World;
        var causes = choice.CauseEventId != 0 && ctx.Log.TryGet(choice.CauseEventId, out _) ? new[] { new Cause(choice.CauseEventId, CauseKind.Motive, 0.8) } : null;
        var payload = new Dictionary<string, string> { ["title"] = choice.Title };
        if (choice.CauseText is { Length: > 0 } why) payload["cause"] = why;
        var culprit = Entity(world, choice.Culprit);
        payload["by"] = culprit is null ? "by ill fortune alone" : choice.Hidden ? "the work of a hand no one could name" : $"the work of {NameOf(world, culprit.Value)}";
        var actors = new Dictionary<string, EntityRef>();
        if (culprit is { } known && !choice.Hidden) actors["culprit"] = known;
        using var lore = ctx.Annotate("lore", choice.Lore);
        var year = ctx.Date.Year;
        switch (candidate.Kind)
        {
            case Incident.Council:
                payload.Remove("by");
                Council(ctx, candidate, choice, payload, actors, causes);
                break;
            case Incident.Loss:
            {
                var realm = world.Realms[Entity(world, candidate.Subject)!.Value.Id];
                var kind = Stocks.Kinds.Where(k => Stocks.Stock(realm, k) >= 1 && !Stocks.Short(realm, k)).OrderByDescending(k => Stocks.Stock(realm, k)).First();
                actors["realm"] = realm.Ref;
                payload["resource"] = kind;
                var ev = ctx.Emit(new EventDraft("StoresLost", Actors: actors, Place: world.Settlements[realm.CapitalId].Ref, Payload: payload,
                    Causes: causes, Importance: 50, Tags: new[] { "Economy", "Politics" }));
                Stocks.Use(ctx, realm, kind, Stocks.Stock(realm, kind) + 1, ev.Id);
                Keep(ctx, ev, candidate.Kind, choice, realm.Ref.ToString(), "");
                break;
            }
            case Incident.Plot:
            case Incident.Meeting:
            {
                var plotters = candidate.Suspects.Select(s => Entity(world, s.Ref)!.Value).ToList();
                var target = world.Characters[Entity(world, candidate.Subject)!.Value.Id];
                var plotter = culprit ?? plotters[0];
                WorldEvent ev;
                if (candidate.Kind == Incident.Meeting)
                {
                    actors["a"] = plotters[0];
                    actors["b"] = plotters[1];
                    actors["target"] = target.Ref;
                    ev = ctx.Emit(new EventDraft("ConspiratorsMet", Actors: actors, Place: world.Settlements[world.Characters[plotters[0].Id].SettlementId].Ref,
                        Payload: payload, Causes: causes, Importance: 50, Tags: new[] { "Characters", "Intrigue" }));
                    Fates.Remember(world.Characters[plotters[0].Id], ev.Id, plotters[1].Id, 40, 60);
                    Fates.Remember(world.Characters[plotters[1].Id], ev.Id, plotters[0].Id, 40, 60);
                }
                else
                {
                    actors["target"] = target.Ref;
                    ev = ctx.Emit(new EventDraft("AssassinationPlotted", Actors: actors, Place: world.Settlements[target.SettlementId].Ref,
                        Payload: payload, Causes: causes, Importance: Fates.Importance(target, 45), Tags: new[] { "Characters", "Intrigue" }));
                }
                world.Incidents.Add(new Incident(ev.Id, candidate.Kind, year, plotter.ToString(), choice.Hidden, target.Ref.ToString(), Target: target.Ref.ToString()));
                break;
            }
            case Incident.Outbreak:
            {
                var deeps = world.Structures[Entity(world, candidate.Subject)!.Value.Id];
                var region = world.Regions[deeps.RegionId];
                var breeds = Monster.BreedsOf(region.Biome).Concat(Monster.GuardianBreeds).ToList();
                var breed = breeds[Hash.Percent(deeps.Id, 9104, year) % breeds.Count];
                var id = world.Ids.Take(Monster.Kind);
                var name = $"The {string.Join(' ', breed.Split(' ').Select(Capital))} of {Lower(deeps.Name)}";
                actors["structure"] = deeps.Ref;
                actors["monster"] = new EntityRef(Monster.Kind, id);
                payload["name"] = name;
                payload["breed"] = breed;
                // The beast came out of the deeps: their opening is always among its causes.
                var outbreakCauses = (causes ?? []).Where(c => c.EventId != deeps.StartedEventId)
                    .Append(new Cause(deeps.StartedEventId, CauseKind.Enabler, 0.6)).ToArray();
                var ev = ctx.Emit(new EventDraft("LabyrinthOutbreak", Actors: actors, Place: region.Ref, Payload: payload,
                    Causes: outbreakCauses, Importance: 55, Tags: new[] { "Magic", "Structures" }));
                world.Monsters[id] = new Monster
                {
                    Id = id, Name = name, Breed = breed, RegionId = region.Id, Might = Math.Clamp(40 + region.Danger / 2 + Hash.Percent(id, 9105, year) / 5, 1, 100),
                    BornDay = ctx.Date.Day, State = MonsterState.Roaming, RoseEventId = ev.Id,
                };
                var owner = Realms.SovereignOf(world, region);
                Keep(ctx, ev, candidate.Kind, choice, owner?.Ref.ToString() ?? "", "");
                break;
            }
        }
    }

    private void Council(SimContext ctx, IncidentCandidate candidate, IncidentChoice choice, Dictionary<string, string> payload,
        Dictionary<string, EntityRef> actors, Cause[]? causes)
    {
        var world = ctx.World;
        var realm = world.Realms[Entity(world, candidate.Subject)!.Value.Id];
        var ruler = Realms.RulerOf(world, realm)!;
        var prey = Realms.Neighbours(world, realm).First(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id) && Weakness(ctx, n) != 0);
        var bold = ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave) ? 1.6 : ruler.Has(Traits.Craven) || ruler.Has(Traits.Content) ? 0.5 : 1;
        var odds = Math.Clamp(DiplomacySystem.Strength(world, realm) / Math.Max(1, DiplomacySystem.Strength(world, prey)), 0.3, 2.0);
        var decision = _decider.Decide(new DecisionBrief(ruler.Id, $"the council of {realm.Name}: strike {prey.Name} while it is weak, or wait",
            [new DecisionOption("strike", 50 * bold * odds, $"strike {prey.Name} while it is weak"), new DecisionOption("wait", 60, "wait and keep the peace")]),
            new Rng(unchecked(world.Seed * 31 + (ulong)realm.Id * 7 + (ulong)ctx.Date.Year)));
        actors["realm"] = realm.Ref;
        actors["neighbour"] = prey.Ref;
        payload["decision"] = decision.Key == "strike" ? $"to strike {prey.Name} while it was weak" : "to wait";
        using var motive = ctx.Annotate("motive", decision.Reasoning);
        var council = ctx.Emit(new EventDraft("CouncilConvened", Actors: actors, Place: world.Settlements[realm.CapitalId].Ref, Payload: payload,
            Causes: causes, Importance: decision.Key == "strike" ? 60 : 45, Tags: new[] { "Politics" }));
        if (decision.Key == "strike")
            DiplomacySystem.Declare(ctx, _inventor, realm,
                new CasusBelliOption(CasusBelli.Opportunity, prey.Id, council.Id, DiplomacySystem.Border(world, realm, prey), 0, 0));
    }

    /// <summary>A beginning with a culprit is kept; a known culprit is blamed at once, a hidden one when unmasked.</summary>
    private static void Keep(SimContext ctx, WorldEvent ev, string kind, IncidentChoice choice, string victim, string target)
    {
        if (choice.Culprit.Length == 0) return;
        if (!choice.Hidden) Blame(ctx, ev.Id, choice.Culprit, victim);
        else ctx.World.Incidents.Add(new Incident(ev.Id, kind, ctx.Date.Year, choice.Culprit, true, victim, Target: target));
    }

    /// <summary>The year after: plots carried out or foiled; hidden culprits found out.</summary>
    private static void FollowUp(SimContext ctx)
    {
        var world = ctx.World;
        var year = ctx.Date.Year;
        for (var i = 0; i < world.Incidents.Count; i++)
        {
            var incident = world.Incidents[i];
            if (incident.Target.Length > 0 && incident.Year < year)
                incident = Carry(ctx, incident);
            if (incident.Hidden && !incident.Unmasked && incident.Year < year && Hash.Percent(incident.EventId, 9106, year) < UnmaskChance)
                incident = Unmask(ctx, incident);
            world.Incidents[i] = incident;
        }
        world.Incidents.RemoveAll(x => x.Target.Length == 0 && (!x.Hidden || x.Unmasked) || year - x.Year > 30);
    }

    /// <summary>A plot carried out: the target dies by the plotter's hand, or the plot is uncovered and the plotter named.</summary>
    private static Incident Carry(SimContext ctx, Incident plot)
    {
        var world = ctx.World;
        var plotter = Entity(world, plot.Culprit) is { } p ? world.Characters.GetValueOrDefault(p.Id) : null;
        var target = Entity(world, plot.Target) is { } t ? world.Characters.GetValueOrDefault(t.Id) : null;
        var done = plot with { Target = "" };
        if (plotter is not { IsActive: true } || target is not { IsActive: true }) return done with { Hidden = false };
        var chance = Math.Clamp(40 + (plotter.Attributes.Intrigue - target.Attributes.Intrigue) * 3, 15, 75);
        var cause = new[] { new Cause(plot.EventId, CauseKind.Trigger, 0.9) };
        if (Hash.Percent(plot.EventId, 9107, ctx.Date.Year) < chance)
        {
            Fates.Die(ctx, target, "assassinated", cause, killer: plot.Hidden ? null : plotter);
            return done;
        }
        var foiled = ctx.Emit(new EventDraft("AssassinationFoiled",
            Actors: new Dictionary<string, EntityRef> { ["plotter"] = plotter.Ref, ["target"] = target.Ref },
            Place: world.Settlements[target.SettlementId].Ref, Causes: cause, Importance: Fates.Importance(target, 50), Tags: new[] { "Characters", "Intrigue" }));
        Fates.Remember(target, foiled.Id, plotter.Id, -90, 90);
        return done with { Hidden = false };
    }

    /// <summary>A hidden culprit found out: the wronged now know whom to hate, and what follows from that follows.</summary>
    private static Incident Unmask(SimContext ctx, Incident incident)
    {
        var world = ctx.World;
        if (Entity(world, incident.Culprit) is not { } culprit || !Alive(world, culprit)) return incident with { Unmasked = true };
        var actors = new Dictionary<string, EntityRef> { ["culprit"] = culprit };
        if (Entity(world, incident.Victim) is { } victim && Alive(world, victim)) actors["victim"] = victim;
        var title = ctx.Log.TryGet(incident.EventId, out var first) ? first.Payload.GetValueOrDefault("title") ?? "it" : "it";
        var unmasked = ctx.Emit(new EventDraft("CulpritUnmasked", Actors: actors, Place: first?.Place,
            Payload: new Dictionary<string, string> { ["title"] = title },
            Causes: new[] { new Cause(incident.EventId, CauseKind.Trigger, 0.9) }, Importance: 60, Tags: new[] { "Intrigue" }));
        Blame(ctx, unmasked.Id, incident.Culprit, incident.Victim);
        return incident with { Unmasked = true };
    }

    /// <summary>The wronged hate the culprit: a realm its neighbour (a grudge it may go to war over), a person their foe.</summary>
    private static void Blame(SimContext ctx, long eventId, string culpritRef, string victimRef)
    {
        var world = ctx.World;
        if (Entity(world, culpritRef) is not { } culprit || Entity(world, victimRef) is not { } victim) return;
        var wronged = victim.Kind == Realm.Kind && world.Realms.TryGetValue(victim.Id, out var realm) ? Realms.RulerOf(world, realm)
            : world.Characters.GetValueOrDefault(victim.Id);
        if (culprit.Kind == Realm.Kind && victim.Kind == Realm.Kind && world.Realms.TryGetValue(victim.Id, out var wrongedRealm))
            Statecraft.Remember(wrongedRealm, eventId, culprit.Id, -60, 80);
        else if (culprit.Kind == Character.Kind && wronged is { IsActive: true } && wronged.Id != culprit.Id)
            Fates.Remember(wronged, eventId, culprit.Id, -80, 90);
        if (victim.Kind == Character.Kind && world.Characters.TryGetValue(victim.Id, out var dead) && !dead.IsActive && culprit.Kind == Character.Kind)
            foreach (var kin in dead.Children.Append(dead.SpouseId).Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>().Where(k => k.IsActive))
                Fates.Remember(kin, eventId, culprit.Id, -80, 90);
    }

    private static long Seed(World world) => unchecked((long)world.Seed);

    private static bool Alive(World world, EntityRef r) => r.Kind switch
    {
        Character.Kind => world.Characters.TryGetValue(r.Id, out var c) && c.IsActive,
        Realm.Kind => world.Realms.TryGetValue(r.Id, out var realm) && !realm.Destroyed,
        Monster.Kind => world.Monsters.TryGetValue(r.Id, out var m) && m.Alive,
        _ => false,
    };

    public static EntityRef? Entity(World world, string reference) =>
        reference.Split('#') is [var kind, var id] && long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? new EntityRef(kind, n) : null;

    private static string NameOf(World world, EntityRef r) => r.Kind switch
    {
        Character.Kind => world.Characters.GetValueOrDefault(r.Id)?.Name ?? "someone",
        Realm.Kind => world.Realms.TryGetValue(r.Id, out var realm) ? Statecraft.Title(realm) : "a realm",
        Monster.Kind => world.Monsters.GetValueOrDefault(r.Id)?.Name ?? "a beast",
        _ => "someone",
    };
}

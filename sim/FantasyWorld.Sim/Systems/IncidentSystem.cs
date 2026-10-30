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
    public const int Fewest = 3, Most = 6, OfAKind = 3;

    /// <summary>A world with this many independent realms or more has its full three to six beginnings a year.</summary>
    public const int FullWorldRealms = 12;

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

        // The wider pool (the owner, 2026-10-29): theft, abduction, poison, forgery, insult, sedition, sacrilege, raids, quests,
        // pretenders and murdered envoys.
        var independent = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var artifact in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held).OrderBy(a => a.Id))
        {
            if (!world.Characters.TryGetValue(artifact.HolderId, out var holder) || !holder.IsActive) continue;
            var thieves = adults.Where(c => c.Id != holder.Id && (c.Attributes.Intrigue >= 14 || Classes.Base(c.Class) == Classes.Rogue)
                                            && c.Memories.Any(m => m.About == holder.Id && m.Feeling <= -20)).Take(3)
                .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, a {Classes.Title(Classes.Of(world, c))} who resents {holder.Name}")).ToList();
            if (thieves.Count == 0)
                thieves = adults.Where(c => c.Id != holder.Id && Classes.Base(c.Class) == Classes.Rogue && c.SettlementId == holder.SettlementId).Take(2)
                    .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, a rogue of {world.Settlements[c.SettlementId].Name}")).ToList();
            found.Add((Incident.Theft, $"{artifact.Name}, held by {holder.Name}, may be stolen", thieves, artifact.OriginEventId,
                $"The Theft of {Lower(artifact.Name)}", artifact.Id + 500_000, artifact.Ref.ToString()));
        }
        foreach (var realm in independent)
        {
            if (Realms.RulerOf(world, realm) is not { } ruler) continue;
            var neighbours = Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed).ToList();
            var hostile = neighbours.Where(n => realm.Has(TreatyKind.Rivalry, n.Id) || DiplomacySystem.Opinion(world, realm, n) <= -20).ToList();
            var rivals = hostile.Select(n => new IncidentSuspect(n.Ref.ToString(), $"the hostile realm {Statecraft.Title(n)}")).ToList();
            var cause = ctx.Log.Involving(realm.Ref).LastOrDefault()?.Id ?? 0;
            // An heir taken.
            if (ruler.Children.Select(id => world.Characters.GetValueOrDefault(id)).OfType<Character>().FirstOrDefault(c => c.IsActive) is { } heir)
            {
                var takers = hostile.Select(n => Realms.RulerOf(world, n)).OfType<Character>()
                    .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, who rules a hostile neighbour"))
                    .Concat(Haters(adults, ruler).Take(2).Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, who hates {ruler.Name}"))).ToList();
                if (takers.Count > 0)
                    found.Add((Incident.Abduction, $"{heir.Name}, child of {ruler.Name} of {Statecraft.Title(realm)}, may be taken", takers, cause,
                        $"The Taking of {heir.Name}", heir.Id + 600_000, heir.Ref.ToString()));
            }
            // A town's wells poisoned, or a sickness loosed.
            if (world.Settlements.Values.Where(t => !t.Abandoned && world.Regions[t.RegionId].OwnerRealmId == realm.Id).OrderByDescending(t => t.Population).FirstOrDefault() is { } town)
            {
                var poisoners = rivals.Concat(adults.Where(c => Classes.Base(c.Class) == Classes.Warlock && c.Memories.Any(m => m.About == ruler.Id && m.Feeling <= -20)).Take(2)
                    .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, a warlock with a grudge"))).ToList();
                found.Add((Incident.Blight, $"the wells of {town.Name} in {Statecraft.Title(realm)} may be poisoned, or a sickness loosed", poisoners, cause,
                    $"The Sickness of {town.Name}", town.Id + 700_000, town.Ref.ToString()));
            }
            // An alliance broken by a forged letter.
            foreach (var ally in neighbours.Where(n => n.Id > realm.Id && realm.Has(TreatyKind.Alliance, n.Id)).Take(1))
            {
                var forgers = independent.Where(o => o.Id != realm.Id && o.Id != ally.Id && (o.Has(TreatyKind.Rivalry, realm.Id) || o.Has(TreatyKind.Rivalry, ally.Id)))
                    .Select(o => new IncidentSuspect(o.Ref.ToString(), $"{Statecraft.Title(o)}, rival of one of them")).ToList();
                found.Add((Incident.Forgery, $"a forged letter may set the allies {Statecraft.Title(realm)} and {Statecraft.Title(ally)} against each other",
                    forgers, cause, $"The Forged Letter of {realm.Name}", realm.Id + 800_000, $"{realm.Ref}|{ally.Ref}"));
            }
            // A lord who stirs the people against the crown.
            if (Stocks.Contentment(world, realm) < 50
                && world.Settlements.Values.Where(t => !t.Abandoned && t.LordId != 0 && t.LordId != ruler.Id && world.Regions[t.RegionId].OwnerRealmId == realm.Id)
                    .Select(t => world.Characters.GetValueOrDefault(t.LordId)).OfType<Character>()
                    .FirstOrDefault(l => l.IsActive && (l.Has(Traits.Ambitious) || l.Memories.Any(m => m.About == ruler.Id && m.Feeling <= -20))) is { } seditious)
                found.Add((Incident.Sedition, $"{seditious.Name}, a discontented lord of {Statecraft.Title(realm)}, may stir the people against {ruler.Name}",
                    [new(seditious.Ref.ToString(), seditious.Name)], cause, $"The Whispering at {world.Settlements[seditious.SettlementId].Name}",
                    seditious.Id + 900_000, realm.Ref.ToString()));
            // A shrine defiled by a neighbour of another faith.
            var domain = Realms.Domain(world, realm).Select(r => r.Id).ToHashSet();
            var heathens = neighbours.Where(n => n.FaithId != realm.FaithId).Select(n => new IncidentSuspect(n.Ref.ToString(), $"{Statecraft.Title(n)}, of another faith")).ToList();
            if (heathens.Count > 0)
            {
                var holy = world.Faiths.GetValueOrDefault(realm.FaithId) is { } own && domain.Contains(own.HolyRegionId) ? world.Regions[own.HolyRegionId] : null;
                if (world.Structures.Values.FirstOrDefault(x => x.Type == StructureKind.Shrine && x.State == StructureState.Standing && domain.Contains(x.RegionId)) is { } shrine)
                    found.Add((Incident.Sacrilege, $"{shrine.Name} in {Statecraft.Title(realm)} may be defiled", heathens, shrine.StartedEventId,
                        $"The Defiling of {Lower(shrine.Name)}", shrine.Id + 1_000_000, $"{shrine.Ref}|{realm.Ref}"));
                else if (holy is not null)
                    found.Add((Incident.Sacrilege, $"the holy places of {holy.Name} in {Statecraft.Title(realm)} may be defiled", heathens, cause,
                        $"The Defiling of {holy.Name}", holy.Id + 1_000_000, $"{holy.Ref}|{realm.Ref}"));
            }
            // A border town burnt by raiders.
            foreach (var foe in hostile.Where(n => !world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id) && w.Involves(n.Id))).Take(1))
            {
                var graph = world.Geography?.Graph;
                var theirs = Realms.Territory(world, foe).Select(r => r.Id).ToHashSet();
                if (graph is not null && world.Settlements.Values.Where(t => !t.Abandoned && world.Regions[t.RegionId].OwnerRealmId == realm.Id
                                                                            && graph.Neighbours(t.RegionId).Any(theirs.Contains))
                        .OrderBy(t => t.Id).FirstOrDefault() is { } burnt)
                    found.Add((Incident.Raid, $"raiders from over the border of {Statecraft.Title(foe)} may burn {burnt.Name} in {Statecraft.Title(realm)}",
                        [new(foe.Ref.ToString(), $"{Statecraft.Title(foe)}, across the border")], cause, $"The Burning of {burnt.Name}", burnt.Id + 1_100_000, burnt.Ref.ToString()));
            }
            // A kinsman who presses a claim to the throne.
            if (Fates.Siblings(world, ruler).FirstOrDefault(k => k.IsActive && k.Stage(ctx.Date) != LifeStage.Child && (k.Has(Traits.Ambitious) || k.Has(Traits.Envious))) is { } pretender)
                found.Add((Incident.Pretender, $"{pretender.Name}, kin of {ruler.Name}, may press a claim to the throne of {Statecraft.Title(realm)}",
                    [new(pretender.Ref.ToString(), pretender.Name)], Fates.Accession(ctx.Log, ruler)?.Id ?? cause, $"The Claim of {pretender.Name}",
                    pretender.Id + 1_200_000, ruler.Ref.ToString()));
            // An envoy slain at a neighbour's court.
            foreach (var host in neighbours.Where(n => !realm.Has(TreatyKind.Alliance, n.Id) && !DiplomacySystem.AtWar(world, n)).Take(1))
            {
                var killers = new List<IncidentSuspect> { new(host.Ref.ToString(), $"{Statecraft.Title(host)}, the host") };
                killers.AddRange(independent.Where(o => o.Id != realm.Id && o.Id != host.Id && o.Has(TreatyKind.Rivalry, realm.Id))
                    .Select(o => new IncidentSuspect(o.Ref.ToString(), $"{Statecraft.Title(o)}, a rival who wants them at war")));
                found.Add((Incident.Envoy, $"the envoy of {Statecraft.Title(realm)} may be slain at the court of {Statecraft.Title(host)}", killers, cause,
                    $"The Murder of {realm.Name}'s Envoy", realm.Id + 1_300_000, $"{realm.Ref}|{host.Ref}"));
            }
        }
        // Inspired by the old stories (Polti's dramatic situations; the Red Wedding, Romeo and Juliet, Monte Cristo).
        foreach (var host in independent)
        {
            if (Realms.RulerOf(world, host) is not { } hostRuler || !(hostRuler.Has(Traits.Cruel) || hostRuler.Has(Traits.Deceitful))) continue;
            foreach (var guestRealm in Realms.Neighbours(world, host).Where(n => n.LiegeId == 0 && !n.Destroyed
                         && (host.Has(TreatyKind.Rivalry, n.Id) || DiplomacySystem.Opinion(world, host, n) <= -30)).Take(1))
                if (Realms.RulerOf(world, guestRealm) is { IsActive: true } guest)
                    found.Add((Incident.Betrayal, $"{hostRuler.Name} of {Statecraft.Title(host)} may invite {guest.Name} of {Statecraft.Title(guestRealm)} to a feast, and break the guest-right",
                        [new(hostRuler.Ref.ToString(), hostRuler.Name)], host.Memories.Where(m => m.About == guestRealm.Id).OrderBy(m => m.Feeling).FirstOrDefault()?.EventId ?? 0,
                        $"The Feast of {world.Settlements[host.CapitalId].Name}", guest.Id + 1_600_000, guest.Ref.ToString()));
        }
        var single = adults.Where(c => FamilySystem.Marriageable(c, ctx.Date) && c.Age(ctx.Date) < 30).OrderBy(c => c.Id).ToList();
        foreach (var a in single.Where(c => c.Sex == Sex.Female))
        {
            var mine = RealmOf(world, a);
            if (mine is null) continue;
            if (single.FirstOrDefault(b => b.Sex == Sex.Male && !Relations.AreKin(a, b) && RealmOf(world, b) is { } theirs && theirs.Id != mine.Id
                                           && (mine.Has(TreatyKind.Rivalry, theirs.Id) || DiplomacySystem.Opinion(world, mine, theirs) <= -30)) is { } b)
                found.Add((Incident.Lovers, $"{a.Name} of {Statecraft.Title(mine)} and {b.Name} of the hostile {Statecraft.Title(RealmOf(world, b)!)} may fall in love and run away together",
                    [], 0, $"The Flight of {a.Name} and {b.Name}", a.Id + 1_700_000, $"{a.Ref}|{b.Ref}"));
        }
        foreach (var accused in adults.Where(c => c.Renown >= 20).OrderBy(c => c.Id))
        {
            var accusers = Haters(adults, accused).Where(c => c.Attributes.Intrigue >= 10).Take(2)
                .Select(c => new IncidentSuspect(c.Ref.ToString(), $"{c.Name}, who hates {accused.Name}")).ToList();
            if (accusers.Count > 0)
                found.Add((Incident.Framed, $"{accused.Name} may be falsely accused of a crime and driven out", accusers,
                    accused.Memories.OrderBy(m => m.Feeling).FirstOrDefault()?.EventId ?? 0, $"The Accusation of {accused.Name}", accused.Id + 1_800_000, accused.Ref.ToString()));
        }

        // An insult given at a feast, between two who already dislike each other.
        foreach (var proud in adults.Where(c => c.Renown >= 15).OrderBy(c => c.Id))
            if (proud.Memories.Where(m => m.Feeling is < 0 and > -60 && world.Characters.TryGetValue(m.About, out var o) && o.IsActive && o.Id != proud.Id && o.Renown >= 10)
                    .OrderBy(m => m.EventId).FirstOrDefault() is { } dislike)
            {
                var other = world.Characters[dislike.About];
                found.Add((Incident.Insult, $"{proud.Name} and {other.Name}, who dislike each other, may trade insults before all at a feast",
                    [new(proud.Ref.ToString(), proud.Name), new(other.Ref.ToString(), other.Name)], dislike.EventId,
                    $"The Feast at {world.Settlements[proud.SettlementId].Name}", proud.Id + 1_400_000, ""));
            }
        // A quest sworn: a bold soul sets out for a labyrinth's treasure.
        var deepest = world.Structures.Values.Where(x => x.Type == StructureKind.Labyrinth && x.State == StructureState.Standing && x.ClearedEventId == 0).OrderBy(x => x.Id).ToList();
        if (deepest.Count > 0)
            foreach (var bold in adults.Where(c => c.Attributes.Prowess >= 14 && c.Ambition != Ambitions.Artifact && c.Role is Role.Knight or Role.Adventurer).OrderBy(c => c.Id))
            {
                var deeps = deepest[(int)(bold.Id % deepest.Count)];
                found.Add((Incident.Quest, $"{bold.Name} may swear to win the treasure of {deeps.Name}", [new(bold.Ref.ToString(), bold.Name)],
                    deeps.StartedEventId, $"The Oath of {bold.Name}", bold.Id + 1_500_000, deeps.Ref.ToString()));
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

    /// <summary>A lord stays at their seat; only those who hold none can be carried off or driven out.</summary>
    private static bool IsLord(World world, Character c) => world.Settlements.Values.Any(t => t.LordId == c.Id);

    private static Realm? RealmOf(World world, Character c) =>
        world.Settlements.TryGetValue(c.SettlementId, out var home) ? Realms.SovereignOf(world, world.Regions[home.RegionId]) : null;

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
                var culprit = c.Suspects.Count > 0 && (Incident.Done.Contains(c.Kind) || roll < 60) ? c.Suspects[roll % c.Suspects.Count].Ref : "";
                return new IncidentChoice(c.Index, c.SuggestedCause, null, culprit, culprit.Length > 0 && roll % 2 == 0, c.Title, null);
            }).ToList();

    private void Begin(SimContext ctx, IncidentCandidate candidate, IncidentChoice choice)
    {
        var world = ctx.World;
        // Someone's doing always has a doer, and what is done in the open is never hidden.
        if (Incident.Done.Contains(candidate.Kind) && choice.Culprit.Length == 0 && candidate.Suspects.Count > 0)
            choice = choice with { Culprit = candidate.Suspects[0].Ref };
        if (Incident.Open.Contains(candidate.Kind)) choice = choice with { Hidden = false };
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
                    var kind = Stocks.Kinds.Where(k => Stocks.Stock(realm, k) >= 1 && !Stocks.Short(realm, k)).OrderByDescending(k => Stocks.Stock(realm, k)).FirstOrDefault();
                    if (realm.Destroyed || kind is null) return;
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
                    if (!target.IsActive || plotters.Any(p => !world.Characters[p.Id].IsActive)) return;
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
                        Id = id,
                        Name = name,
                        Breed = breed,
                        RegionId = region.Id,
                        Might = Math.Clamp(40 + region.Danger / 2 + Hash.Percent(id, 9105, year) / 5, 1, 100),
                        BornDay = ctx.Date.Day,
                        State = MonsterState.Roaming,
                        RoseEventId = ev.Id,
                    };
                    var owner = Realms.SovereignOf(world, region);
                    Keep(ctx, ev, candidate.Kind, choice, owner?.Ref.ToString() ?? "", "");
                    break;
                }
            default:
                More(ctx, candidate, choice, payload, actors, causes, culprit);
                break;
        }
    }

    /// <summary>The wider pool's beginnings (the owner, 2026-10-29), each with what it does to the world and whom it wrongs.</summary>
    private static void More(SimContext ctx, IncidentCandidate candidate, IncidentChoice choice, Dictionary<string, string> payload,
        Dictionary<string, EntityRef> actors, Cause[]? causes, EntityRef? culprit)
    {
        var world = ctx.World;
        var subjects = candidate.Subject.Split('|').Select(r => Entity(world, r)).OfType<EntityRef>().ToList();
        var doer = culprit is { Kind: Character.Kind } c && world.Characters.TryGetValue(c.Id, out var d) && d.IsActive ? d : null;
        if (Incident.Done.Contains(candidate.Kind) && doer is null) return; // the doer is gone
        WorldEvent Emit(string type, EntityRef place, int importance, params string[] tags) =>
            ctx.Emit(new EventDraft(type, Actors: actors, Place: place, Payload: payload, Causes: causes, Importance: importance, Tags: tags));
        EntityRef SeatOf(Realm realm) => world.Settlements[realm.CapitalId].Ref;
        switch (candidate.Kind)
        {
            case Incident.Theft:
                {
                    var artifact = world.Artifacts[subjects[0].Id];
                    if (artifact.Place != ArtifactPlace.Held || !world.Characters.TryGetValue(artifact.HolderId, out var holder) || !holder.IsActive
                        || holder.Id == doer?.Id) return;
                    actors["artifact"] = artifact.Ref;
                    actors["holder"] = holder.Ref;
                    var ev = Emit("ArtifactTaken", world.Settlements[holder.SettlementId].Ref, Math.Max(50, Fates.Importance(holder, 45)), "Artifacts", "Intrigue");
                    // Into the thief's hands, or, with no thief, lost in the land (to be found again by delvers).
                    if (doer is not null) Fates.MoveToHolder(artifact, doer, ev.Id, "stolen");
                    else Fates.Lose(artifact, world.Settlements[holder.SettlementId].RegionId, ev.Id, holder.Id);
                    Keep(ctx, ev, candidate.Kind, choice, holder.Ref.ToString(), "");
                    break;
                }
            case Incident.Abduction:
                {
                    var child = world.Characters[subjects[0].Id];
                    if (world.Characters.Values.FirstOrDefault(p => p.Children.Contains(child.Id) && p.IsActive) is not { } parent || !child.IsActive) return;
                    actors["child"] = child.Ref;
                    actors["parent"] = parent.Ref;
                    var ev = Emit("KinTaken", world.Settlements[child.SettlementId].Ref, Math.Max(55, Fates.Importance(parent, 45)), "Characters", "Intrigue");
                    if (doer is not null && !IsLord(world, child)) child.SettlementId = doer.SettlementId; // held where the taker lives
                    if (doer is not null) Fates.Remember(child, ev.Id, doer.Id, -60, 70);
                    Keep(ctx, ev, candidate.Kind, choice, parent.Ref.ToString(), "");
                    break;
                }
            case Incident.Blight:
                {
                    var town = world.Settlements[subjects[0].Id];
                    var realm = world.Realms[world.Regions[town.RegionId].OwnerRealmId];
                    actors["realm"] = realm.Ref;
                    var lost = town.Adults / 10;
                    town.Adults -= lost;
                    town.Unrest = Math.Min(100, town.Unrest + 25);
                    payload["dead"] = lost.ToString(CultureInfo.InvariantCulture);
                    var ev = Emit("TownSickened", town.Ref, 50, "Disaster");
                    Keep(ctx, ev, candidate.Kind, choice, realm.Ref.ToString(), "");
                    break;
                }
            case Incident.Forgery:
                {
                    var (a, b) = (world.Realms[subjects[0].Id], world.Realms[subjects[1].Id]);
                    actors["a"] = a.Ref;
                    actors["b"] = b.Ref;
                    var ev = Emit("LetterForged", SeatOf(a), 55, "Politics", "Intrigue");
                    a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
                    b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
                    Statecraft.Remember(a, ev.Id, b.Id, -45, 70);
                    Statecraft.Remember(b, ev.Id, a.Id, -45, 70);
                    Keep(ctx, ev, candidate.Kind, choice, $"{a.Ref}|{b.Ref}", "");
                    break;
                }
            case Incident.Insult:
                {
                    var other = candidate.Suspects.Select(x => Entity(world, x.Ref)!.Value).First(r => r != culprit);
                    actors["insulted"] = other;
                    var ev = Emit("InsultGiven", world.Settlements[doer!.SettlementId].Ref, Math.Max(45, Fates.Importance(doer, 40)), "Characters");
                    Fates.Remember(world.Characters[other.Id], ev.Id, doer.Id, -55, 80);
                    break;
                }
            case Incident.Sedition:
                {
                    var realm = world.Realms[subjects[0].Id];
                    actors["realm"] = realm.Ref;
                    var seat = world.Settlements[doer!.SettlementId];
                    seat.Unrest = Math.Min(100, seat.Unrest + 35);
                    var ev = Emit("SeditionStirred", seat.Ref, 50, "Politics", "Intrigue");
                    Keep(ctx, ev, candidate.Kind, choice, Realms.RulerOf(world, realm)?.Ref.ToString() ?? "", "");
                    break;
                }
            case Incident.Sacrilege:
                {
                    var realm = world.Realms[subjects[1].Id];
                    var region = subjects[0].Kind == Structure.Kind ? world.Regions[world.Structures[subjects[0].Id].RegionId] : world.Regions[subjects[0].Id];
                    actors["holy"] = subjects[0];
                    actors["realm"] = realm.Ref;
                    var ev = Emit("ShrineDefiled", region.Ref, 55, "Faith");
                    if (world.Faiths.TryGetValue(realm.FaithId, out var faith)) faith.Zeal = Math.Min(100, faith.Zeal + 15);
                    Keep(ctx, ev, candidate.Kind, choice, realm.Ref.ToString(), "");
                    break;
                }
            case Incident.Raid:
                {
                    var town = world.Settlements[subjects[0].Id];
                    var realm = world.Realms[world.Regions[town.RegionId].OwnerRealmId];
                    actors["realm"] = realm.Ref;
                    // Its fortification (the town's level) keeps the raiders from the worst of it.
                    var ward = Levels.Ward(Levels.Of(world, town));
                    town.Wealth *= 1 - 0.3 * ward;
                    town.Adults -= (int)(town.Adults / 20 * ward);
                    town.Unrest = Math.Min(100, town.Unrest + 20);
                    var ev = Emit("BorderRaided", town.Ref, 50, "War");
                    Keep(ctx, ev, candidate.Kind, choice, realm.Ref.ToString(), "");
                    break;
                }
            case Incident.Quest:
                {
                    var deeps = world.Structures[subjects[0].Id];
                    actors["structure"] = deeps.Ref;
                    doer!.Ambition = Ambitions.Artifact;
                    Emit("QuestSworn", world.Settlements[doer.SettlementId].Ref, Math.Max(45, Fates.Importance(doer, 40)), "Characters", "Deeds");
                    break;
                }
            case Incident.Pretender:
                {
                    var ruler = world.Characters[subjects[0].Id];
                    actors["ruler"] = ruler.Ref;
                    var ev = Emit("ClaimPressed", world.Settlements[ruler.SettlementId].Ref, Math.Max(55, Fates.Importance(ruler, 45)), "Politics", "Characters");
                    Fates.Remember(doer!, ev.Id, ruler.Id, -60, 80);
                    Fates.Remember(ruler, ev.Id, doer!.Id, -50, 80);
                    doer.Ambition = Ambitions.Renown;
                    break;
                }
            case Incident.Betrayal:
                {
                    var guest = world.Characters[subjects[0].Id];
                    if (!guest.IsActive) return;
                    actors["guest"] = guest.Ref;
                    var ev = Emit("GuestRightBroken", world.Settlements[doer!.SettlementId].Ref, Math.Max(65, Fates.Importance(guest, 55)), "Characters", "Politics");
                    Fates.Die(ctx, guest, "slain at a feast under the guest-right", [new Cause(ev.Id, CauseKind.Trigger, 1.0)], killer: doer);
                    if (RealmOf(world, doer) is { } host && RealmOf(world, guest) is { } wronged && host.Id != wronged.Id)
                        Statecraft.Remember(wronged, ev.Id, host.Id, -80, 90);
                    break;
                }
            case Incident.Lovers:
                {
                    var (a, b) = (world.Characters[subjects[0].Id], world.Characters[subjects[1].Id]);
                    if (!FamilySystem.Marriageable(a, ctx.Date) || !FamilySystem.Marriageable(b, ctx.Date)) return;
                    actors["a"] = a.Ref;
                    actors["b"] = b.Ref;
                    var ev = Emit("LoversEloped", world.Settlements[a.SettlementId].Ref, 50, "Characters");
                    FamilySystem.Wed(ctx, a, b);
                    // Each house holds the other's child to blame.
                    foreach (var (child, other) in new[] { (a, b), (b, a) })
                        foreach (var kin in world.Characters.Values.Where(p => p.IsActive && p.Children.Contains(child.Id)))
                            Fates.Remember(kin, ev.Id, other.Id, -40, 60);
                    break;
                }
            case Incident.Framed:
                {
                    var accused = world.Characters[subjects[0].Id];
                    if (!accused.IsActive) return;
                    actors["accused"] = accused.Ref;
                    var ev = Emit("FalselyAccused", world.Settlements[accused.SettlementId].Ref, Math.Max(50, Fates.Importance(accused, 45)), "Characters", "Intrigue");
                    // Driven out to a far town; they will know whom to hate once the truth is out (or at once, if the accuser was open).
                    if (!IsLord(world, accused) && world.Settlements.Values.Where(t => !t.Abandoned && t.Id != accused.SettlementId && RealmOf(world, accused) is { } home
                                                             && Realms.SovereignOf(world, world.Regions[t.RegionId])?.Id != home.Id)
                            .OrderBy(t => Hash.Percent(accused.Id, 9110, t.Id)).FirstOrDefault() is { } exile)
                        accused.SettlementId = exile.Id;
                    accused.Renown = Math.Max(0, accused.Renown - 20);
                    Keep(ctx, ev, candidate.Kind, choice, accused.Ref.ToString(), "");
                    break;
                }
            case Incident.Envoy:
                {
                    var (sender, host) = (world.Realms[subjects[0].Id], world.Realms[subjects[1].Id]);
                    actors["realm"] = sender.Ref;
                    actors["host"] = host.Ref;
                    var ev = Emit("EnvoySlain", SeatOf(host), 55, "Politics", "Diplomacy");
                    // The sender blames the host, whoever truly did it; the truth may come out later.
                    Statecraft.Remember(sender, ev.Id, host.Id, -50, 80);
                    Keep(ctx, ev, candidate.Kind, choice, sender.Ref.ToString(), "");
                    break;
                }
        }
    }

    private void Council(SimContext ctx, IncidentCandidate candidate, IncidentChoice choice, Dictionary<string, string> payload,
        Dictionary<string, EntityRef> actors, Cause[]? causes)
    {
        var world = ctx.World;
        // Earlier beginnings this year may have changed things: what no longer holds does not happen.
        var realm = world.Realms[Entity(world, candidate.Subject)!.Value.Id];
        if (realm.Destroyed || DiplomacySystem.AtWar(world, realm) || Realms.RulerOf(world, realm) is not { IsActive: true } ruler) return;
        var prey = Realms.Neighbours(world, realm).FirstOrDefault(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id) && Weakness(ctx, n) != 0);
        if (prey is null) return;
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
        if (victimRef.Contains('|'))
        {
            foreach (var one in victimRef.Split('|')) Blame(ctx, eventId, culpritRef, one);
            return;
        }
        var world = ctx.World;
        if (Entity(world, culpritRef) is not { } culprit || Entity(world, victimRef) is not { } victim || culprit == victim) return;
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

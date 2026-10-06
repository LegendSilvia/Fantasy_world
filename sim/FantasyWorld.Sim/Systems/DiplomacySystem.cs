using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>A reason to go to war, found in history: the event it cites and what it aims at.</summary>
public sealed record CasusBelliOption(string Kind, long Against, long EventId, long TargetRegionId, long TargetArtifactId, double Weight);

/// <summary>
/// Relations between independent realms (spec §6 "Diplomacy"), yearly. Realm opinion = the rulers'
/// personal opinion + realm memories + faith and culture + treaties. Friendly neighbours ally (sealed
/// by a royal marriage when kin allow), hostile ones declare rivalry. A ruler at peace weighs every
/// casus belli history offers against keeping the peace (a decision point); a declared war cites its
/// casus belli and draws in allies.
/// </summary>
public sealed class DiplomacySystem : ISimSystem
{
    private readonly IDecider _decider;
    private readonly IInventor _inventor;

    public DiplomacySystem(IDecider? decider = null, IInventor? inventor = null)
    {
        _decider = decider ?? new RulesDecider();
        _inventor = inventor ?? new RulesInventor();
    }

    public string Name => "diplomacy";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var realms = Realms.Independent(world).OrderBy(r => r.Id).ToList();
        foreach (var a in realms)
            foreach (var b in Realms.Neighbours(world, a).Where(b => b.Id > a.Id && b.LiegeId == 0))
                ReviewTreaties(ctx, a, b);

        foreach (var realm in realms)
        {
            if (realm.Destroyed || realm.LiegeId != 0 || AtWar(world, realm)) continue;
            var ruler = Realms.RulerOf(world, realm);
            if (ruler is null || ruler.Stage(ctx.Date) == LifeStage.Child) continue;
            var options = CasusBelliFor(ctx, realm);
            if (options.Count == 0) continue;
            var choices = options.Select((o, i) => new DecisionOption($"war:{i}", o.Weight * Aggression(world, realm, ruler)
                    * Math.Clamp(Strength(world, realm) / Math.Max(1, Strength(world, world.Realms[o.Against])), 0.3, 2.0),
                    $"{o.Kind} against {world.Realms[o.Against].Name}"))
                .Append(new DecisionOption("peace", 100 + realm.WarWeariness, "keep the peace"))
                .ToList();
            var choice = _decider.Decide(new DecisionBrief(ruler.Id, "war or peace", choices), ctx.Rng);
            if (choice.Key == "peace") continue;
            using (ctx.Annotate("motive", choice.Reasoning))
                Declare(ctx, realm, options[int.Parse(choice.Key[4..], CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>What realm a thinks of realm b, −100…100.</summary>
    public static double Opinion(World world, Realm a, Realm b)
    {
        var score = 0.0;
        if (Realms.RulerOf(world, a) is { } ra && Realms.RulerOf(world, b) is { } rb) score += Relations.Opinion(ra, rb) / 2;
        score += a.Memories.Where(m => m.About == b.Id).Sum(m => m.Feeling * m.Weight / 100);
        score += a.FaithId == b.FaithId ? 15 : -15;
        if (a.CultureId == b.CultureId) score += 10;
        if (a.Has(TreatyKind.Alliance, b.Id)) score += 25;
        if (a.Has(TreatyKind.Rivalry, b.Id)) score -= 25;
        return Math.Clamp(score, -100, 100);
    }

    /// <summary>Rough fighting power: levies from the territory, men-at-arms the treasury could pay, knights.</summary>
    public static double Strength(World world, Realm realm)
    {
        var territory = Realms.Territory(world, realm).Select(r => r.Id).ToHashSet();
        var levies = world.Settlements.Values.Where(s => !s.Abandoned && territory.Contains(s.RegionId)).Sum(s => s.Adults) * 0.05;
        var knights = world.ActiveCharacters.Count(c => c.Role == Role.Knight && territory.Contains(world.Settlements[c.SettlementId].RegionId));
        return levies + Math.Max(0, realm.Treasury) / 50 * 3 + knights * 20;
    }

    public static bool AtWar(World world, Realm realm) => world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id));

    private static double Aggression(World world, Realm realm, Character ruler)
    {
        var a = 1.0;
        if (ruler.Has(Traits.Ambitious) || ruler.Has(Traits.Wrathful) || ruler.Has(Traits.Brave)) a *= 1.5;
        if (ruler.Has(Traits.Content) || ruler.Has(Traits.Craven) || ruler.Has(Traits.Kind)) a *= 0.5;
        var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
        if (tenets.Contains("pacifist")) a *= 0.3;
        if (tenets.Contains("warlike")) a *= 1.5;
        if (realm.Conditions.ContainsKey(Statecraft.StabilityCollapsed)) a *= 0.5;
        return a;
    }

    private static void ReviewTreaties(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        var ab = Opinion(world, a, b);
        var ba = Opinion(world, b, a);
        var allied = a.Has(TreatyKind.Alliance, b.Id);
        var rivals = a.Has(TreatyKind.Rivalry, b.Id);
        if (!allied && ab >= 40 && ba >= 40 && !AtWarWith(world, a, b))
        {
            var wedding = RoyalMarriage(ctx, a, b);
            var pact = Pact(ctx, a, b, TreatyKind.Alliance, "AllianceFormed", 40, wedding);
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Rivalry && t.With == a.Id);
            Statecraft.Remember(a, pact.Id, b.Id, 40, 60);
            Statecraft.Remember(b, pact.Id, a.Id, 40, 60);
        }
        else if (allied && (ab < 0 || ba < 0))
        {
            a.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == b.Id);
            b.Treaties.RemoveAll(t => t.Kind == TreatyKind.Alliance && t.With == a.Id);
            var broken = ctx.Emit(new EventDraft("AllianceBroken", Actors: Pair(a, b), Place: world.Settlements[a.CapitalId].Ref,
                Importance: 40, Tags: new[] { "Politics", "Diplomacy" }));
            Statecraft.Remember(a, broken.Id, b.Id, -30, 60);
            Statecraft.Remember(b, broken.Id, a.Id, -30, 60);
        }
        else if (!rivals && ab <= -40 && ba <= -40)
            Pact(ctx, a, b, TreatyKind.Rivalry, "RivalryDeclared", 35, null);
    }

    private static WorldEvent Pact(SimContext ctx, Realm a, Realm b, TreatyKind kind, string type, int importance, WorldEvent? cause)
    {
        var ev = ctx.Emit(new EventDraft(type, Actors: Pair(a, b), Place: ctx.World.Settlements[a.CapitalId].Ref,
            Causes: cause is null ? null : new[] { new Cause(cause.Id, CauseKind.Enabler, 0.8) },
            Importance: importance, Tags: new[] { "Politics", "Diplomacy" }));
        a.Treaties.Add(new Treaty(kind, b.Id, ev.Id, 0));
        b.Treaties.Add(new Treaty(kind, a.Id, ev.Id, 0));
        return ev;
    }

    /// <summary>Weds an unmarried adult child or sibling of one ruler to one of the other's, if any match.</summary>
    private static WorldEvent? RoyalMarriage(SimContext ctx, Realm a, Realm b)
    {
        var world = ctx.World;
        IEnumerable<Character> Kin(Realm realm) => Realms.RulerOf(world, realm) is { } ruler
            ? ruler.Children.Select(id => world.Characters[id]).Concat(Fates.Siblings(world, ruler)).Append(ruler)
                .Where(k => FamilySystem.Marriageable(k, ctx.Date))
            : Array.Empty<Character>();
        // Children first, then siblings, then the rulers themselves.
        foreach (var x in Kin(a))
            foreach (var y in Kin(b))
                if (x.Sex != y.Sex && !(x.Role == Role.Lord && y.Role == Role.Lord) && !Relations.AreKin(x, y))
                    return FamilySystem.Wed(ctx, x, y);
        return null;
    }

    /// <summary>Every casus belli this realm's history gives it against a neighbour.</summary>
    public static List<CasusBelliOption> CasusBelliFor(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var options = new List<CasusBelliOption>();
        var ruler = Realms.RulerOf(world, realm);
        foreach (var enemy in Realms.Neighbours(world, realm).Where(n => n.LiegeId == 0 && !n.Destroyed && !realm.Has(TreatyKind.Alliance, n.Id)))
        {
            var theirs = Realms.Territory(world, enemy).ToList();
            var grudge = realm.Memories.Where(m => m.About == enemy.Id && m.Feeling <= -50).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null)
            {
                var ev = ctx.Log.Get(grudge.EventId);
                var lost = ev.Type == "RegionCeded" && theirs.Any(r => ev.Place == r.Ref) ? ev.Place!.Value.Id : 0;
                if (ev.Type == "IndependenceDeclared")
                    options.Add(new(CasusBelli.Independence, enemy.Id, grudge.EventId, world.Settlements[enemy.CapitalId].RegionId, 0, 40));
                else if (lost != 0) options.Add(new(CasusBelli.Reconquest, enemy.Id, grudge.EventId, lost, 0, 40));
                else options.Add(new(CasusBelli.Revenge, enemy.Id, grudge.EventId, Border(world, realm, enemy), 0, 30));
            }

            var tenets = world.Faiths.GetValueOrDefault(realm.FaithId)?.Tenets ?? new List<string>();
            if (realm.FaithId != enemy.FaithId && (tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, 0, Border(world, realm, enemy), 0, 20));
            // A faith that lost its holy land to this neighbour wants it back, the more so the more zealous it is.
            if (world.Faiths.GetValueOrDefault(realm.FaithId) is { HolySiteLostEventId: not 0 } mine && world.Regions.TryGetValue(mine.HolyRegionId, out var holy)
                && Realms.SovereignOf(world, holy)?.Id == enemy.Id && (mine.Zeal >= 40 || tenets.Contains("zealous") || tenets.Contains("warlike")))
                options.Add(new(CasusBelli.HolyWar, enemy.Id, mine.HolySiteLostEventId, holy.Id, 0, 40));

            var hungry = Realms.Domain(world, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
            var granary = theirs.Where(r => r.Fertility >= 60 && Touches(world, r, realm)).OrderByDescending(r => r.Fertility).ThenBy(r => r.Id).FirstOrDefault();
            if (hungry != 0 && granary is not null) options.Add(new(CasusBelli.ResourceNeed, enemy.Id, hungry, granary.Id, 0, 30));

            if (ruler is not null)
            {
                foreach (var artifact in world.Artifacts.Values.Where(x => HeldBy(world, x, enemy)))
                {
                    // Only a fresh wrong counts: the artifact's latest change of hands was a theft from the ruler's house.
                    var theft = artifact.Provenance.LastOrDefault();
                    if (theft is { How: "stolen" or "looted" } && ruler.HouseId != 0
                        && world.Characters.TryGetValue(theft.From, out var victim) && victim.HouseId == ruler.HouseId) options.Add(new(CasusBelli.ArtifactRecovery, enemy.Id, theft.EventId, world.Settlements[enemy.CapitalId].RegionId, artifact.Id, 25));
                }
                if (Realms.RulerOf(world, enemy) is { } theirRuler && world.Characters.TryGetValue(theirRuler.SpouseId, out var consort)
                    && (consort.HouseId == ruler.HouseId) && consort.MarriageEventId != 0)
                    options.Add(new(CasusBelli.Claim, enemy.Id, consort.MarriageEventId, world.Settlements[enemy.CapitalId].RegionId, 0, 20));
            }
        }
        return options;
    }

    private void Declare(SimContext ctx, Realm attacker, CasusBelliOption cb)
    {
        var world = ctx.World;
        var defender = world.Realms[cb.Against];
        var cbEvent = cb.EventId;
        if (cb.Kind == CasusBelli.HolyWar)
            cbEvent = ctx.Emit(new EventDraft("HolyWarProclaimed", Actors: Pair(attacker, defender),
                Place: world.Settlements[attacker.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["faith"] = world.Faiths[attacker.FaithId].Name },
                Causes: cb.EventId != 0 ? new[] { new Cause(cb.EventId, CauseKind.Motive, 0.9) } : null,
                Importance: 50, Tags: new[] { "Politics", "Faith" })).Id;

        var target = world.Regions.GetValueOrDefault(cb.TargetRegionId);
        var name = cb.Kind switch
        {
            CasusBelli.HolyWar when target is not null && target.Id == world.Faiths[attacker.FaithId].HolyRegionId => $"The Holy War for {target.Name}",
            CasusBelli.HolyWar => $"The Holy War of {world.Faiths[attacker.FaithId].Deity}",
            CasusBelli.Independence => $"The {defender.Name} Rebellion",
            CasusBelli.Revenge => $"The War of {attacker.Name}'s Vengeance",
            _ => $"The {target?.Name ?? defender.Name} War",
        };
        var warId = world.Ids.Peek(War.Kind);
        var invention = _inventor.Invent(new InventionBrief("war", $"{War.Kind}#{warId}",
            $"A war of {cb.Kind} declared by {Statecraft.Title(attacker)} on {Statecraft.Title(defender)}"
            + (target is null ? "" : $" over {target.Name}") + $", in year {ctx.Date.Year}.", name));
        name = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind),
            Name = name,
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            Attackers = { attacker.Id },
            Defenders = { defender.Id },
            CasusBelli = cb.Kind,
            CasusBelliEventId = cbEvent,
            TargetRegionId = cb.TargetRegionId,
            TargetArtifactId = cb.TargetArtifactId,
            StartDay = ctx.Date.Day,
        };
        var declared = ctx.Emit(new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = attacker.Ref, ["defender"] = defender.Ref, ["war"] = war.Ref },
            Place: world.Settlements[defender.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["casus belli"] = cb.Kind, ["war"] = name },
            Causes: WarCauses(ctx, attacker, defender, cbEvent),
            Importance: 70, Tags: new[] { "Politics", "War" }));
        war.DeclaredEventId = declared.Id;
        world.Wars[war.Id] = war;
        attacker.Treaties.RemoveAll(t => t.With == defender.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        defender.Treaties.RemoveAll(t => t.With == attacker.Id && t.Kind is TreatyKind.Alliance or TreatyKind.NonAggression);
        Statecraft.Remember(defender, declared.Id, attacker.Id, -50, 70);

        foreach (var (side, leader) in new[] { (war.Attackers, attacker), (war.Defenders, defender) })
            foreach (var ally in Realms.Independent(world).Where(r => r.Has(TreatyKind.Alliance, leader.Id) && !AtWar(world, r)).OrderBy(r => r.Id).ToList())
            {
                // An ally of both sides stays out rather than fight a friend.
                if (war.Involves(ally.Id) || ally.Has(TreatyKind.Alliance, attacker.Id) && ally.Has(TreatyKind.Alliance, defender.Id)
                    || !ctx.Rng.Chance(0.7)) continue;
                side.Add(ally.Id);
                ctx.Emit(new EventDraft("AllyJoinedWar",
                    Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                    Payload: new Dictionary<string, string> { ["side"] = side == war.Attackers ? "attackers" : "defenders" },
                    Place: world.Settlements[ally.CapitalId].Ref,
                    Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
            }
    }

    /// <summary>
    /// The casus belli is the motive; the realms' latest rivalry, their latest shared grievance (a peace, a ceded
    /// region, a declaration of independence) and the ruler's accession, when there are any, are enablers (at most
    /// three causes in all), so a war's why-chain reaches back past its pretext.
    /// </summary>
    private static List<Cause> WarCauses(SimContext ctx, Realm attacker, Realm defender, long cbEvent)
    {
        var causes = new List<Cause> { new(cbEvent, CauseKind.Motive, 0.9) };
        bool Cited(long id) => causes.Any(c => c.EventId == id);
        var shared = ctx.Log.Involving(attacker.Ref).Where(e => e.Actors.ContainsValue(defender.Ref)).ToList();
        var rivalry = shared.LastOrDefault(e => e.Type is "RivalryDeclared" or "AllianceBroken");
        if (rivalry is not null && !Cited(rivalry.Id)) causes.Add(new Cause(rivalry.Id, CauseKind.Enabler, 0.5));
        var grievance = shared.LastOrDefault(e => e.Type is "PeaceSigned" or "RegionCeded" or "IndependenceDeclared");
        if (grievance is not null && !Cited(grievance.Id)) causes.Add(new Cause(grievance.Id, CauseKind.Enabler, 0.4));
        if (causes.Count < 3 && Realms.RulerOf(ctx.World, attacker) is { } ruler
            && Fates.Accession(ctx.Log, ruler) is { } accession && !Cited(accession.Id))
            causes.Add(new Cause(accession.Id, CauseKind.Enabler, 0.3));
        return causes;
    }

    private static bool AtWarWith(World world, Realm a, Realm b) =>
        world.Wars.Values.Any(w => !w.Ended && (w.IsAttacker(a.Id) && w.Defenders.Contains(b.Id) || w.IsAttacker(b.Id) && w.Defenders.Contains(a.Id)));

    private static bool HeldBy(World world, Artifact x, Realm realm) => x.Place switch
    {
        ArtifactPlace.Held => Realms.SovereignOf(world, world.Settlements[world.Characters[x.HolderId].SettlementId])?.Id == realm.Id,
        ArtifactPlace.Treasury => Realms.SovereignOf(world, world.Settlements[x.SettlementId])?.Id == realm.Id,
        _ => false,
    };

    /// <summary>The enemy's region nearest our territory: an enemy region adjacent to ours, lowest id.</summary>
    private static long Border(World world, Realm ours, Realm theirs) =>
        Realms.Territory(world, theirs).Where(r => Touches(world, r, ours)).Select(r => r.Id).DefaultIfEmpty(world.Settlements[theirs.CapitalId].RegionId).Min();

    private static bool Touches(World world, Region region, Realm realm) =>
        (world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).Any(n => Realms.SovereignOf(world, world.Regions[n])?.Id == realm.Id);

    private static Dictionary<string, EntityRef> Pair(Realm a, Realm b) => new() { ["a"] = a.Ref, ["b"] = b.Ref };
}

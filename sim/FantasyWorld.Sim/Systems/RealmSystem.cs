using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Politics inside realms (spec §6). Monthly: capitals stay in the domain, settlements pay taxes
/// (vassals pass a fifth to their liege), councils of duchies and greater fill their seats. Yearly:
/// legitimacy and stability drift (with collapse and recovery as condition events citing their causes),
/// war weariness fades, unhappy vassals declare independence, ranks rise and fall, and each change of
/// phase opens a named era.
/// </summary>
public sealed class RealmSystem : ISimSystem
{
    public const double TaxRate = 0.01;
    public const double VassalTribute = 0.2;
    public const double IndependencePerYear = 0.25;

    private static readonly IReadOnlyDictionary<RealmPhase, string[]> EraWords = new Dictionary<RealmPhase, string[]>
    {
        [RealmPhase.Expanding] = new[] { "Rising", "Golden", "Banners", "Conquest" },
        [RealmPhase.Stable] = new[] { "Long Peace", "Plenty", "Quiet Crowns", "Hearth" },
        [RealmPhase.Declining] = new[] { "Twilight", "Waning", "Ash", "Broken Oaths" },
    };

    private readonly IInventor _inventor;

    public RealmSystem(IInventor? inventor = null) => _inventor = inventor ?? new RulesInventor();

    public string Name => "realms";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        ILookup<long, Character>? residents = null;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            if (realm.Destroyed || !Statecraft.SecureCapital(ctx, realm, CapitalLossCauses(ctx, realm))) continue;
            Collect(world, realm);
            if (realm.Rank >= RealmRank.Duchy)
                FillCouncil(ctx, realm, residents ??= world.ActiveCharacters.ToLookup(c => c.SettlementId));
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed).ToList())
        {
            Drift(ctx, realm);
            if (realm.LiegeId != 0) ConsiderIndependence(ctx, realm);
            Rerank(ctx, realm);
            if (realm.LiegeId == 0) ReviewPhase(ctx, realm);
            for (var i = realm.Memories.Count - 1; i >= 0; i--)
            {
                var faded = realm.Memories[i] with { Weight = realm.Memories[i].Weight * 0.85 };
                if (faded.Weight < 3) realm.Memories.RemoveAt(i);
                else realm.Memories[i] = faded;
            }
            if (!world.Wars.Values.Any(w => !w.Ended && w.Involves(realm.Id)))
                realm.WarWeariness = Math.Max(0, realm.WarWeariness - 10);
        }
    }

    private static void Collect(World world, Realm realm)
    {
        var income = 0.0;
        foreach (var s in Realms.Domain(world, realm).SelectMany(world.LivingSettlements))
        {
            var tax = s.Wealth * TaxRate;
            s.Wealth -= tax;
            income += tax;
        }
        if (realm.LiegeId != 0)
        {
            var tribute = income * VassalTribute;
            world.Realms[realm.LiegeId].Treasury += tribute;
            income -= tribute;
        }
        realm.Treasury += income;
    }

    /// <summary>Residents of the realm's domain, looked up once per tick: the cost stays flat as the dead accumulate.</summary>
    private static void FillCouncil(SimContext ctx, Realm realm, ILookup<long, Character> residents)
    {
        var world = ctx.World;
        var ruler = Realms.RulerOf(world, realm);
        List<Character>? court = null;
        foreach (var seat in CouncilSeats.All)
        {
            // Holders keep their seat while active and eligible (a member raised to a lordship must step down).
            if (realm.Council.TryGetValue(seat, out var holder) && world.Characters[holder] is { IsActive: true } member
                && member.Id != ruler?.Id && Eligible(realm, seat, member)) continue;
            realm.Council.Remove(seat);
            var seated = realm.Council.Values.ToHashSet();
            court ??= Realms.Domain(world, realm).SelectMany(world.LivingSettlements).SelectMany(s => residents[s.Id]).ToList();
            var candidate = court
                .Where(c => c.Id != ruler?.Id && !seated.Contains(c.Id)
                            && c.Stage(ctx.Date) != LifeStage.Child && Eligible(realm, seat, c))
                .OrderByDescending(c => Aptitude(seat, c)).ThenBy(c => c.Id)
                .FirstOrDefault();
            if (candidate is null) continue;
            realm.Council[seat] = candidate.Id;
            ctx.Emit(new EventDraft("CouncilAppointed",
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref, ["member"] = candidate.Ref },
                Place: world.Settlements[realm.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["seat"] = seat },
                Importance: realm.LiegeId == 0 ? 15 : 8, Tags: new[] { "Politics" }));
        }
    }

    private static bool Eligible(Realm realm, string seat, Character c) => seat switch
    {
        CouncilSeats.CourtMage => c.Role == Role.Mage && realm.MagicLaw != MagicLaw.Outlawed,
        CouncilSeats.HighPriest => c.Role == Role.Priest && c.FaithId == realm.FaithId,
        _ => c.Role is not (Role.Rebel or Role.Lord),
    };

    public static double Aptitude(string seat, Character c) => seat switch
    {
        CouncilSeats.Marshal => c.Attributes.Martial * 2 + c.Attributes.Prowess,
        CouncilSeats.Steward => c.Attributes.Stewardship * 3,
        CouncilSeats.Spymaster => c.Attributes.Intrigue * 3,
        CouncilSeats.CourtMage => c.Attributes.Lore * 3,
        _ => c.Attributes.Lore * 2 + c.Attributes.Diplomacy,
    } + c.Renown / 10;

    private static void Drift(SimContext ctx, Realm realm)
    {
        var world = ctx.World;
        var ruler = Realms.RulerOf(world, realm);
        var legitimacyTarget = 60.0;
        if (ruler is null) legitimacyTarget -= 45;
        else
        {
            if (ruler.Age(ctx.Date) < Character.AdultAge) legitimacyTarget -= 25;
            if (ruler.FaithId != realm.FaithId) legitimacyTarget -= 10;
            legitimacyTarget += ruler.Renown / 5;
            if (world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == ruler.Id && a.Powers.Any(p => p.Kind == "legitimacy")))
                legitimacyTarget += 15;
        }
        realm.Legitimacy = Math.Clamp(realm.Legitimacy + (legitimacyTarget - realm.Legitimacy) * 0.3, 0, 100);

        var settlements = Realms.Domain(world, realm).SelectMany(world.LivingSettlements).ToList();
        var unrest = settlements.Count > 0 ? settlements.Average(s => s.Unrest) : 0;
        var hungry = Realms.Domain(world, realm).Count(r => r.Conditions.ContainsKey(Conditions.Shortage));
        var stabilityTarget = 30 + realm.Legitimacy * 0.4 - realm.WarWeariness * 0.3 - unrest * 0.3 - hungry * 5;
        realm.Stability = Math.Clamp(realm.Stability + (stabilityTarget - realm.Stability) * 0.3, 0, 100);

        Threshold(ctx, realm, Statecraft.LegitimacyCollapsed, realm.Legitimacy < 25, realm.Legitimacy >= 40,
            "LegitimacyCollapsed", "LegitimacyRestored", LegitimacyCauses(ctx, realm));
        Threshold(ctx, realm, Statecraft.StabilityCollapsed, realm.Stability < 25, realm.Stability >= 40,
            "StabilityCollapsed", "StabilityRestored", StabilityCauses(ctx, realm));
    }

    private static void Threshold(SimContext ctx, Realm realm, string condition, bool begins, bool ends,
        string beginType, string endType, IReadOnlyList<Cause> causes)
    {
        var active = realm.Conditions.TryGetValue(condition, out var began);
        var place = ctx.World.Settlements[realm.CapitalId].Ref;
        if (!active && begins)
            realm.Conditions[condition] = ctx.Emit(new EventDraft(beginType,
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: place, Causes: causes,
                Importance: realm.LiegeId == 0 ? 50 : 25, Tags: new[] { "Politics" })).Id;
        else if (active && ends)
        {
            ctx.Emit(new EventDraft(endType, Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: place,
                Causes: new[] { new Cause(began, CauseKind.Trigger, 1.0) }, Importance: 20, Tags: new[] { "Politics" }));
            realm.Conditions.Remove(condition);
        }
    }

    /// <summary>A collapse of legitimacy cites how the current ruler came to power (or the empty seat).</summary>
    private static IReadOnlyList<Cause> LegitimacyCauses(SimContext ctx, Realm realm)
    {
        var capital = ctx.World.Settlements[realm.CapitalId];
        if (capital.Conditions.TryGetValue(Fates.VacantSeat, out var vacancy)) return new[] { new Cause(vacancy, CauseKind.Trigger, 0.9) };
        var accession = ctx.Log.Involving(capital.Ref).LastOrDefault(e => e.Type is "SuccessionOccurred" or "NewLordRose");
        return accession is null ? Array.Empty<Cause>() : new[] { new Cause(accession.Id, CauseKind.Enabler, 0.7) };
    }

    private static IReadOnlyList<Cause> StabilityCauses(SimContext ctx, Realm realm)
    {
        var causes = new List<Cause>();
        if (realm.Conditions.TryGetValue(Statecraft.LegitimacyCollapsed, out var legitimacy)) causes.Add(new Cause(legitimacy, CauseKind.Enabler, 0.8));
        var war = ctx.World.Wars.Values.Where(w => !w.Ended && w.Involves(realm.Id)).OrderBy(w => w.Id).FirstOrDefault();
        if (war is not null) causes.Add(new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.7));
        var famine = Realms.Domain(ctx.World, realm).Select(r => r.Conditions.GetValueOrDefault(Conditions.Shortage)).FirstOrDefault(id => id != 0);
        if (famine != 0) causes.Add(new Cause(famine, CauseKind.Enabler, 0.6));
        return causes;
    }

    private static void ConsiderIndependence(SimContext ctx, Realm vassal)
    {
        var world = ctx.World;
        var liege = world.Realms[vassal.LiegeId];
        var ruler = Realms.RulerOf(world, vassal);
        var overlord = Realms.RulerOf(world, liege);
        var causes = new List<Cause>();
        if (liege.Conditions.TryGetValue(Statecraft.StabilityCollapsed, out var collapse)) causes.Add(new Cause(collapse, CauseKind.Enabler, 0.8));
        if (ruler is not null && overlord is not null && Relations.Opinion(ruler, overlord) <= Relations.RivalAt)
        {
            var grudge = ruler.Memories.Where(m => m.About == overlord.Id).OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is not null) causes.Add(new Cause(grudge.EventId, CauseKind.Motive, 0.9));
        }
        var unhappy = liege.Conditions.ContainsKey(Statecraft.StabilityCollapsed)
                      || (ruler is not null && overlord is not null && Relations.Opinion(ruler, overlord) <= Relations.RivalAt);
        if (!unhappy || !ctx.Rng.Chance(IndependencePerYear)) return;

        vassal.LiegeId = 0;
        var declared = ctx.Emit(new EventDraft("IndependenceDeclared",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = vassal.Ref, ["former liege"] = liege.Ref },
            Place: world.Settlements[vassal.CapitalId].Ref, Causes: causes, Importance: 55, Tags: new[] { "Politics" }));
        Statecraft.Remember(liege, declared.Id, vassal.Id, -70, 90);
    }

    private static void Rerank(SimContext ctx, Realm realm)
    {
        var rank = Realms.RankFor(ctx.World, realm);
        if (rank == realm.Rank) return;
        var rose = rank > realm.Rank;
        realm.Rank = rank;
        ctx.Emit(new EventDraft(rose ? "RealmRose" : "RealmDiminished",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["rank"] = rank.ToString() },
            Importance: rank == RealmRank.Empire && rose ? 85 : realm.LiegeId == 0 ? 50 : 25, Tags: new[] { "Politics" }));
    }

    private void ReviewPhase(SimContext ctx, Realm realm)
    {
        var size = Realms.Territory(ctx.World, realm).Count();
        var phase = size > realm.RegionsAtLastReview ? RealmPhase.Expanding
            : size < realm.RegionsAtLastReview ? RealmPhase.Declining
            : realm.Phase is RealmPhase.Founded or RealmPhase.Expanding ? RealmPhase.Stable
            : realm.Phase;
        realm.RegionsAtLastReview = size;
        if (phase == realm.Phase || !EraWords.TryGetValue(phase, out var words)) return;
        var quietStart = realm.Phase == RealmPhase.Founded && phase == RealmPhase.Stable;
        realm.Phase = phase;
        if (quietStart) return;
        var era = $"The {(phase == RealmPhase.Stable ? "Age of " : "")}{words[ctx.Rng.NextInt(0, words.Length)]}{(phase == RealmPhase.Stable ? "" : " Age")} of {realm.Name}";
        var invention = _inventor.Invent(new InventionBrief("era", $"{realm.Ref}@{ctx.Date.Year}",
            $"{Statecraft.Title(realm)} enters a time of {phase.ToString().ToLowerInvariant()} fortunes in year {ctx.Date.Year}; "
            + $"it holds {size} region(s) and its legitimacy stands at {realm.Legitimacy.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}/100.", era));
        era = invention.Name;
        using var lore = ctx.Annotate("lore", invention.Lore);
        ctx.Emit(new EventDraft("EraBegan",
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["era"] = era, ["phase"] = phase.ToString() },
            Importance: 45, Tags: new[] { "Politics", "Era" }));
    }

    /// <summary>A lost capital cites the latest cession or abandonment there.</summary>
    private static IReadOnlyList<Cause> CapitalLossCauses(SimContext ctx, Realm realm)
    {
        var capital = ctx.World.Settlements[realm.CapitalId];
        var loss = ctx.Log.Involving(capital.Ref).LastOrDefault(e => e.Type == "SettlementAbandoned")
                   ?? ctx.Log.Involving(ctx.World.Regions[capital.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded");
        return loss is null ? Array.Empty<Cause>() : new[] { new Cause(loss.Id, CauseKind.Trigger, 1.0) };
    }
}

using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Revolution (sub-project 12 R2, the owner's decision of 2026-10-28: "all the way"). Yearly, an independent realm
/// that is unsettled (stability below <see cref="UnsettledBelow"/>) with towns in revolt beyond its capital's land may
/// see its people rise, the likelier the more towns revolt: the most fitting discontented notable of a revolting town leads them, the revolting towns' lands become a rebel
/// realm under the leader, and the rebels declare a civil war on the crown (casus belli "revolution"), fought like any
/// war. Its peace ends the revolution (<see cref="End"/>): the crown wins and the rebel realm falls back into it, the
/// leader executed or fled; or the rebels win and their leader chooses (Claude, through the decider) to break away, to
/// seize the throne, or to seize it and remake the realm's government; a stalemate leaves the rebels free.
/// </summary>
public sealed class RevolutionSystem : ISimSystem
{
    /// <summary>Stability under which a realm is unsettled: its towns grow restless and their revolts may become a revolution.</summary>
    public const double UnsettledBelow = 35;

    /// <summary>The yearly chance of a revolution for each town in revolt, and at most.</summary>
    public const double ChancePerRevolt = 0.3, MostChance = 0.75;

    /// <summary>The crown's condition while a revolution is under way, mapped to the event that began it.</summary>
    public const string Condition = "revolution";

    public const string Crushed = "crushed", BrokeAway = "breakaway", Overthrown = "overthrow";

    /// <summary>RevolutionBegan's payload key listing the lands that rose, by region id.</summary>
    public const string RegionIds = "region ids";

    private readonly IInventor _inventor;

    public RevolutionSystem(IInventor? inventor = null) => _inventor = inventor ?? new RulesInventor();

    public string Name => "revolution";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var realm in Realms.Independent(ctx.World).OrderBy(r => r.Id).ToList())
        {
            if (realm.Destroyed) continue;
            var risen = Risen(ctx.World, realm);
            if (risen.Count == 0 || !ctx.Rng.Chance(Math.Min(MostChance, ChancePerRevolt * risen.Count))) continue;
            Begin(ctx, realm, risen);
        }
    }

    /// <summary>
    /// The revolting settlements that would rise with a revolution now, outside the capital's land (empty if none
    /// would): the realm is unsettled and no revolution is under way.
    /// </summary>
    public static IReadOnlyList<Settlement> Risen(World world, Realm realm)
    {
        if (realm.Stability >= UnsettledBelow || realm.Conditions.ContainsKey(Condition)) return [];
        var capitalRegion = world.Settlements[realm.CapitalId].RegionId;
        return Realms.Domain(world, realm).Where(r => r.Id != capitalRegion).SelectMany(world.LivingSettlements)
            .Where(s => s.Conditions.ContainsKey(Conditions.Revolt)).OrderBy(s => s.Id).ToList();
    }

    /// <summary>
    /// Who leads the rising: an adult of a revolting town who is neither the ruler nor the ruler's heir, nor already a
    /// lord, the likelier the bolder, the more able, the more ambitious, the more wronged by the ruler, and a rebel first.
    /// </summary>
    public static Character? Leader(World world, Realm realm, IReadOnlyList<Settlement> risen, WorldDate today)
    {
        var ruler = Realms.RulerOf(world, realm);
        var towns = risen.Select(s => s.Id).ToHashSet();
        return world.ActiveCharacters
            .Where(c => towns.Contains(c.SettlementId) && c.Stage(today) != LifeStage.Child && c.Id != ruler?.Id
                        && c.Role is Role.Rebel or Role.None or Role.Knight or Role.Adventurer
                        && !(ruler is not null && Fates.Heir(world, ruler, today)?.Id == c.Id))
            .OrderByDescending(c => Fitness(c, ruler)).ThenBy(c => c.Id)
            .FirstOrDefault();
    }

    private static double Fitness(Character c, Character? ruler) =>
        c.Attributes.Martial * 2 + c.Attributes.Intrigue + c.Attributes.Diplomacy + c.Renown / 5
        + (c.Role == Role.Rebel ? 15 : 0) + (c.Has(Traits.Ambitious) ? 10 : 0) + (c.Has(Traits.Brave) ? 5 : 0)
        - (c.Has(Traits.Craven) || c.Has(Traits.Content) ? 10 : 0)
        + (ruler is not null && Relations.Opinion(c, ruler) <= Relations.RivalAt ? 10 : 0);

    private void Begin(SimContext ctx, Realm crown, IReadOnlyList<Settlement> risen)
    {
        var world = ctx.World;
        var leader = Leader(world, crown, risen, ctx.Date);
        if (leader is null) return;
        var seat = world.Settlements[leader.SettlementId];
        var regions = risen.Select(s => world.Regions[s.RegionId]).DistinctBy(r => r.Id).OrderBy(r => r.Id).ToList();
        var domain = Realms.Domain(world, crown).Count();
        var ruler = Realms.RulerOf(world, crown);

        var causes = new List<Cause>();
        if (seat.Conditions.TryGetValue(Conditions.Revolt, out var revolt)) causes.Add(new Cause(revolt, CauseKind.Trigger, 0.9));
        if (crown.Conditions.TryGetValue(Statecraft.StabilityCollapsed, out var collapse)) causes.Add(new Cause(collapse, CauseKind.Enabler, 0.8));
        if (Stocks.Shortages(crown).FirstOrDefault() is { } wanting) causes.Add(new Cause(crown.Conditions["short:" + wanting], CauseKind.Enabler, 0.6));

        var id = world.Ids.Take(Realm.Kind);
        var rebelRef = new EntityRef(Realm.Kind, id);
        var actors = new Dictionary<string, EntityRef> { ["realm"] = crown.Ref, ["leader"] = leader.Ref, ["rebels"] = rebelRef };
        if (ruler is not null) actors["ruler"] = ruler.Ref;
        var began = ctx.Emit(new EventDraft("RevolutionBegan", Actors: actors, Place: seat.Ref,
            Payload: new Dictionary<string, string>
            {
                ["regions"] = regions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // For the history replay (Views.History): the lands that rose.
                [RegionIds] = string.Join(",", regions.Select(r => r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            },
            Causes: causes, Importance: 80, Tags: new[] { "Politics", "War" }));

        var rebels = new Realm
        {
            Id = id,
            Name = RebelName(world, seat),
            CapitalId = seat.Id,
            Government = crown.Government,
            SuccessionLaw = crown.SuccessionLaw,
            MagicLaw = crown.MagicLaw,
            Legitimacy = 40,
            Stability = 50,
            CultureId = world.Regions[seat.RegionId].CultureId,
            FaithId = world.Regions[seat.RegionId].FaithId,
            Colors = Colors(ctx.Rng, crown),
            FoundedEventId = began.Id,
            RegionsAtLastReview = regions.Count,
        };
        world.Realms[rebels.Id] = rebels;
        foreach (var region in regions)
        {
            region.OwnerRealmId = rebels.Id;
            region.OccupierRealmId = 0;
        }
        // The rebels seize the towns' coffers and their lands' share of the crown's treasury and stores.
        foreach (var s in regions.SelectMany(world.LivingSettlements))
        {
            var seized = s.Wealth * 0.1;
            s.Wealth -= seized;
            rebels.Treasury += seized;
        }
        var share = domain > 0 ? (double)regions.Count / domain : 0;
        var war = Math.Max(0, crown.Treasury) * share;
        crown.Treasury -= war;
        rebels.Treasury += war;
        foreach (var (kind, stock) in crown.Stocks.ToList())
        {
            rebels.Stocks[kind] = Math.Round(stock * share, 3);
            crown.Stocks[kind] = Math.Round(stock - rebels.Stocks[kind], 3);
        }
        rebels.Rank = Realms.RankFor(world, rebels);

        // The leader takes the town; a lord who held it is put out.
        if (seat.LordId != 0 && world.Characters[seat.LordId] is var ousted && ousted.Id != leader.Id)
        {
            ousted.Role = Role.None;
            if (world.Houses.TryGetValue(ousted.HouseId, out var house) && house.SeatSettlementId == seat.Id) house.SeatSettlementId = 0;
            Fates.Remember(ousted, began.Id, leader.Id, -70, 80);
        }
        leader.Role = Role.Lord;
        leader.Renown = Math.Min(100, leader.Renown + 15);
        seat.LordId = leader.Id;
        seat.Conditions.Remove(Fates.VacantSeat);
        world.Houses[leader.HouseId].SeatSettlementId = seat.Id;
        if (ruler is not null)
        {
            Fates.Remember(leader, began.Id, ruler.Id, -60, 80);
            Fates.Remember(ruler, began.Id, leader.Id, -70, 90);
        }
        crown.Conditions[Condition] = began.Id;
        Statecraft.Remember(crown, began.Id, rebels.Id, -70, 90);

        DiplomacySystem.Declare(ctx, _inventor, rebels, new CasusBelliOption(CasusBelli.Revolution, crown.Id, began.Id,
            world.Settlements[crown.CapitalId].RegionId, 0, 0));
    }

    /// <summary>The rebels take their seat's land's name, or the seat's own when a standing realm already bears it.</summary>
    private static string RebelName(World world, Settlement seat)
    {
        var land = world.Regions[seat.RegionId].Name;
        return world.Realms.Values.Any(r => !r.Destroyed && r.Name == land) ? seat.Name : land;
    }

    private static string[] Colors(Rng rng, Realm crown)
    {
        var free = RealmGenerator.Palette.Where(c => !crown.Colors.Contains(c)).ToList();
        var first = free[rng.NextInt(0, free.Count)];
        free.Remove(first);
        return [first, free[rng.NextInt(0, free.Count)]];
    }

    /// <summary>The leader of victorious rebels chooses what comes of the revolution (Claude, within the choice budget).</summary>
    public static DecisionOption Choose(SimContext ctx, IDecider decider, War war)
    {
        var world = ctx.World;
        var crown = world.Realms[war.DefenderId];
        var rebels = world.Realms[war.AttackerId];
        var leader = Realms.RulerOf(world, rebels);
        var breakAway = new DecisionOption(BrokeAway, 30 + (rebels.CultureId != crown.CultureId ? 20 : 0) + (rebels.FaithId != crown.FaithId ? 15 : 0),
            $"rule {rebels.Name} as a realm of its own, free of {crown.Name}");
        if (leader is null || crown.Destroyed) return breakAway;
        var bold = (leader.Has(Traits.Ambitious) ? 25 : 0) + (leader.Ambition == Ambitions.Renown ? 10 : 0) + (leader.Has(Traits.Content) ? -15 : 0);
        var remade = Remade(crown.Government, leader);
        return decider.Decide(new DecisionBrief(leader.Id, "after the revolution", new[]
        {
            breakAway,
            new DecisionOption("seize", Math.Max(5, 30 + bold), $"take the throne of {crown.Name} and rule it as it stands"),
            new DecisionOption("remake", Math.Max(5, 15 + bold / 2 + leader.Attributes.Stewardship),
                $"take the throne of {crown.Name} and remake it as a {English(remade)}"),
        }), ctx.Rng);
    }

    /// <summary>The government a revolution that remakes the realm sets up: a merchant republic under a steward, else an
    /// elective kingdom (an elective realm is made a crown again).</summary>
    public static Government Remade(Government old, Character leader) =>
        old == Government.ElectiveKingdom ? Government.FeudalMonarchy
        : leader.Attributes.Stewardship >= leader.Attributes.Martial && old != Government.MerchantRepublic ? Government.MerchantRepublic
        : Government.ElectiveKingdom;

    private static string English(Government government) => government switch
    {
        Government.FeudalMonarchy => "feudal monarchy",
        Government.ElectiveKingdom => "elective kingdom",
        Government.MerchantRepublic => "merchant republic",
        _ => government.ToString().ToLowerInvariant(),
    };

    /// <summary>The peace's terms for a revolution: crushed, independence, or the crown taken (and perhaps remade).</summary>
    public static string Terms(string outcome, string? choice) => outcome switch
    {
        Crushed => "revolution crushed",
        Overthrown when choice == "remake" => "crown seized and realm remade",
        Overthrown => "crown seized",
        _ => "independence",
    };

    /// <summary>What the peace's outcome and the leader's choice make of the revolution.</summary>
    public static string Outcome(bool attackerWon, bool white, string? choice) =>
        white ? BrokeAway : !attackerWon ? Crushed : choice is "seize" or "remake" ? Overthrown : BrokeAway;

    /// <summary>
    /// Ends the revolution after its war's peace: the rebel realm falls back into the crown (crushed) or takes it over
    /// (overthrow), or stays free (breakaway); the beaten side's leader is executed or flees.
    /// </summary>
    public static void End(SimContext ctx, War war, WorldEvent peace, string outcome, DecisionOption? choice)
    {
        var world = ctx.World;
        var crown = world.Realms[war.DefenderId];
        var rebels = world.Realms[war.AttackerId];
        var leader = Realms.RulerOf(world, rebels) ?? (ctx.Log.TryGet(rebels.FoundedEventId, out var founding)
            && founding.Actors.TryGetValue("leader", out var first) && world.Characters[first.Id].IsActive ? world.Characters[first.Id] : null);
        var ruler = Realms.RulerOf(world, crown);
        var payload = new Dictionary<string, string> { ["outcome"] = outcome };
        Government? remade = null;
        if (outcome == Overthrown && choice?.Key == "remake" && leader is not null)
        {
            remade = Remade(crown.Government, leader);
            payload["government"] = English(remade.Value);
        }
        var actors = new Dictionary<string, EntityRef> { ["realm"] = crown.Ref, ["rebels"] = rebels.Ref, ["war"] = war.Ref };
        if (leader is not null) actors["leader"] = leader.Ref;
        if (ruler is not null) actors["ruler"] = ruler.Ref;
        var causes = new List<Cause> { new(peace.Id, CauseKind.Trigger, 1.0) };
        if (crown.Conditions.TryGetValue(Condition, out var began)) causes.Add(new Cause(began, CauseKind.Enabler, 0.7));
        WorldEvent ended;
        using (ctx.Annotate("motive", choice?.Reasoning))
            ended = ctx.Emit(new EventDraft("RevolutionEnded", Actors: actors,
                Place: world.Settlements[outcome == Overthrown || crown.Destroyed ? rebels.CapitalId : crown.CapitalId].Ref,
                Payload: payload, Causes: causes, Importance: 80, Tags: new[] { "Politics", "War" }));
        crown.Conditions.Remove(Condition);
        var because = new[] { new Cause(ended.Id, CauseKind.Trigger, 1.0) };

        switch (outcome)
        {
            case Crushed:
                if (!crown.Destroyed) Dissolve(world, rebels, crown);
                crown.Stability = Math.Min(100, crown.Stability + 15);
                if (leader is not null) Punish(ctx, leader, crown, because);
                break;
            case Overthrown:
                var seat = world.Settlements[rebels.CapitalId];
                Dissolve(world, rebels, crown);
                if (leader is null) break;
                var capital = world.Settlements[crown.CapitalId];
                if (seat.LordId == leader.Id) seat.LordId = 0;
                if (ruler is not null && ruler.Id != leader.Id)
                {
                    capital.LordId = 0;
                    if (world.Houses.TryGetValue(ruler.HouseId, out var fallen) && fallen.SeatSettlementId == capital.Id) fallen.SeatSettlementId = 0;
                    Punish(ctx, ruler, crown, because);
                }
                leader.SettlementId = capital.Id;
                leader.Role = Role.Lord;
                leader.Renown = Math.Min(100, leader.Renown + 20);
                capital.LordId = leader.Id;
                capital.Conditions.Remove(Fates.VacantSeat);
                world.Houses[leader.HouseId].SeatSettlementId = capital.Id;
                if (remade is { } government)
                {
                    crown.Government = government;
                    crown.SuccessionLaw = RealmGenerator.SuccessionFor(government);
                }
                (crown.Legitimacy, crown.Stability) = (35, 45);
                break;
            default:
                rebels.Legitimacy = Math.Max(rebels.Legitimacy, 55);
                if (!crown.Destroyed) crown.Legitimacy = Math.Max(0, crown.Legitimacy - 10);
                break;
        }
    }

    /// <summary>A beaten leader loses any seat and is executed, or flees to the nearest town beyond the victor's reach.</summary>
    private static void Punish(SimContext ctx, Character c, Realm victor, IReadOnlyList<Cause> causes)
    {
        var world = ctx.World;
        foreach (var held in world.Settlements.Values.Where(s => s.LordId == c.Id)) held.LordId = 0;
        if (world.Houses.TryGetValue(c.HouseId, out var house)) house.SeatSettlementId = 0;
        c.Role = Role.None;
        var reach = Realms.Territory(world, victor).Select(r => r.Id).ToHashSet();
        var refuge = world.Settlements.Values.Where(s => !s.Abandoned && s.Tier >= SettlementTier.Town && !reach.Contains(s.RegionId))
            .OrderBy(s => s.Id).ToList();
        if (refuge.Count == 0 || ctx.Rng.Chance(0.5))
        {
            Fates.Die(ctx, c, "executed", causes);
            return;
        }
        var to = refuge[ctx.Rng.NextInt(0, refuge.Count)];
        var from = world.Settlements[c.SettlementId];
        c.SettlementId = to.Id;
        ctx.Emit(new EventDraft("FledIntoExile", Actors: new Dictionary<string, EntityRef> { ["character"] = c.Ref, ["from"] = from.Ref },
            Place: to.Ref, Causes: causes, Importance: Fates.Importance(c, 35), Tags: new[] { "Characters", "Politics" }));
    }

    /// <summary>The rebel realm folds into the crown: its land, treasury and stores pass to it, quietly (the revolution's end is the news).</summary>
    private static void Dissolve(World world, Realm rebels, Realm into)
    {
        if (rebels.Destroyed) return;
        rebels.Destroyed = true;
        rebels.Phase = RealmPhase.Collapsed;
        foreach (var region in world.Regions.Values.Where(r => r.OwnerRealmId == rebels.Id))
        {
            region.OwnerRealmId = into.Id;
            if (region.OccupierRealmId == into.Id) region.OccupierRealmId = 0;
        }
        foreach (var region in world.Regions.Values.Where(r => r.OccupierRealmId == rebels.Id)) region.OccupierRealmId = 0;
        into.Treasury += Math.Max(0, rebels.Treasury);
        rebels.Treasury = 0;
        foreach (var (kind, stock) in rebels.Stocks) into.Stocks[kind] = into.Stocks.GetValueOrDefault(kind) + stock;
        rebels.Stocks.Clear();
        rebels.Council.Clear();
        foreach (var vassal in Realms.VassalsOf(world, rebels).ToList()) vassal.LiegeId = into.Id;
        foreach (var other in world.Realms.Values) other.Treaties.RemoveAll(t => t.With == rebels.Id);
        rebels.Treaties.Clear();
        foreach (var war in world.Wars.Values.Where(w => !w.Ended && w.Involves(rebels.Id)))
        {
            war.Attackers.Remove(rebels.Id);
            war.Defenders.Remove(rebels.Id);
        }
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded && a.RealmId == rebels.Id)) Statecraft.Disband(world, army);
        rebels.Memories.Clear();
    }
}

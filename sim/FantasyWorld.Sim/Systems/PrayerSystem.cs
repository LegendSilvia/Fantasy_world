using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>The players' answers gathered since the last day (sub-project 13 N5): votes per prayer, and per face-off side.</summary>
public sealed record Tally(
    IReadOnlyDictionary<long, (int Grant, int Deny)> Prayers,
    IReadOnlyDictionary<long, (int A, int B)> Blessings)
{
    public static readonly Tally Empty = new(new Dictionary<long, (int, int)>(), new Dictionary<long, (int, int)>());

    /// <summary>The site's tally as JSON: {"prayers": {"12": {"grant": 3, "deny": 1}}, "blessings": {"456": {"a": 2, "b": 0}}}.</summary>
    public static Tally Parse(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        static int Count(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
        Dictionary<long, (int, int)> Read(string section, string first, string second)
        {
            var result = new Dictionary<long, (int, int)>();
            if (root.TryGetProperty(section, out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var item in items.EnumerateObject())
                    if (long.TryParse(item.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && item.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        result[id] = (Count(item.Value, first), Count(item.Value, second));
            return result;
        }
        return new Tally(Read("prayers", "grant", "deny"), Read("blessings", "a", "b"));
    }
}

/// <summary>
/// Prayer and the gods (sub-project 13 N5, the owner: the player is a god; characters and nations pray, the players
/// answer, and may sway a face-off). Each year the world's most pressing needs become prayers: a ruler at war prays for
/// victory, the wounded for healing, the starving for rain, the childless for an heir, the haunted for deliverance, the
/// wronged for vengeance. The players grant or deny them (<see cref="Apply"/>) and bless sides of the face-offs to come;
/// what they leave undecided for a year, the rules decide (the more zealous the faith, the likelier an answer). An
/// answered prayer is a miracle, and its faith grows; an unheeded one leaves the faithful doubting.
/// </summary>
public sealed class PrayerSystem : ISimSystem
{
    public const int PerYear = 3, OpenDays = 300, FavourDays = 400;
    public const double FavourStrength = 1.25;

    public string Name => "prayer";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var prayer in ctx.World.Prayers.Values.Where(p => p.IsOpen && ctx.Date.Day - p.Day >= OpenDays).ToList())
        {
            var zeal = ctx.World.Faiths.GetValueOrDefault(prayer.FaithId)?.Zeal ?? 0;
            Answer(ctx, prayer, ctx.Rng.Chance(0.15 + zeal / 300), byPlayers: false);
        }
        Rise(ctx);
    }

    public static bool Favoured(World world, EntityRef who) => world.Favour.TryGetValue(who.ToString(), out var until) && until >= world.Date.Day;

    private sealed record Need(Character Supplicant, string Plea, EntityRef? About, double Weight);

    private static void Rise(SimContext ctx)
    {
        var world = ctx.World;
        var praying = world.Prayers.Values.Where(p => p.IsOpen).Select(p => p.SupplicantId).ToHashSet();
        var needs = new List<Need>();
        bool May(Character? c) => c is { IsActive: true } && !praying.Contains(c.Id) && world.Faiths.ContainsKey(c.FaithId) && !SagaSystem.IsDemonLord(world, c);
        Character? LordOf(Region r) => world.LivingSettlements(r).OrderByDescending(s => s.Population).Select(s => world.Characters.GetValueOrDefault(s.LordId)).FirstOrDefault(c => c is not null);

        foreach (var war in world.Wars.Values.Where(w => !w.Ended && !w.Mustering(ctx.Date)))
            foreach (var side in new[] { war.AttackerId, war.DefenderId })
                if (world.Realms.TryGetValue(side, out var realm) && !realm.Destroyed && Realms.RulerOf(world, realm) is { } ruler && May(ruler)
                    && world.Realms.TryGetValue(side == war.AttackerId ? war.DefenderId : war.AttackerId, out var foe)
                    && DiplomacySystem.Strength(world, realm) < DiplomacySystem.Strength(world, foe))
                    needs.Add(new Need(ruler, Prayer.Victory, war.Ref, 50));
        foreach (var c in world.ActiveCharacters.Where(c => c.Wounds.Count > 0 && c.Renown >= 30 && May(c)))
            needs.Add(new Need(c, Prayer.Healing, null, 30 + c.Renown / 3));
        foreach (var region in world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.Shortage)))
            if (LordOf(region) is { } lord && May(lord)) needs.Add(new Need(lord, Prayer.Rain, region.Ref, 40));
        foreach (var realm in Realms.Independent(world))
            if (Realms.RulerOf(world, realm) is { } ruler && May(ruler) && ruler.Age(ctx.Date) >= 30 && ruler.Children.Count == 0
                && world.Characters.TryGetValue(ruler.SpouseId, out var spouse) && spouse.IsActive)
                needs.Add(new Need(ruler, Prayer.Heir, null, 35));
        foreach (var beast in world.Monsters.Values.Where(m => m.Alive && m.Might >= 50 && m.State != MonsterState.Slumbering))
            if (LordOf(world.Regions[beast.RegionId]) is { } lord && May(lord)) needs.Add(new Need(lord, Prayer.Deliverance, beast.Ref, 30 + beast.Might / 3));
        foreach (var c in world.ActiveCharacters.Where(c => c.Ambition == Ambitions.Vengeance && c.Renown >= 20 && May(c)))
            if (world.Characters.TryGetValue(c.AmbitionTarget, out var foe) && foe.IsActive) needs.Add(new Need(c, Prayer.Vengeance, foe.Ref, 25));

        var chosen = new HashSet<long>();
        foreach (var need in needs.OrderByDescending(n => n.Weight + ctx.Rng.NextDouble() * 10).ThenBy(n => n.Supplicant.Id))
        {
            if (chosen.Count >= PerYear) break;
            if (!chosen.Add(need.Supplicant.Id)) continue;
            var faith = world.Faiths[need.Supplicant.FaithId];
            var actors = new Dictionary<string, EntityRef> { ["supplicant"] = need.Supplicant.Ref };
            if (need.About is { } about) actors["about"] = about;
            var id = world.Ids.Take(Prayer.Kind);
            var offered = ctx.Emit(new EventDraft("PrayerOffered", Actors: actors, Place: world.Settlements[need.Supplicant.SettlementId].Ref,
                Payload: new Dictionary<string, string>
                {
                    ["plea"] = need.Plea, ["god"] = faith.Deity, ["prayer"] = id.ToString(CultureInfo.InvariantCulture),
                },
                Importance: 30, Tags: new[] { "Faith" }));
            world.Prayers[id] = new Prayer
            {
                Id = id, EventId = offered.Id, SupplicantId = need.Supplicant.Id, FaithId = faith.Id, Plea = need.Plea, About = need.About,
                Day = ctx.Date.Day,
            };
        }
    }

    /// <summary>
    /// The players' answers, before the day's history (sub-project 13 N5): each open prayer most of them granted is
    /// answered, each most denied goes unheeded (a tie waits); each face-off side most of them blessed is favoured.
    /// </summary>
    public static IReadOnlyList<WorldEvent> Apply(SimContext ctx, Tally tally)
    {
        var told = new List<WorldEvent>();
        foreach (var (id, (grant, deny)) in tally.Prayers.OrderBy(p => p.Key))
            if (ctx.World.Prayers.TryGetValue(id, out var prayer) && prayer.IsOpen && grant != deny)
                told.Add(Answer(ctx, prayer, grant > deny, byPlayers: true, grant, deny));
        foreach (var (eventId, (a, b)) in tally.Blessings.OrderBy(p => p.Key))
            if (a != b && Bless(ctx, eventId, a > b, Math.Max(a, b)) is { } blessed) told.Add(blessed);
        return told;
    }

    public static WorldEvent Answer(SimContext ctx, Prayer prayer, bool granted, bool byPlayers, int grant = 0, int deny = 0)
    {
        var world = ctx.World;
        var supplicant = world.Characters[prayer.SupplicantId];
        var faith = world.Faiths.GetValueOrDefault(prayer.FaithId);
        var actors = new Dictionary<string, EntityRef> { ["supplicant"] = supplicant.Ref };
        if (prayer.About is { } about) actors["about"] = about;
        var payload = new Dictionary<string, string> { ["plea"] = prayer.Plea, ["god"] = faith?.Deity ?? "the gods", ["by"] = byPlayers ? "players" : "fate" };
        if (byPlayers) (payload["grant"], payload["deny"]) = (grant.ToString(CultureInfo.InvariantCulture), deny.ToString(CultureInfo.InvariantCulture));
        var place = world.Settlements[supplicant.SettlementId].Ref;
        var answered = ctx.Emit(new EventDraft(granted ? "PrayerAnswered" : "PrayerUnheeded", Actors: actors, Place: place, Payload: payload,
            Causes: new[] { new Cause(prayer.EventId, CauseKind.Trigger, 1.0) }, Importance: granted ? 40 : 15, Tags: new[] { "Faith" }));
        prayer.State = granted ? Prayer.Granted : Prayer.Denied;
        prayer.AnsweredEventId = answered.Id;
        if (faith is not null) faith.Zeal = Math.Clamp(faith.Zeal + (granted ? 3 : -1), 0, 100);
        if (!granted)
        {
            if (!supplicant.Has(Traits.Devout) && !supplicant.Has(Traits.Cynical) && ctx.Rng.Chance(0.3)) supplicant.Traits.Add(Traits.Cynical);
            return answered;
        }
        if (supplicant.Has(Traits.Cynical)) supplicant.Traits.Remove(Traits.Cynical);
        var until = world.Date.Day + FavourDays;
        switch (prayer.Plea)
        {
            case Prayer.Victory when prayer.About is { } warRef && world.Wars.TryGetValue(warRef.Id, out var war):
                var realm = world.Realms.Values.FirstOrDefault(r => !r.Destroyed && war.Involves(r.Id) && Realms.RulerOf(world, r)?.Id == supplicant.Id);
                if (realm is not null) world.Favour[realm.Ref.ToString()] = until;
                break;
            case Prayer.Healing:
                supplicant.Wounds.Clear();
                break;
            case Prayer.Rain when prayer.About is { } regionRef && world.Regions.TryGetValue(regionRef.Id, out var region):
                region.Fertility = Math.Min(100, region.Fertility + 15);
                region.Food = region.Food * 2 + 500;
                break;
            case Prayer.Heir when world.Characters.TryGetValue(supplicant.SpouseId, out var spouse) && spouse.IsActive:
                var (mother, father) = supplicant.Sex == Sex.Female ? (supplicant, spouse) : (spouse, supplicant);
                FamilySystem.Birth(ctx, mother, father);
                break;
            case Prayer.Deliverance when prayer.About is { } beastRef && world.Monsters.TryGetValue(beastRef.Id, out var beast) && beast.Alive:
                beast.Might = Math.Max(1, beast.Might - 35);
                if (beast.Might <= 15) (beast.State, beast.SlainEventId) = (MonsterState.Slain, answered.Id);
                break;
            case Prayer.Vengeance when prayer.About is { } foeRef && world.Characters.TryGetValue(foeRef.Id, out var foe) && foe.IsActive:
                Fates.Injure(foe, Fate.Wounded, ctx.Rng);
                world.Favour[supplicant.Ref.ToString()] = until;
                break;
        }
        return answered;
    }

    /// <summary>The face-offs the players may still bless: duels and wars declared and not yet fought, and not blessed.</summary>
    public static IReadOnlyList<WorldEvent> Blessable(World world, EventLog log) =>
        log.All.Where(e => e.Type is "DuelDeclared" or "WarDeclared" && world.Date.Day - e.Date.Day <= FavourDays && Blessable(world, log, e)).ToList();

    private static bool Blessable(World world, EventLog log, WorldEvent declared)
    {
        if (declared.Type == "WarDeclared")
        {
            if (!declared.Actors.TryGetValue("war", out var w) || !world.Wars.TryGetValue(w.Id, out var war) || war.Ended || !war.Mustering(world.Date)) return false;
        }
        else if (declared.Type == "DuelDeclared")
        {
            if (log.Since(declared.Id).Any(e => e.Type is "DuelBegun" or "DuelFought" or "DuelCalledOff" && e.Causes.Any(c => c.EventId == declared.Id))) return false;
        }
        else return false;
        return !log.Since(declared.Id).Any(e => e.Type == "GodsBlessed" && e.Causes.Any(c => c.EventId == declared.Id));
    }

    /// <summary>
    /// The players bless a side of a face-off to come (the declaration's first side, or its second): the gods favour
    /// that duellist or realm in the fight. Null if the face-off is unknown or its side gone.
    /// </summary>
    public static WorldEvent? Bless(SimContext ctx, long declaredEventId, bool firstSide, int votes)
    {
        var world = ctx.World;
        if (!ctx.Log.TryGet(declaredEventId, out var declared) || !Blessable(world, ctx.Log, declared)) return null;
        var key = (declared.Type, firstSide) switch
        {
            ("DuelDeclared", true) => "challenger", ("DuelDeclared", false) => "defender",
            ("WarDeclared", true) => "attacker", ("WarDeclared", false) => "defender",
            _ => null,
        };
        if (key is null || !declared.Actors.TryGetValue(key, out var blessed)) return null;
        var alive = blessed.Kind == Character.Kind ? world.Characters.TryGetValue(blessed.Id, out var c) && c.IsActive
            : world.Realms.TryGetValue(blessed.Id, out var r) && !r.Destroyed;
        if (!alive) return null;
        world.Favour[blessed.ToString()] = world.Date.Day + FavourDays;
        return ctx.Emit(new EventDraft("GodsBlessed", Actors: new Dictionary<string, EntityRef> { ["blessed"] = blessed },
            Place: declared.Place, Payload: new Dictionary<string, string> { ["votes"] = votes.ToString(CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 30, Tags: new[] { "Faith" }));
    }
}

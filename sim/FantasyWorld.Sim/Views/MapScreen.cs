using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public enum PinMarker { Plain, Realm, RecentEvent, Conflict }

public sealed record MapRegion(EntityRef Ref, string Name, string Biome, MapPoint? Center, EntityRef? Owner, EntityRef? Occupier, string Colour);

public sealed record MapCard(string Kicker, string Title, string Line);

/// <summary>A settlement pin; Marks are the buildings worth drawing beside it today ("temple", "mage tower").</summary>
public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card,
    IReadOnlyList<string>? Marks = null, int Level = 0);

/// <summary>A structure as it was at the date; Detail says how deep a labyrinth has been delved ("level 2 of 5").</summary>
public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position, string? Detail = null);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// A host in the field: who leads it and who rides with it, its strength, what it is doing, and the road it means to
/// take (its region, then each region on its path, ending at its target).
/// </summary>
public sealed record MapArmy(
    EntityRef Ref, string Name, EntityRef Realm, string Colour, EntityRef War, string Side, Mention? Commander, IReadOnlyList<Mention> Knights,
    int Soldiers, string Objective, bool Besieging, MapPoint Position, IReadOnlyList<MapPoint> Route);

/// <summary>A trade road on the map: its markets, the way it runs (market, regions between, market) and its state.</summary>
/// <summary>A trade route on the map; <c>Sea</c> marks each leg (Path[i] to Path[i+1]) sailed rather than walked.</summary>
public sealed record MapRoute(EntityRef Ref, string Name, EntityRef From, EntityRef To, string State, int Volume, IReadOnlyList<MapPoint> Path,
    IReadOnlyList<bool>? Sea = null);

/// <summary>A beast in its lair: what it is, how mighty, whether it is a legend or asleep, and the event of its rising.</summary>
public sealed record MapMonster(EntityRef Ref, string Name, string Breed, int Might, bool Legendary, bool Slumbering, MapPoint Position, long RoseEventId,
    bool Guarding = false);

/// <summary>An independent realm's standard on the map, always shown: its name, rank, heraldic colours, where its capital stands and how many regions it holds.</summary>
public sealed record MapRealm(EntityRef Ref, string Name, string Rank, string Colour, string Accent, MapPoint Position, int Regions);

/// <summary>A battle of the last year: where, who won, and the event to explain it.</summary>
/// <summary>A fight on the map (the owner, 2026-10-28: crossed swords where fights occur): a battle or a duel, fought this past
/// year (Victor and Vanquished) or still under way (the attacker or challenger, then the defender). War is unset for a duel.</summary>
public sealed record MapBattle(long EventId, int Year, MapPoint Position, EntityRef Victor, EntityRef Vanquished, EntityRef War,
    string Kind = MapBattle.Battle, bool UnderWay = false)
{
    public const string Battle = "battle", Duel = "duel", Siege = "siege";
}

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend,
    IReadOnlyList<MapArmy>? Armies = null, IReadOnlyList<MapBattle>? Battles = null, IReadOnlyList<MapRoute>? Routes = null,
    IReadOnlyList<MapMonster>? Monsters = null, IReadOnlyList<MapRealm>? Realms = null)
{
    /// <summary>Battles shown on the map: those of the last year.</summary>
    public const int BattleDays = 360;
    public static MapScreen Build(World world, EventLog log, WorldDate? asOf = null)
    {
        var requested = asOf is { } d && d < world.Date ? d : world.Date;
        var map = History.PoliticalMapAt(world, log, requested);
        var date = map.Historical ? requested : world.Date;
        var today = date == world.Date;
        var political = map.Regions;
        EntityRef? RealmRef(long id) => id != 0 && world.Realms.TryGetValue(id, out var r) ? r.Ref : null;
        string Colour(long id) => id != 0 && world.Realms.TryGetValue(map.TopOf(id), out var top) ? top.Colors[0] : "none";

        var regions = world.Regions.Values.Select(r => new MapRegion(r.Ref, r.Name, r.Biome.ToString(), MapPoint.Of(world, r.Ref),
            RealmRef(political[r.Id].OwnerRealmId), RealmRef(political[r.Id].OccupierRealmId), Colour(political[r.Id].OwnerRealmId))).ToList();

        var abandoned = log.All.Where(e => e.Type == "SettlementAbandoned" && e.Date <= date && e.Place is { Kind: Settlement.Kind })
            .Select(e => e.Place!.Value.Id).ToHashSet();
        var season = date.Day - 90;
        var settlements = new List<MapSettlement>();
        foreach (var s in world.Settlements.Values.Where(s => !abandoned.Contains(s.Id)))
        {
            var state = political[s.RegionId];
            var here = log.Involving(s.Ref).Where(e => e.Date <= date).ToList();
            var marker = state.OccupierRealmId != 0 ? PinMarker.Conflict
                : here.Any(e => e.Date.Day > season && e.Importance >= 30) ? PinMarker.RecentEvent
                : state.OwnerRealmId != 0 ? PinMarker.Realm : PinMarker.Plain;
            var holder = world.Realms.GetValueOrDefault(state.OwnerRealmId);
            var ceded = log.Involving(world.Regions[s.RegionId].Ref).LastOrDefault(e => e.Type == "RegionCeded" && e.Date <= date);
            var title = holder is null ? "Held by no realm"
                : $"Held by the {Statecraft.Title(holder)} {(ceded is null ? "since the founding" : $"since Year {ceded.Date.Year}")}";
            if (state.OccupierRealmId != 0 && world.Realms.TryGetValue(state.OccupierRealmId, out var occupier))
                title += $", occupied by {occupier.Name}";
            var thisYear = here.Count(e => e.Date.Year == date.Year);
            var line = (today ? $"Pop. {s.Population.ToString("N0", CultureInfo.InvariantCulture)} · " : "")
                       + $"{thisYear} event{(thisYear == 1 ? "" : "s")} this year";
            settlements.Add(new MapSettlement(s.Ref, s.Name, s.Tier.ToString(), MapPoint.Of(world, s.Ref), holder?.Ref, marker,
                new MapCard($"{s.Name} · {s.Tier}", title, line), today ? Marked.Where(s.Buildings.Contains).ToList() : Array.Empty<string>(), Levels.Of(world, s)));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => Cleared(log, st, date)
                ? new MapStructure(st.Ref, st.Name, st.Type.ToString(), st.State.ToString(), MapPoint.Of(world, st.Ref), "cleared")
                : st.GuardianId != 0 || st.Type == StructureKind.Labyrinth
                    ? new MapStructure(st.Ref, st.Name, nameof(StructureKind.Labyrinth), nameof(Model.StructureState.Standing), MapPoint.Of(world, st.Ref),
                        today && st.Levels > 0 ? $"level {st.Depth} of {st.Levels}" : null)
                    : new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
            .ToList();

        var borders = world.Geography is { } geo
            ? geo.Graph.Links.Where(l => political[l.A].OwnerRealmId != political[l.B].OwnerRealmId)
                .Select(l => new Border(world.Regions[Math.Min(l.A, l.B)].Ref, world.Regions[Math.Max(l.A, l.B)].Ref)).Distinct().ToList()
            : new List<Border>();

        var conflicts = world.Wars.Values.Where(w => w.StartDay <= date.Day && (!w.Ended || log.TryGet(w.EndedEventId, out var end) && end.Date > date))
            .Select(w => (War: w, Sides: map.Wars.GetValueOrDefault(w.Id) ?? new WarSides(w.Attackers, w.Defenders)))
            .Select(x => new Conflict(x.War.Ref, x.War.Name, x.Sides.Attackers.Select(id => world.Realms[id].Ref).ToList(),
                x.Sides.Defenders.Select(id => world.Realms[id].Ref).ToList(),
                political.Values.Where(p => p.OccupyingWarId == x.War.Id).Select(p => world.Regions[p.RegionId].Ref).ToList()))
            .ToList();

        var independent = political.Values.Select(p => p.OwnerRealmId).Where(id => id != 0).Select(map.TopOf).Distinct()
            .Select(id => world.Realms[id]).OrderBy(r => r.Id).ToList();
        var legend = independent.Select(r => new LegendEntry(r.Name, r.Colors[0]))
            .Append(new LegendEntry("Active conflict", "conflict")).Append(new LegendEntry("Event this season", "event")).ToList();
        // Each independent realm's standard at its capital (else at the heart of its largest-held region).
        var held = political.Values.Where(p => p.OwnerRealmId != 0).GroupBy(p => map.TopOf(p.OwnerRealmId)).ToDictionary(g => g.Key, g => g.Select(p => p.RegionId).ToList());
        var realms = independent.Select(r =>
            {
                var regions = held.GetValueOrDefault(r.Id) ?? [];
                var at = world.Settlements.TryGetValue(r.CapitalId, out var capital) && !capital.Abandoned && regions.Contains(capital.RegionId)
                    ? MapPoint.Of(world, capital.Ref)
                    : regions.Select(id => MapPoint.Of(world, world.Regions[id].Ref)).FirstOrDefault(p => p is not null);
                return at is null ? null : new MapRealm(r.Ref, r.Name, r.Rank.ToString(), r.Colors[0], r.Colors.Length > 1 ? r.Colors[1] : r.Colors[0], at, regions.Count);
            })
            .OfType<MapRealm>().ToList();
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend, today ? ArmiesIn(world) : Array.Empty<MapArmy>(), BattlesBefore(world, log, date), RoutesAt(world, log, date, today), MonstersAt(world, log, date), realms);
    }

    /// <summary>Buildings drawn beside their settlement.</summary>
    public static readonly IReadOnlyList<string> Marked = new[] { "temple", "mage tower" };

    /// <summary>A labyrinth cleared by the date has become a ruin or a shrine.</summary>
    private static bool Cleared(EventLog log, Structure st, WorldDate date) =>
        st.ClearedEventId != 0 && log.TryGet(st.ClearedEventId, out var cleared) && cleared.Date <= date;

    /// <summary>Hosts in the field today (armies are not kept in history, so past maps show none).</summary>
    private static IReadOnlyList<MapArmy> ArmiesIn(World world)
    {
        Mention? Of(long id) => world.Characters.TryGetValue(id, out var c) && c.IsActive ? new Mention(c.Ref, Names.Full(world, c)) : null;
        MapPoint? At(long region) => world.Regions.TryGetValue(region, out var r) ? MapPoint.Of(world, r.Ref) : null;
        var armies = new List<MapArmy>();
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded).OrderBy(a => a.Id))
        {
            if (At(army.RegionId) is not { } here || !world.Wars.TryGetValue(army.WarId, out var war) || !world.Realms.TryGetValue(army.RealmId, out var realm)) continue;
            var route = new List<MapPoint> { here };
            route.AddRange(army.Path.Select(At).OfType<MapPoint>());
            armies.Add(new MapArmy(army.Ref, army.Name, realm.Ref, realm.Colors[0], war.Ref, war.IsAttacker(realm.Id) ? "attacker" : "defender",
                Of(army.CommanderId), army.Knights.Select(Of).OfType<Mention>().ToList(), army.Soldiers, army.Objective.ToString().ToLowerInvariant(),
                world.Regions[army.RegionId].BesiegerArmyId == army.Id, here, route));
        }
        return armies;
    }

    /// <summary>Beasts that had risen by the date and were not yet slain then.</summary>
    private static IReadOnlyList<MapMonster> MonstersAt(World world, EventLog log, WorldDate date) =>
        world.Monsters.Values
            .Where(m => log.TryGet(m.RoseEventId, out var rose) && rose.Date <= date
                        && !(m.State == MonsterState.Slain && log.TryGet(m.SlainEventId, out var slain) && slain.Date <= date))
            .Select(m => (Beast: m, At: MapPoint.Of(world, world.Regions[m.RegionId].Ref)))
            .Where(x => x.At is not null)
            // A beast none had yet lived to describe is not on the map at all (the owner, 2026-10-29).
            .Where(x => x.Beast.Revealed && log.TryGet(x.Beast.RevealedEventId, out var seen) && seen.Date <= date)
            .Select(x => new MapMonster(x.Beast.Ref, x.Beast.Name, x.Beast.Breed,
                (int)Math.Round(x.Beast.Might), x.Beast.Legendary,
                x.Beast.State == MonsterState.Slumbering, x.At!, x.Beast.RoseEventId, x.Beast.State == MonsterState.Lairing))
            .ToList();

    /// <summary>Roads opened by the date; their state then is replayed from their severing and restoring.</summary>
    private static IReadOnlyList<MapRoute> RoutesAt(World world, EventLog log, WorldDate date, bool today)
    {
        var routes = new List<MapRoute>();
        foreach (var route in world.Routes.Values)
        {
            if (!log.TryGet(route.OpenedEventId, out var opened) || opened.Date > date) continue;
            var state = route.State;
            if (!today)
            {
                var last = log.Involving(route.Ref).LastOrDefault(e => e.Date <= date && e.Type is "RouteSevered" or "RouteRestored");
                state = last?.Type == "RouteSevered" ? RouteState.Severed : RouteState.Open;
                // A road closes for good when a market is abandoned or shrinks below a town.
                if (log.Involving(route.Ref).Any(e => e.Type == "RouteClosed" && e.Date <= date))
                    state = RouteState.Closed;
            }
            if (state == RouteState.Closed) continue;
            var (from, to) = (world.Settlements[route.FromId], world.Settlements[route.ToId]);
            var path = new List<MapPoint>();
            var regions = new List<long>();
            if (MapPoint.Of(world, from.Ref) is { } start) (path, regions) = ([start], [route.Regions[0]]);
            foreach (var r in route.Regions.Skip(1).SkipLast(1))
                if (MapPoint.Of(world, world.Regions[r].Ref) is { } middle)
                {
                    path.Add(middle);
                    regions.Add(r);
                }
            if (MapPoint.Of(world, to.Ref) is { } end)
            {
                path.Add(end);
                regions.Add(route.Regions[^1]);
            }
            if (path.Count < 2) continue;
            // Which legs cross the sea (the owner, 2026-10-29: sea routes drawn over the water).
            var graph = world.Geography?.Graph;
            var sea = Enumerable.Range(0, regions.Count - 1).Select(i => graph?.Between(regions[i], regions[i + 1])?.Kind == LinkKind.Sea).ToList();
            routes.Add(new MapRoute(route.Ref, route.Name, from.Ref, to.Ref, state.ToString().ToLowerInvariant(), (int)Math.Round(today ? route.Volume : 0), path,
                sea.Any(x => x) ? sea : null));
        }
        return routes;
    }

    /// <summary>The fights of the past year, and those begun and not yet ended (a battle or duel that lasts days).</summary>
    private static IReadOnlyList<MapBattle> BattlesBefore(World world, EventLog log, WorldDate date)
    {
        var fights = new List<MapBattle>();
        // A challenge or clash is under way from the day it is made until it is fought or called off (the owner, 2026-10-30:
        // every fight, the ones still to come included, is on the map); once begun, the duel's own start stands for it.
        var ended = new HashSet<long>();
        foreach (var e in log.All)
        {
            if (e.Date > date) break;
            if (e.Type is "BattleFought" or "BattleBrokenOff" or "DuelFought" or "DuelCalledOff" or "DuelBegun" or "SiegeEnded")
                foreach (var c in e.Causes.Where(c => c.Kind == CauseKind.Trigger)) ended.Add(c.EventId);
        }
        foreach (var e in log.All)
        {
            if (e.Date > date) break;
            if (e.Place is not { } p || MapPoint.Of(world, p) is not { } at) continue;
            var recent = e.Date.Day > date.Day - BattleDays;
            switch (e.Type)
            {
                case "BattleFought" when recent:
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, e.Actors["victor"], e.Actors["vanquished"], e.Actors["war"]));
                    break;
                case "BattleJoined" when !ended.Contains(e.Id) && e.Actors.TryGetValue("attacker", out var a) && e.Actors.TryGetValue("defender", out var d):
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, a, d, e.Actors.GetValueOrDefault("war"), MapBattle.Battle, UnderWay: true));
                    break;
                case "DuelFought" when recent && e.Actors.TryGetValue("challenger", out var a) && e.Actors.TryGetValue("defender", out var d):
                    var challengerWon = e.Payload.GetValueOrDefault("outcome")?.StartsWith("attacker", StringComparison.Ordinal) ?? true;
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, challengerWon ? a : d, challengerWon ? d : a, default, MapBattle.Duel));
                    break;
                case "DuelBegun" when !ended.Contains(e.Id) && e.Actors.TryGetValue("challenger", out var a) && e.Actors.TryGetValue("defender", out var d):
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, a, d, default, MapBattle.Duel, UnderWay: true));
                    break;
                case "DuelDeclared" or "ClashBegun" when !ended.Contains(e.Id) && e.Date.Day > date.Day - PendingDays
                                                        && e.Actors.TryGetValue("challenger", out var a) && e.Actors.TryGetValue("defender", out var d)
                                                        && Alive(world, a, date) && Alive(world, d, date):
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, a, d, default, MapBattle.Duel, UnderWay: true));
                    break;
                case "SiegeBegan" when !ended.Contains(e.Id) && Besieging(world, log, e, date):
                    fights.Add(new MapBattle(e.Id, e.Date.Year, at, e.Actors.GetValueOrDefault("army"), default, e.Actors.GetValueOrDefault("war"), MapBattle.Siege, UnderWay: true));
                    break;
            }
        }
        return fights;
    }

    /// <summary>A challenge or clash not fought within two years has lapsed (its people moved on, or the world forgot it).</summary>
    public const int PendingDays = 2 * WorldDate.DaysPerYear;

    private static bool Alive(World world, EntityRef who, WorldDate date) =>
        who.Kind != Character.Kind || !world.Characters.TryGetValue(who.Id, out var c) || c.Died is not { } died || died > date;

    /// <summary>
    /// A siege goes on until the town falls, its war ends, or (today) its besiegers have left.
    /// </summary>
    private static bool Besieging(World world, EventLog log, WorldEvent began, WorldDate date)
    {
        if (began.Actors.TryGetValue("war", out var w) && world.Wars.TryGetValue(w.Id, out var war)
            && war.EndedEventId != 0 && log.TryGet(war.EndedEventId, out var end) && end.Date <= date) return false;
        if (date < world.Date) return true;
        return began.Place is { Kind: Settlement.Kind } at && world.Settlements.TryGetValue(at.Id, out var town)
            && world.Regions.TryGetValue(town.RegionId, out var region) && region.BesiegerArmyId != 0;
    }

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type is not (StructureKind.Landmark or StructureKind.Fortification)) return st.State.ToString();
        if (RuinedBy(world, log, st, date)) return nameof(Model.StructureState.Ruined);
        // A great work's state at a date is its latest turn by then: completed, restored, or a collapse that ruined it.
        var turn = log.Involving(st.Ref).LastOrDefault(e => e.Date <= date
            && e.Type is "LandmarkCompleted" or "FortificationCompleted" or "RestorationBegun" or "WorksCollapsed");
        return turn?.Type switch
        {
            "LandmarkCompleted" or "FortificationCompleted" => nameof(Model.StructureState.Standing),
            "WorksCollapsed" when turn.Payload.GetValueOrDefault("ruined") == "yes" => nameof(Model.StructureState.Ruined),
            _ => nameof(Model.StructureState.Building),
        };
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}

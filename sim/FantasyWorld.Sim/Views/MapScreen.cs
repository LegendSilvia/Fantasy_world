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

public sealed record MapSettlement(EntityRef Ref, string Name, string Tier, MapPoint? Position, EntityRef? Holder, PinMarker Marker, MapCard Card);

public sealed record MapStructure(EntityRef Ref, string Name, string Kind, string State, MapPoint? Position);

public sealed record Border(EntityRef A, EntityRef B);

public sealed record Conflict(EntityRef War, string Name, IReadOnlyList<EntityRef> Attackers, IReadOnlyList<EntityRef> Defenders, IReadOnlyList<EntityRef> Occupied);

public sealed record LegendEntry(string Label, string Colour);

/// <summary>
/// The *Diorama map HUD* (sub-project 2 §3.2) at any date: regions with their holder and occupier, settlement pins with
/// hover cards, landmarks and labyrinths, borders between realms, wars under way and a legend. Political state comes
/// from <see cref="History"/>; figures that are not historical (population) show only for today.
/// </summary>
public sealed record MapScreen(
    string WorldName, string Summary, int Year, int FirstYear, bool Historical, IReadOnlyList<MapRegion> Regions, IReadOnlyList<MapSettlement> Settlements,
    IReadOnlyList<MapStructure> Structures, IReadOnlyList<Border> Borders, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<LegendEntry> Legend)
{
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
                new MapCard($"{s.Name} · {s.Tier}", title, line)));
        }

        var structures = world.Structures.Values.Where(st => log.TryGet(st.StartedEventId, out var begun) && begun.Date <= date)
            .Select(st => new MapStructure(st.Ref, st.Name, st.Type.ToString(), StructureState(world, log, st, date), MapPoint.Of(world, st.Ref)))
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
        var labyrinths = structures.Count(st => st.Kind == nameof(StructureKind.Labyrinth));
        var summary = $"{Count(independent.Count, "realm")}, {Count(settlements.Count, "settlement")}, "
                      + $"{Count(labyrinths, "open labyrinth")}.";
        return new MapScreen(DigestScreen.DisplayName(world), char.ToUpperInvariant(summary[0]) + summary[1..], date.Year, 1, map.Historical,
            regions, settlements, structures, borders, conflicts, legend);
    }

    /// <summary>A landmark is building until its completion event, and ruined once its settlement's ruin event has passed.</summary>
    private static string StructureState(World world, EventLog log, Structure st, WorldDate date)
    {
        if (st.Type == StructureKind.Landmark && !(st.CompletedEventId != 0 && log.TryGet(st.CompletedEventId, out var done) && done.Date <= date))
            return st.State == Model.StructureState.Ruined && RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Building);
        if (st.Type == StructureKind.Landmark && st.State == Model.StructureState.Ruined)
            return RuinedBy(world, log, st, date) ? nameof(Model.StructureState.Ruined) : nameof(Model.StructureState.Standing);
        return st.State.ToString();
    }

    private static bool RuinedBy(World world, EventLog log, Structure st, WorldDate date) =>
        world.Settlements.TryGetValue(st.SettlementId, out var home)
        && log.Involving(home.Ref).Any(e => e.Type == "StructureRuined" && e.Date <= date);

    private static string Count(int n, string noun) =>
        $"{(n < Words.Length ? Words[n] : n.ToString(CultureInfo.InvariantCulture))} {noun}{(n == 1 ? "" : "s")}";

    private static readonly string[] Words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
}

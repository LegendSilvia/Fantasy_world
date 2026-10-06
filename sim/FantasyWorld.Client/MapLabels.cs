using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>The map HUD's layer toggles (sub-project 4 §4); Armies covers hosts, fronts, occupation and battles.</summary>
[Flags]
public enum MapLayers
{
    None = 0,
    Borders = 1,
    Settlements = 2,
    Events = 4,
    Structures = 8,
    TradeRoutes = 16,
    Armies = 32,
    Default = Borders | Settlements | Events | Structures | Armies | TradeRoutes,
}

/// <summary>Which settlements get a name tag at each zoom, and where a linked place is on the map.</summary>
public static class MapLabels
{
    /// <summary>
    /// Settlements with news (a conflict or this season's event) are tagged whenever the Events layer is on; others
    /// when Settlements is on and they are big enough for the zoom: towns and cities from afar, villages at region
    /// zoom, everything at town zoom.
    /// </summary>
    public static bool Shows(MapSettlement settlement, Zoom zoom, MapLayers layers)
    {
        if (settlement.Position is null) return false;
        if (settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent) return layers.HasFlag(MapLayers.Events);
        if (!layers.HasFlag(MapLayers.Settlements)) return false;
        var tier = Enum.TryParse<SettlementTier>(settlement.Tier, out var t) ? t : SettlementTier.Hamlet;
        return tier >= zoom switch { Zoom.Realm => SettlementTier.Town, Zoom.Region => SettlementTier.Village, _ => SettlementTier.Hamlet };
    }

    /// <summary>Where a settlement, region or structure sits on the map, if it is on it.</summary>
    public static MapPoint? Find(MapScreen map, EntityRef entity) =>
        map.Settlements.FirstOrDefault(s => s.Ref == entity)?.Position
        ?? map.Regions.FirstOrDefault(r => r.Ref == entity)?.Center
        ?? map.Structures.FirstOrDefault(s => s.Ref == entity)?.Position;

    /// <summary>The zoom a linked place opens at: a region shows its whole extent, anything smaller the town view.</summary>
    public static Zoom ZoomFor(EntityRef entity) => entity.Kind == Region.Kind ? Zoom.Region : Zoom.Town;
}

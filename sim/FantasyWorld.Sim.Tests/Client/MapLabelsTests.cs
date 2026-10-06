using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Client;

public class MapLabelsTests
{
    private static MapSettlement Settlement(string tier, PinMarker marker = PinMarker.Realm, double x = 0.5) =>
        new(new EntityRef("settlement", 1), "Esk", tier, new MapPoint(x, 0.5), null, marker, new MapCard("Esk · " + tier, "Held by no realm", ""));

    [Fact]
    public void BiggerPlacesAreNamedFromFurtherAway()
    {
        Assert.True(MapLabels.Shows(Settlement("City"), Zoom.Realm, MapLayers.Default));
        Assert.False(MapLabels.Shows(Settlement("Village"), Zoom.Realm, MapLayers.Default));
        Assert.True(MapLabels.Shows(Settlement("Village"), Zoom.Region, MapLayers.Default));
        Assert.True(MapLabels.Shows(Settlement("Hamlet"), Zoom.Town, MapLayers.Default));
        Assert.False(MapLabels.Shows(Settlement("City"), Zoom.Town, MapLayers.Default & ~MapLayers.Settlements));
    }

    [Fact]
    public void PlacesWithNews_FollowTheEventsLayer()
    {
        Assert.True(MapLabels.Shows(Settlement("Hamlet", PinMarker.Conflict), Zoom.Realm, MapLayers.Events));
        Assert.False(MapLabels.Shows(Settlement("City", PinMarker.RecentEvent), Zoom.Town, MapLayers.Default & ~MapLayers.Events));
    }

    [Fact]
    public void LinkedPlaces_AreFoundOnTheMap()
    {
        var esk = Settlement("Town", x: 0.25);
        var map = new MapScreen("W", "", 1, 1, false, Array.Empty<MapRegion>(), new[] { esk }, Array.Empty<MapStructure>(),
            Array.Empty<Border>(), Array.Empty<Conflict>(), Array.Empty<LegendEntry>());
        Assert.Equal(new MapPoint(0.25, 0.5), MapLabels.Find(map, esk.Ref));
        Assert.Null(MapLabels.Find(map, new EntityRef("region", 9)));
        Assert.Equal(Zoom.Region, MapLabels.ZoomFor(new EntityRef("region", 9)));
        Assert.Equal(Zoom.Town, MapLabels.ZoomFor(esk.Ref));
    }
}

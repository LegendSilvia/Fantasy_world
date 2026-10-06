using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class ScatterTests
{
    private static readonly TerrainGrid Grid =
        WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch).Geography!.Terrain;

    [Fact]
    public void Woods_GrowOnLand_ByBiome_AndTheSameEachTime()
    {
        var trees = Scatter.Trees(Grid);
        Assert.InRange(trees.Count, 200, Scatter.MaxTrees);
        Assert.All(trees, t => Assert.True(t.Position.Y > BlockMesh.Shore));
        Assert.All(trees, t => Assert.Equal(t.Position.Y, BlockMesh.HeightAt(Grid, (t.Position.X + 1) / 2, (t.Position.Z + 1) / 2), 3));
        Assert.Contains(trees, t => t.Kind == TreeKind.Broadleaf);
        Assert.Equal(trees, Scatter.Trees(Grid));
    }

    [Fact]
    public void Clearings_AreLeftAroundSettlements()
    {
        var centre = Scatter.Trees(Grid)[0].Position;
        Assert.DoesNotContain(Scatter.Trees(Grid, new[] { centre }), t => Vector2.Distance(new(t.Position.X, t.Position.Z), new(centre.X, centre.Z)) < 0.03f);
    }

    [Fact]
    public void BiggerPlaces_HaveMoreHouses_AndTownsAKeep()
    {
        Marker Place(string tier) => new(new EntityRef("settlement", 4), "Esk", MarkerShape.Settlement, new Vector3(0.1f, 0.05f, 0.1f),
            Markers.SizeOf(tier), Palette.Realm, PinMarker.Realm);
        var hamlet = Scatter.Houses(Place("Hamlet"), Grid);
        var city = Scatter.Houses(Place("City"), Grid);
        Assert.True(city.Count > 3 * hamlet.Count);
        Assert.DoesNotContain(hamlet, h => h.Tower);
        Assert.Single(city, h => h.Tower);
        Assert.All(city, h => Assert.True(Vector2.Distance(new(h.Position.X, h.Position.Z), new(0.1f, 0.1f)) <= Markers.SizeOf("City")));
        Assert.All(city, h => Assert.Equal(Palette.Realm, h.Roof));
    }
}

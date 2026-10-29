using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class MarkersTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(6, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void EverySettlementAndStructure_StandsOnTheBlock_SizedByTier()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var markers = Markers.For(map, Aged.World.Geography!);
        Assert.Equal(map.Settlements.Count + map.Structures.Count + map.Settlements.Sum(s => s.Marks?.Count ?? 0), markers.Count);
        Assert.All(markers, m => Assert.True(m.Position.X is >= -1.1f and <= 1.1f && m.Position.Z is >= -1.1f and <= 1.1f && m.Position.Y >= 0));
        var city = map.Settlements.MaxBy(s => Markers.SizeOf(s.Tier))!;
        Assert.Equal(Markers.SizeOf(city.Tier), markers.Single(m => m.Ref == city.Ref && m.Shape == MarkerShape.Settlement).Size);
        Assert.True(Markers.SizeOf("City") > Markers.SizeOf("Hamlet"));
        Assert.All(markers.Where(m => m.Kind == PinMarker.Conflict), m => Assert.Equal(Palette.Conflict, m.Colour));
    }

    [Fact]
    public void Borders_RunBetweenDifferentlyHeldRegions()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var borders = Markers.Borders(map, Aged.World.Geography!);
        Assert.Equal(map.Borders.Count > 0, borders.Count > 0);
        var grid = Aged.World.Geography!.Terrain;
        Assert.All(borders, b => Assert.InRange(Math.Abs(b.From.X - b.To.X) + Math.Abs(b.From.Z - b.To.Z), 0.5f / grid.Width, 2.5f / grid.Width));
    }

    [Fact]
    public void BorderLines_AreSmoothChains_CoveringEveryBorderEdge()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var lines = Markers.BorderLines(map, Aged.World.Geography!);
        Assert.Equal(map.Borders.Count > 0, lines.Count > 0);
        Assert.All(lines, l => Assert.True(l.Count >= 2));
        // Rounded corners: Chaikin cutting leaves no exact right-angle staircase of tile-length steps.
        var grid = Aged.World.Geography!.Terrain;
        Assert.True(lines.Sum(l => l.Count) > Markers.Borders(map, Aged.World.Geography!).Count);
        Assert.All(lines.SelectMany(l => l), p => Assert.True(p.Y > 0));
    }

    [Fact]
    public void Chains_WalkEdgesIntoLines_AndLoops()
    {
        TileCoord P(int x, int y) => new(x, y);
        var open = Markers.Chains(new[] { (P(0, 0), P(1, 0)), (P(1, 0), P(2, 0)), (P(2, 0), P(2, 1)) });
        Assert.Single(open);
        Assert.Equal(4, open[0].Count);
        var square = Markers.Chains(new[] { (P(0, 0), P(1, 0)), (P(1, 0), P(1, 1)), (P(1, 1), P(0, 1)), (P(0, 1), P(0, 0)) });
        Assert.Single(square);
        Assert.Equal(square[0][0], square[0][^1]);
    }

    [Fact]
    public void Labels_ThatWouldOverlap_GiveWayToMoreImportantOnes()
    {
        var labels = new[]
        {
            new LabelBox(10, 10, 80, 20, 0), new LabelBox(20, 15, 80, 20, 5), new LabelBox(200, 10, 80, 20, 0),
            new LabelBox(900, 10, 80, 20, 9), new LabelBox(500, 10, 80, 20, 1),
        };
        var visible = Declutter.Visible(labels, 1000, 800, new[] { new LabelBox(480, 0, 200, 200, int.MaxValue) });
        Assert.Equal(new[] { false, true, true, true, false }, visible);
    }

    [Fact]
    public void DigestPins_FloatAboveTheirPlaces_InOrder()
    {
        var digest = DigestScreen.Build(Aged.World, Aged.Log, FantasyWorld.Sim.Chronicle.Digest.Build(Aged.Log, Aged.World, 0, new HashSet<EntityRef>()), new WorldDate(0));
        var pins = Markers.Pins(digest, Aged.World.Geography!);
        Assert.Equal(digest.Pins.Count, pins.Count);
        Assert.Equal(pins.Select(p => p.Number).Order(), pins.Select(p => p.Number));
        Assert.All(pins, p => Assert.True(p.Position.Y >= Markers.PinLift));
    }

    [Fact]
    public void FortressesShrinesTemplesAndTowers_HaveTheirOwnMarkers()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var town = map.Settlements.First(s => s.Position is not null);
        var at = town.Position!;
        map = map with
        {
            Settlements = map.Settlements.Select(s => s.Ref == town.Ref ? s with { Marks = new[] { "temple", "mage tower" } } : s with { Marks = null }).ToList(),
            Structures = new[]
            {
                new MapStructure(new EntityRef("structure", 901), "The Walls", "Fortification", "Standing", at),
                new MapStructure(new EntityRef("structure", 902), "The Shrine", "Shrine", "Standing", at, "cleared"),
                new MapStructure(new EntityRef("structure", 903), "The Old Deeps", "Ruin", "Ruined", at, "cleared"),
            },
        };
        var markers = Markers.For(map, Aged.World.Geography!);
        Assert.Equal(MarkerShape.Fortress, markers.Single(m => m.Ref.Id == 901).Shape);
        Assert.Equal((MarkerShape.Shrine, Palette.Gold), (markers.Single(m => m.Ref.Id == 902).Shape, markers.Single(m => m.Ref.Id == 902).Colour));
        Assert.Equal(MarkerShape.Ruin, markers.Single(m => m.Ref.Id == 903).Shape);
        var beside = markers.Where(m => m.Ref == town.Ref && m.Shape != MarkerShape.Settlement).ToList();
        Assert.Equal(new[] { MarkerShape.Temple, MarkerShape.Tower }, beside.Select(m => m.Shape));
        var home = markers.Single(m => m.Ref == town.Ref && m.Shape == MarkerShape.Settlement);
        Assert.All(beside, m => Assert.InRange(System.Numerics.Vector3.Distance(m.Position with { Y = 0 }, home.Position with { Y = 0 }), 0.01f, 0.05f));
    }
}

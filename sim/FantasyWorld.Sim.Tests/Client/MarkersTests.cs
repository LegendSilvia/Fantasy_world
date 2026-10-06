using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class MarkersTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void EverySettlementAndStructure_StandsOnTheBlock_SizedByTier()
    {
        var map = MapScreen.Build(Aged.World, Aged.Log);
        var markers = Markers.For(map, Aged.World.Geography!);
        Assert.Equal(map.Settlements.Count + map.Structures.Count, markers.Count);
        Assert.All(markers, m => Assert.True(m.Position.X is >= -1.1f and <= 1.1f && m.Position.Z is >= -1.1f and <= 1.1f && m.Position.Y >= 0));
        var city = map.Settlements.MaxBy(s => Markers.SizeOf(s.Tier))!;
        Assert.Equal(Markers.SizeOf(city.Tier), markers.Single(m => m.Ref == city.Ref).Size);
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
    public void DigestPins_FloatAboveTheirPlaces_InOrder()
    {
        var digest = DigestScreen.Build(Aged.World, Aged.Log, FantasyWorld.Sim.Chronicle.Digest.Build(Aged.Log, Aged.World, 0, new HashSet<EntityRef>()), new WorldDate(0));
        var pins = Markers.Pins(digest, Aged.World.Geography!);
        Assert.Equal(digest.Pins.Count, pins.Count);
        Assert.Equal(pins.Select(p => p.Number).Order(), pins.Select(p => p.Number));
        Assert.All(pins, p => Assert.True(p.Position.Y >= Markers.PinLift));
    }
}

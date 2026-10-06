using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class MapScreenTests
{
    [Fact]
    public void TheMap_ShowsHoldersOccupiersAndConflicts()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var war = TestRealms.DeclareWar(world, log);
        var siege = log.Append(world.Date, new EventDraft("RegionOccupied", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref, ["war"] = war.Ref },
            Place: world.Settlements[2].Ref, Importance: 30, Tags: new[] { "War" }));
        world.Regions[2].OccupierRealmId = 1;

        var map = MapScreen.Build(world, log);

        var held = map.Settlements.Single(s => s.Ref == world.Settlements[2].Ref);
        Assert.Equal(PinMarker.Conflict, held.Marker);
        Assert.Equal($"Held by the {Statecraft.Title(world.Realms[2])} since the founding, occupied by {world.Realms[1].Name}", held.Card.Title);
        Assert.StartsWith("Pop. ", held.Card.Line);
        Assert.Equal(world.Realms[1].Ref, map.Regions.Single(r => r.Ref == world.Regions[2].Ref).Occupier);
        var conflict = Assert.Single(map.Conflicts);
        Assert.Equal(new[] { world.Regions[2].Ref }, conflict.Occupied);
        Assert.Contains(map.Legend, l => l.Label == world.Realms[1].Name);
    }

    [Fact]
    public void TheMapAtAPastDate_ShowsThePastState()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 30);
        var ceded = sim.Log.All.First(e => e.Type == "RegionCeded");
        var before = MapScreen.Build(world, sim.Log, ceded.Date.AddDays(-1));
        var after = MapScreen.Build(world, sim.Log, ceded.Date);
        Assert.Equal(ceded.Actors["from"], before.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.Equal(ceded.Actors["to"], after.Regions.Single(r => r.Ref == ceded.Place).Owner);
        Assert.DoesNotContain("Pop.", before.Settlements[0].Card.Line);

        var now = MapScreen.Build(world, sim.Log);
        Assert.Equal(world.Date.Year, now.Year);
        Assert.Equal(world.Settlements.Values.Count(s => !s.Abandoned), now.Settlements.Count);
        Assert.All(now.Settlements, s => Assert.True(s.Position!.X is > 0 and < 1));
        Assert.All(now.Borders, b => Assert.NotEqual(now.Regions.Single(r => r.Ref == b.A).Owner, now.Regions.Single(r => r.Ref == b.B).Owner));
        Assert.Matches(@"^[A-Z][a-z]+ realms?, [a-z0-9]+ settlements?, [a-z0-9]+ open labyrinths?\.$", now.Summary);
    }

    [Fact]
    public void AWorldWithoutAFoundingRecord_ShowsTodaysMap_AtAnyDate()
    {
        var world = TestRealms.TwoKingdoms();
        world.Date = new WorldDate(3600);
        var map = MapScreen.Build(world, new EventLog(), new WorldDate(10));
        Assert.False(map.Historical);
        Assert.Equal(world.Date.Year, map.Year);
    }
}

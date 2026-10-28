using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class StructureSystemTests
{
    private static Simulator Make(World world, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(), new ISimSystem[] { new StructureSystem() }, checkInvariants: true);

    [Fact]
    public void WealthyLords_RaiseLandmarks_OverYears()
    {
        var world = TestRealms.TwoKingdoms();
        var seat = world.Settlements[1];
        seat.Tier = SettlementTier.Town;
        seat.Wealth = 1_000_000;
        // Only the structure system runs, so no stocks fill: a wealthy realm has its timber and stone in hand.
        foreach (var realm in world.Realms.Values) realm.Stocks[ResourceKinds.Timber] = realm.Stocks[ResourceKinds.Stone] = 1_000_000;
        var sim = Make(world);

        sim.AdvanceDays(360 * 60);

        var begun = sim.Log.All.First(e => e.Type == "LandmarkBegun");
        var done = sim.Log.All.First(e => e.Type == "LandmarkCompleted");
        Assert.Equal(begun.Id, done.Causes.Single().EventId);
        Assert.True(done.Date.Year - begun.Date.Year >= 6);
        var landmark = world.Structures[done.Actors["structure"].Id];
        Assert.Equal(StructureState.Standing, landmark.State);
        Assert.Equal(seat.LordId, landmark.BuilderId);
        Assert.True(world.Characters[landmark.BuilderId].Renown >= 10 || !world.Characters[landmark.BuilderId].IsAlive);
    }

    [Fact]
    public void Labyrinths_OpenInDangerousLeyLands_AndHideARelic()
    {
        var world = TestPeople.World();
        var wild = world.Regions[2];
        (wild.LeyPower, wild.Danger) = (90, 50);
        var sim = Make(world);

        sim.AdvanceDays(360 * 150);

        var opened = sim.Log.All.Single(e => e.Type == "LabyrinthOpened");
        Assert.Equal(wild.Ref, opened.Place);
        Assert.True(wild.Danger >= 65);
        var relic = world.Artifacts.Values.Single(a => a.OriginEventId == opened.Id);
        Assert.Equal((ArtifactPlace.Lost, wild.Id), (relic.Place, relic.RegionId));
    }

    [Fact]
    public void AbandonedSettlements_LeaveRuins_CitingTheirAbandonment()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var town = world.Settlements[2];
        var lord = world.Characters[town.LordId];
        var begun = log.Append(world.Date, new EventDraft("LandmarkBegun"));
        world.Structures[world.Ids.Take(Structure.Kind)] = new Structure
        {
            Id = 1, Name = "The Spire", Type = StructureKind.Landmark, RegionId = town.RegionId, SettlementId = town.Id,
            BuilderId = lord.Id, StartedEventId = begun.Id, State = StructureState.Building,
        };
        var abandoned = log.Append(world.Date, new EventDraft("SettlementAbandoned", Place: town.Ref));
        town.Abandoned = true;
        town.Children = town.Adults = town.Elders = 0;
        lord.Role = Role.None;
        town.LordId = 0;
        lord.SettlementId = 1;

        var sim = Make(world, log);
        sim.AdvanceDays(360);

        var fell = sim.Log.All.Single(e => e.Type == "StructureRuined");
        Assert.Equal(abandoned.Id, fell.Causes.Single().EventId);
        Assert.Equal(StructureState.Ruined, world.Structures[1].State);
        Assert.Contains(world.Structures.Values, s => s.Type == StructureKind.Ruin && s.Name == $"The Ruins of {town.Name}");
    }

    [Fact]
    public void StructuresAreListed()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, CliApp.Run(new[] { "structures", file.Path }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("kind        state", output.ToString());
    }
}

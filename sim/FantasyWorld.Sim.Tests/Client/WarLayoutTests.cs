using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class WarLayoutTests
{
    /// <summary>A tiny world where realm A's host marches across the border into B's land, which A partly occupies.</summary>
    private static (World World, EventLog Log, Army Army, Region Target) AtWar()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var graph = world.Geography!.Graph;
        var link = graph.Links.First(l => world.Regions[l.A].OwnerRealmId != 0 && world.Regions[l.B].OwnerRealmId != 0
                                          && world.Regions[l.A].OwnerRealmId != world.Regions[l.B].OwnerRealmId);
        var (home, target) = (world.Regions[link.A], world.Regions[link.B]);
        var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
        var log = new EventLog();
        var war = new War { Id = world.Ids.Take(War.Kind), Name = "The Border War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id }, StartDay = 0 };
        world.Wars[war.Id] = war;
        var people = world.Characters.Values.Where(c => c.IsActive).Take(2).ToList();
        var army = new Army
        {
            Id = world.Ids.Take(Army.Kind),
            Name = $"The Host of {a.Name}",
            RealmId = a.Id,
            WarId = war.Id,
            CommanderId = people[0].Id,
            Knights = { people[1].Id },
            Levies = 900,
            MenAtArms = 100,
            RegionId = home.Id,
            Path = { target.Id },
            TargetRegionId = target.Id,
            Objective = ArmyObjective.Siege,
        };
        world.Armies[army.Id] = army;
        target.OccupierRealmId = a.Id;
        log.Append(world.Date, new EventDraft("BattleFought", Actors: new Dictionary<string, EntityRef>
        { ["victor"] = a.Ref, ["vanquished"] = b.Ref, ["war"] = war.Ref }, Place: target.Ref));
        return (world, log, army, target);
    }

    [Fact]
    public void TheMap_ShowsHostsWithTheirRoad_Leaders_AndRecentBattles()
    {
        var (world, log, army, target) = AtWar();
        var map = MapScreen.Build(world, log);
        var host = Assert.Single(map.Armies!);
        Assert.Equal(army.Ref, host.Ref);
        Assert.Equal(2, host.Route.Count);
        Assert.Equal(MapPoint.Of(world, target.Ref), host.Route[^1]);
        Assert.Equal(world.Characters[army.CommanderId].Ref, host.Commander!.Ref);
        Assert.Single(host.Knights);
        Assert.Equal("attacker", host.Side);
        Assert.Equal(1000 + 1, host.Soldiers);
        Assert.Single(map.Battles!);
        Assert.Empty(MapScreen.Build(world, log, new WorldDate(-1)).Armies!);
    }

    [Fact]
    public void Arrows_RunFromTheHostToItsTarget_WideningToAHead()
    {
        var (world, log, _, target) = AtWar();
        var map = MapScreen.Build(world, log);
        var arrow = Assert.Single(WarLayout.Arrows(map, world.Geography!));
        var goal = Projection.ToBlock(world.Geography!.Terrain, MapPoint.Of(world, target.Ref)!, 0);
        Assert.True(Vector2.Distance(new(arrow.Head.X, arrow.Head.Z), new(goal.X, goal.Z)) < 0.01f);
        Assert.All(arrow.Mesh.Positions, p => Assert.True(p.Y > 0));
        Assert.Equal(arrow.Mesh.Positions.Length, arrow.Mesh.Uvs!.Length);
        Assert.Equal(1, arrow.Mesh.Uvs.Max(u => u.Y), 3);
        Assert.All(arrow.Mesh.Indices, i => Assert.InRange(i, 0, arrow.Mesh.Positions.Length - 1));
    }

    [Fact]
    public void TheFront_RunsBetweenTheSides_AndOccupiedLandIsHatched()
    {
        var (world, log, _, target) = AtWar();
        var map = MapScreen.Build(world, log);
        var fronts = WarLayout.Fronts(map, world.Geography!);
        Assert.NotEmpty(fronts);
        Assert.Contains(fronts, f => f.Teeth.Triangles > 0);
        var hatch = WarLayout.Occupation(map, world.Geography!)!;
        var tiles = world.Geography!.RegionOf.Count(r => r == target.Id);
        Assert.Equal(tiles * 2, hatch.Triangles);
        Assert.NotEmpty(WarLayout.OccupationOutline(map, world.Geography!));
    }
}

using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class ContinentTests
{
    private static readonly Lazy<World> Huge = new(() => WorldGenerator.Generate(new WorldGenOptions(2, WorldSize.Huge), DateTimeOffset.UnixEpoch));

    [Fact]
    public void AHugeWorld_HasThreeTimesTheLand_OnTwoToFourContinents()
    {
        var world = Huge.Value;
        Assert.Equal(448, world.Geography!.Terrain.Width);
        Assert.InRange(world.Regions.Count, 290, 310);
        Assert.InRange(world.Continents.Count, 2, 4);
        Assert.All(world.Regions.Values, r => Assert.Contains(r.ContinentId, world.Continents.Keys));
        Assert.All(world.Continents.Values, c => Assert.True(world.Regions.Values.Count(r => r.ContinentId == c.Id) >= 30));
        Assert.Equal(world.Continents.Count, world.Continents.Values.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void ShipsSail_BetweenEveryTwoContinents_FromSeveralPorts()
    {
        var world = Huge.Value;
        var graph = world.Geography!.Graph;
        Assert.Equal(world.Regions.Count, graph.Reachable(world.Regions.Keys.First()).Count);
        var continents = world.Continents.Keys.ToList();
        foreach (var (a, b) in continents.SelectMany((a, i) => continents.Skip(i + 1).Select(b => (a, b))))
        {
            var crossings = graph.Links.Where(l => l.Kind == LinkKind.Sea
                && new[] { world.Regions[l.A].ContinentId, world.Regions[l.B].ContinentId }.Order().SequenceEqual(new[] { a, b }.Order())).ToList();
            Assert.InRange(crossings.Count, 1, RegionGraphBuilder.CrossingsPerShore + 1);
        }
    }

    [Fact]
    public void AWorldOfOneLandmass_IsOneContinent()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.NotEmpty(world.Continents);
        Assert.All(world.Regions.Values, r => Assert.NotEqual(0, r.ContinentId));
    }
}

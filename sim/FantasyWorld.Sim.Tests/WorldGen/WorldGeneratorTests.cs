using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class WorldGeneratorTests
{
    private static World Tiny(ulong seed = 7) =>
        WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);

    [Fact]
    public void TinyWorld_IsCompleteAndValid()
    {
        var world = Tiny();
        Assert.NotNull(world.Geography);
        Assert.InRange(world.Regions.Count, 9, 16);
        Assert.True(world.Settlements.Count >= world.Regions.Count);
        Assert.Equal(world.Regions.Count, world.Geography!.Graph.Reachable(1).Count);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void SameSeed_SameWorld()
    {
        string Json(World w) => JsonSerializer.Serialize(new { w.Regions, w.Settlements, w.Geography });
        Assert.Equal(Json(Tiny(5)), Json(Tiny(5)));
        Assert.NotEqual(Json(Tiny(5)), Json(Tiny(6)));
    }

    [Fact]
    public void RegionAt_MatchesSettlementRegions()
    {
        var world = Tiny();
        Assert.All(world.Settlements.Values, s => Assert.Equal(s.RegionId, world.Geography!.RegionAt(s.Tile)));
    }

    [Fact]
    public void IdGenerator_ContinuesAfterGeneratedEntities()
    {
        var world = Tiny();
        Assert.Equal(world.Regions.Count + 1, world.Ids.Peek(Region.Kind));
        Assert.Equal(world.Settlements.Count + 1, world.Ids.Peek(Settlement.Kind));
    }

    [Fact]
    public void MediumWorld_MatchesTheSpec()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(42), DateTimeOffset.UnixEpoch);
        Assert.Equal(256, world.Geography!.Terrain.Width);
        Assert.InRange(world.Regions.Count, 95, 120);
        Assert.InRange(world.Settlements.Values.Count(s => s.Tier == SettlementTier.City), 6, 8);
    }
}

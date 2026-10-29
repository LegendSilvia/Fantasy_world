using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>The world's own resources (the owner, 2026-10-29): made with the world, named by Claude, found by a secret, and crucial.</summary>
public class WondersTests
{
    private sealed class Namer : IInventor
    {
        public Invention Invent(InventionBrief brief) => new($"Claude's {brief.Fallback}", "Told by Claude.");
    }

    [Fact]
    public void AWorld_HasOneToThreeOfItsOwn_TheSealFirstAndHidden_TheFoundOnesInItsLand()
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var world = WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
            Assert.InRange(world.Wonders.Count, 1, 3);
            Assert.Equal(WorldResource.Seal, world.Wonders[0].Role);
            Assert.True(world.Wonders[0].Hidden);
            Assert.Equal(world.Wonders.Count, world.Wonders.Select(w => w.Role).Distinct().Count());
            Assert.All(world.Wonders, w => Assert.Equal(!w.Hidden, world.Regions[w.RegionId].Resources.ContainsKey(w.Kind)));
        }
    }

    [Fact]
    public void Claude_NamesThem_BeforeHistory_AndASecret_BringsTheHiddenToLight()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Wonders.Invent(world, new Namer());
        Assert.All(world.Wonders, w => Assert.StartsWith("Claude's ", w.Name));
        Assert.All(world.Wonders, w => Assert.Equal("Told by Claude.", w.What));
        Assert.All(world.Wonders.Where(w => !w.Hidden), w => Assert.True(world.Regions[w.RegionId].Resources.ContainsKey(w.Kind)));

        var log = new EventLog();
        var ctx = new SimContext(world, log, new EventQueue(), new Rng(1));
        var finder = world.Characters.Values.First(c => c.IsActive);
        var secret = log.Append(world.Date, new EventDraft("SecretRevealed", Place: world.Regions.Values.First().Ref));
        var found = Wonders.Discover(ctx, finder, secret.Id);
        Assert.NotNull(found);
        Assert.Equal("WonderFound", found!.Type);
        Assert.False(world.Wonders[0].Hidden);
        Assert.Equal(finder.Id, world.Wonders[0].FinderId);
        Assert.True(world.Regions[world.Wonders[0].RegionId].Resources.ContainsKey(world.Wonders[0].Kind));
    }

    [Fact]
    public void AForgeResource_InAStore_ArmsTheRealm()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        world.Wonders.Add(new WorldResource("emberiron", "Emberiron", "an iron", WorldResource.Forge, world.Regions.Keys.First(), Hidden: false));
        var realm = world.Realms.Values.First();
        var before = Stocks.Arms(world, realm);
        realm.Stocks["emberiron"] = 10;
        Assert.Equal(before * Wonders.ForgeArms, Stocks.Arms(world, realm), 6);
    }
}

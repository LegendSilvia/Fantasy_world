using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Engine;

/// <summary>Whole-world checks of the character systems: a lasting cast, kept seats, bounded artifacts, feuds with causes.</summary>
public class PeopleHealthTests
{
    private static Simulator NewSim(ulong seed, WorldSize size, bool checkInvariants) =>
        new(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch),
            new EventLog(), new EventQueue(), DefaultSystems.Create(), checkInvariants);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoCenturies_KeepALivingCast(ulong seed)
    {
        var sim = NewSim(seed, WorldSize.Tiny, checkInvariants: true);
        var world = sim.World;
        var startingArtifacts = world.Artifacts.Count;

        sim.AdvanceDays(200 * 360);

        var target = LifeSystem.NotablesPerSettlement * world.Settlements.Values.Count(s => !s.Abandoned);
        Assert.InRange(world.ActiveCharacters.Count(), target * 0.3, target * 1.5);
        Assert.All(world.Settlements.Values.Where(s => s.Tier >= SettlementTier.Town && !s.Abandoned),
            s => Assert.NotEqual(0, s.LordId));
        Assert.True(world.Characters.Values.Count(c => !c.IsAlive) > 100, "two centuries should see many deaths");
        Assert.InRange(world.Artifacts.Count, startingArtifacts, DeedsSystem.ArtifactCap(world) + startingArtifacts);
        foreach (var type in new[] { "CharacterBorn", "MarriageWed", "ComingOfAge", "SuccessionOccurred", "DuelFought" })
            Assert.Contains(sim.Log.All, e => e.Type == type);
    }

    [Fact]
    public void MediumWorld_GrowsFeuds_WithTraceableCauses()
    {
        var sim = NewSim(42, WorldSize.Medium, checkInvariants: false);
        sim.AdvanceDays(80 * 360);

        var duelDeaths = sim.Log.All.Where(e => e.Type == "CharacterDied" && e.Payload["cause"] == "a duel").ToList();
        Assert.NotEmpty(duelDeaths);
        Assert.Contains(sim.Log.All, e => e.Type == "DuelFought" && e.Payload["stakes"] == "to the death");

        var query = new HistoryQuery(sim.Log);
        Assert.Contains(duelDeaths, d => query.WhyChain(d.Id).Causes.Any(duel => duel.Causes.Count > 0));
        Assert.Contains(sim.Log.All, e => e.Type is "ArtifactFound" or "ArtifactInherited" or "ArtifactStolen");
        Assert.InRange(sim.World.ActiveCharacters.Count(), 300, 1200);
    }
}

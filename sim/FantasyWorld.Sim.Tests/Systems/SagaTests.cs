using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Systems;

public class SagaTests
{
    private sealed class Choose(string key) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) => brief.Options.FirstOrDefault(o => o.Key == key) ?? brief.Options[0];
    }

    private sealed class Fixed(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.FirstOrDefault(o => o.Key == key) ?? encounter.Outcomes[0];
    }

    /// <summary>A medium world in its fortieth year, and a saga system that has made its demon lord rise.</summary>
    private static (SimContext Ctx, SagaSystem System, Saga Saga) Risen(string fight = "attacker_kills", string choice = "wait")
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5), DateTimeOffset.UnixEpoch);
        world.Date = new WorldDate(40L * WorldDate.DaysPerYear);
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(9));
        var system = new SagaSystem(new Choose(choice), new Fixed(fight));
        for (var year = 0; year < 400 && SagaSystem.Active(world) is null; year++) system.OnYearlyTick(ctx);
        return (ctx, system, SagaSystem.Active(world)!);
    }

    private static void Years(SimContext ctx, SagaSystem system, int years)
    {
        for (var k = 0; k < years && SagaSystem.Active(ctx.World) is not null; k++)
        {
            ctx.World.Date = ctx.World.Date.AddDays(WorldDate.DaysPerYear);
            system.OnYearlyTick(ctx);
        }
    }

    [Fact]
    public void ADemonLord_RisesInTheDarkestLand_WithItsDominion_AProphecy_AndADreadWeapon()
    {
        var dark = SagaSystem.Darkest(WorldGenerator.Generate(new WorldGenOptions(5), DateTimeOffset.UnixEpoch))!;
        var (ctx, _, saga) = Risen();
        var world = ctx.World;
        var demon = world.Characters[saga.DemonLordId];
        var dominion = world.Realms[saga.RealmId];
        Assert.Equal(Government.Dominion, dominion.Government);
        Assert.Equal(demon.Id, Realms.RulerOf(world, dominion)!.Id);
        Assert.Equal(dark.Id, world.Settlements[dominion.CapitalId].RegionId);
        Assert.StartsWith("Dominion of ", Statecraft.Title(dominion));
        Assert.True(SagaSystem.IsDemonLord(world, demon));
        Assert.Contains(world.Artifacts.Values, a => a.Place == ArtifactPlace.Held && a.HolderId == demon.Id);
        Assert.Equal("ProphecySpoken", ctx.Log.Get(saga.ProphecyEventId).Type);
        Assert.Equal(Character.AdultAge + 4, LifeSystem.Aged(Character.AdultAge + 400, 100));
    }

    [Fact]
    public void TheHeroForetold_GrowsReady_AndSlaysTheDemonLord_BreakingItsDominion()
    {
        var (ctx, system, saga) = Risen();
        Years(ctx, system, 1);
        var hero = ctx.World.Characters[saga.HeroId];
        Assert.Equal((Ambitions.Vengeance, saga.DemonLordId), (hero.Ambition, hero.AmbitionTarget));
        Assert.Contains(ctx.Log.All, e => e.Type == "HeroChosen" && e.Actors["hero"] == hero.Ref);
        hero.Renown = 100;
        Years(ctx, system, 200);

        var slain = ctx.Log.All.Single(e => e.Type == "DemonLordSlain");
        var chosen = ctx.Log.All.Last(e => e.Type == "HeroChosen" && e.Actors["hero"] == slain.Actors["hero"]);
        Assert.True(slain.Date.Year - chosen.Date.Year >= SagaSystem.ReadyYears);
        Assert.Equal((Saga.Slain, false), (saga.Outcome, saga.Active));
        Assert.False(ctx.World.Characters[saga.DemonLordId].IsAlive);
        Assert.True(ctx.World.Realms[saga.RealmId].Destroyed);
    }

    [Fact]
    public void ASage_MaySealTheDemonLordAway_AtTheCostOfTheirLife()
    {
        var (ctx, system, saga) = Risen(fight: "defender_kills", choice: "seal");
        var sage = ctx.World.ActiveCharacters.First(c => c.Role is not Role.Lord);
        sage.Role = Role.Mage;
        sage.Attributes.Lore = 20;
        Years(ctx, system, 40);

        // Each hero is beaten back (the arbiter says so), so after fifteen years the sage gives their life.
        var sealedEvent = ctx.Log.All.Single(e => e.Type == "DemonLordSealed");
        Assert.Contains(ctx.Log.All, e => e.Type == "HeroFell");
        Assert.Equal(Saga.Sealed, saga.Outcome);
        var sealer = ctx.World.Characters[sealedEvent.Actors["sealer"].Id];
        Assert.Equal("the sealing", sealer.DeathCause);
        Assert.True(ctx.World.Characters[saga.DemonLordId].Faded);
        Assert.True(sealedEvent.Date.Year - ctx.Log.Get(saga.RoseEventId).Date.Year >= 15 || Realms.Territory(ctx.World, ctx.World.Realms[saga.RealmId]).Count() <= 1);
    }
}

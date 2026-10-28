using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Systems;

public class RevolutionTests
{
    private sealed class Choose(string key) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) => brief.Options.Single(o => o.Key == key);
    }

    /// <summary>A realm of two lands, its stability low, the town beyond its capital's land in revolt with a rebel in it.</summary>
    private static (SimContext Ctx, Realm Crown, Character Ruler, Character Rebel, Settlement Town) Unsettled()
    {
        var world = TestPeople.World();
        var ruler = TestPeople.Add(world, 1, 40, role: Role.Lord);
        var crown = TestRealms.AddRealm(world, 1, "Westmark");
        world.Regions[2].OwnerRealmId = crown.Id;
        foreach (var r in world.Regions.Values) world.FoundingOwners[r.Id] = r.OwnerRealmId;
        var town = world.Settlements[2];
        town.Tier = SettlementTier.Town;
        (crown.Stability, crown.Treasury) = (20, 1000);
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        town.Conditions[Conditions.Revolt] = ctx.Log.Append(world.Date, new EventDraft("RevoltBegan", Place: town.Ref)).Id;
        var rebel = TestPeople.Add(world, 2, 30, role: Role.Rebel);
        rebel.Attributes.Martial = 15;
        return (ctx, crown, ruler, rebel, town);
    }

    private static War Rise(SimContext ctx, Realm crown)
    {
        var system = new RevolutionSystem();
        for (var year = 0; year < 100 && !crown.Conditions.ContainsKey(RevolutionSystem.Condition); year++) system.OnYearlyTick(ctx);
        return ctx.World.Wars.Values.Single(w => w.CasusBelli == CasusBelli.Revolution);
    }

    [Fact]
    public void ASettledRealm_OrARevoltInTheCapitalsLand_RaisesNoRevolution()
    {
        var (ctx, crown, _, _, town) = Unsettled();
        crown.Stability = 60;
        Assert.Empty(RevolutionSystem.Risen(ctx.World, crown));
        crown.Stability = 20;
        Assert.Equal([town], RevolutionSystem.Risen(ctx.World, crown));
        crown.CapitalId = town.Id;
        Assert.Empty(RevolutionSystem.Risen(ctx.World, crown));
    }

    [Fact]
    public void AnUnsettledRealm_WithATownInRevolt_RisesUnderALeader_WhoDeclaresACivilWar()
    {
        var (ctx, crown, ruler, rebel, town) = Unsettled();
        var war = Rise(ctx, crown);

        var rebels = ctx.World.Realms[war.AttackerId];
        Assert.Equal(crown.Id, war.DefenderId);
        Assert.Equal(rebels.Id, ctx.World.Regions[2].OwnerRealmId);
        Assert.Equal(crown.Id, ctx.World.Regions[1].OwnerRealmId);
        Assert.Equal(town.Id, rebels.CapitalId);
        Assert.Equal(rebel.Id, Realms.RulerOf(ctx.World, rebels)?.Id);
        Assert.Equal(Role.Lord, rebel.Role);
        Assert.True(rebels.Treasury > 0);
        Assert.NotEqual(crown.Colors, rebels.Colors);

        var began = ctx.Log.All.Single(e => e.Type == "RevolutionBegan");
        Assert.Equal(began.Id, crown.Conditions[RevolutionSystem.Condition]);
        Assert.Equal(rebel.Ref, began.Actors["leader"]);
        Assert.Equal(ruler.Ref, began.Actors["ruler"]);
        Assert.Contains(began.Causes, c => c.EventId == town.Conditions[Conditions.Revolt]);
        var declared = ctx.Log.Get(war.DeclaredEventId);
        Assert.Equal(CasusBelli.Revolution, declared.Payload["casus belli"]);
        Assert.Contains(declared.Causes, c => c.EventId == began.Id);
    }

    [Fact]
    public void ACrushedRevolution_ReturnsTheLand_AndTheLeaderIsExecuted()
    {
        var (ctx, crown, _, rebel, _) = Unsettled();
        var war = Rise(ctx, crown);
        var rebels = ctx.World.Realms[war.AttackerId];

        MilitarySystem.MakePeace(ctx, war, attackerWon: false, white: false);

        Assert.True(rebels.Destroyed);
        Assert.Equal(crown.Id, ctx.World.Regions[2].OwnerRealmId);
        var ended = ctx.Log.All.Single(e => e.Type == "RevolutionEnded");
        Assert.Equal(RevolutionSystem.Crushed, ended.Payload["outcome"]);
        Assert.False(crown.Conditions.ContainsKey(RevolutionSystem.Condition));
        // Both towns are the crown's: there is nowhere to flee.
        Assert.False(rebel.IsAlive);
        Assert.Equal("executed", rebel.DeathCause);
        Assert.Contains("revolution crushed", ctx.Log.Get(war.EndedEventId).Payload["terms"]);
    }

    [Fact]
    public void VictoriousRebels_WhoseLeaderRemakesTheRealm_TakeItsThrone_AndChangeItsGovernment()
    {
        var (ctx, crown, ruler, rebel, _) = Unsettled();
        crown.Government = Government.FeudalMonarchy;
        rebel.Attributes.Stewardship = 20;
        var war = Rise(ctx, crown);
        var rebels = ctx.World.Realms[war.AttackerId];

        MilitarySystem.MakePeace(ctx, war, attackerWon: true, white: false, new Choose("remake"));

        Assert.Equal(rebel.Id, Realms.RulerOf(ctx.World, crown)?.Id);
        Assert.Equal(Government.MerchantRepublic, crown.Government);
        Assert.Equal(SuccessionLaw.Elective, crown.SuccessionLaw);
        Assert.True(rebels.Destroyed);
        Assert.Equal(crown.Id, ctx.World.Regions[2].OwnerRealmId);
        Assert.False(ruler.IsAlive);
        var ended = ctx.Log.All.Single(e => e.Type == "RevolutionEnded");
        Assert.Equal(RevolutionSystem.Overthrown, ended.Payload["outcome"]);
        Assert.Equal("merchant republic", ended.Payload["government"]);
    }

    [Fact]
    public void AStalemate_LeavesTheRebelsFree()
    {
        var (ctx, crown, _, rebel, _) = Unsettled();
        var war = Rise(ctx, crown);
        var rebels = ctx.World.Realms[war.AttackerId];

        MilitarySystem.MakePeace(ctx, war, attackerWon: false, white: true);

        Assert.False(rebels.Destroyed);
        Assert.Equal(0, rebels.LiegeId);
        Assert.Equal(rebels.Id, ctx.World.Regions[2].OwnerRealmId);
        Assert.Equal(rebel.Id, Realms.RulerOf(ctx.World, rebels)?.Id);
        Assert.Equal(RevolutionSystem.BrokeAway, ctx.Log.All.Single(e => e.Type == "RevolutionEnded").Payload["outcome"]);
        Assert.False(crown.Conditions.ContainsKey(RevolutionSystem.Condition));
    }

    [Fact]
    public void History_ShowsTheRebelsLand_WhileTheRevolutionLasts()
    {
        var (ctx, crown, _, _, _) = Unsettled();
        var world = ctx.World;
        world.Date = world.Date.AddDays(10);
        var war = Rise(ctx, crown);
        var during = world.Date.AddDays(5);
        world.Date = world.Date.AddDays(10);
        MilitarySystem.MakePeace(ctx, war, attackerWon: false, white: false);
        world.Date = world.Date.AddDays(10);

        Assert.Equal(war.AttackerId, History.PoliticalMapAt(world, ctx.Log, during).Regions[2].OwnerRealmId);
        var after = History.PoliticalMapAt(world, ctx.Log, world.Date.AddDays(-5));
        Assert.Equal(crown.Id, after.Regions[2].OwnerRealmId);
        Assert.Contains(war.AttackerId, after.Fallen);
    }

    [Fact]
    public void TheRevolution_ReadsAsHeadlineNews()
    {
        var (ctx, crown, ruler, rebel, _) = Unsettled();
        var war = Rise(ctx, crown);
        MilitarySystem.MakePeace(ctx, war, attackerWon: true, white: false, new Choose("seize"));
        var narrator = new TemplateNarrator(ctx.World);

        var began = ctx.Log.All.Single(e => e.Type == "RevolutionBegan");
        Assert.True(TemplateNarrator.IsHeadline("RevolutionBegan"));
        Assert.Equal($"Revolution in the {Statecraft.Title(crown)}", narrator.Title(began));
        Assert.Contains($"{rebel.Name}", narrator.Sentence(began));
        Assert.Contains("in revolution against", narrator.Sentence(began));
        var ended = ctx.Log.All.Single(e => e.Type == "RevolutionEnded");
        Assert.Contains($"overthrew {ruler.Name}", narrator.Sentence(ended));
        Assert.Contains("took the throne of", narrator.Sentence(ended));
        Assert.Contains("crown seized", ctx.Log.Get(war.EndedEventId).Payload["terms"]);
    }
}

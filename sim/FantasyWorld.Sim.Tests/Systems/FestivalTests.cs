using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class FestivalTests
{
    private sealed class Claude : IInventor
    {
        public Invention Invent(InventionBrief brief) => new($"The Rite of {brief.Subject}", "Lore of the rite.");
    }

    [Fact]
    public void EveryNation_FoundsAFestivalOfItsOwn_AndKeepsItEachYearInItsMonth()
    {
        var world = TestRealms.TwoKingdoms();
        var sim = new Simulator(world, new EventLog(), new EventQueue(), new ISimSystem[] { new FestivalSystem() }, checkInvariants: true);
        sim.AdvanceDays(WorldDate.DaysPerYear * 2);

        var founded = sim.Log.All.Where(e => e.Type == "FestivalFounded").ToList();
        Assert.Equal(2, founded.Count);
        Assert.All(world.Realms.Values, r => Assert.NotNull(r.Festival));
        Assert.NotEqual(world.Realms[1].Festival!.Name, world.Realms[2].Festival!.Name);
        var held = sim.Log.All.Where(e => e.Type == "FestivalHeld" && e.Actors["realm"] == world.Realms[1].Ref).ToList();
        Assert.Equal(2, held.Count);
        Assert.All(held, e => Assert.Equal(world.Realms[1].Festival!.Month, e.Date.Month));
        Assert.Equal(world.Realms[1].Festival!.Name, held[0].Payload["festival"]);
    }

    [Fact]
    public void ClaudeNamesAFestival_KeepingTheRulesMonthAndRite()
    {
        var world = TestRealms.TwoKingdoms();
        var rules = FestivalSystem.Make(world, world.Realms[1], new Rng(3));
        var claude = FestivalSystem.Invent(world, world.Realms[1], new Rng(3), new Claude(), 5);
        Assert.Equal(($"The Rite of {world.Realms[1].Ref}@5", "Lore of the rite."), (claude.Name, claude.Lore));
        Assert.Equal((rules.Month, rules.Rite, rules.Honours), (claude.Month, claude.Rite, claude.Honours));
        Assert.Equal(rules, FestivalSystem.Invent(world, world.Realms[1], new Rng(3), new RulesInventor(), 5));
    }
}

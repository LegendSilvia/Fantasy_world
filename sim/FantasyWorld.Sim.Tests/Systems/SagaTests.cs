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
        // A world without its own seal (the owner, 2026-10-29: with one, it must be found first).
        ctx.World.Wonders.Clear();
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

    [Fact]
    public void ASealedDemonLord_NeverDiesOfAge_AndIsCalledDemonLord_WithItsFate()
    {
        var (ctx, system, saga) = Risen(fight: "defender_kills", choice: "seal");
        var sage = ctx.World.ActiveCharacters.First(c => c.Role is not Role.Lord);
        sage.Role = Role.Mage;
        sage.Attributes.Lore = 20;
        ctx.World.Wonders.Clear();
        Years(ctx, system, 40);
        Assert.Equal(Saga.Sealed, saga.Outcome);

        // The owner, 2026-10-30: "demon lord die of old age".
        var demon = ctx.World.Characters[saga.DemonLordId];
        var life = new LifeSystem();
        for (var year = 0; year < 500; year++)
        {
            ctx.World.Date = ctx.World.Date.AddDays(WorldDate.DaysPerYear);
            life.OnYearlyTick(ctx);
        }
        Assert.True(demon.IsAlive);
        Assert.True(SagaSystem.Undying(ctx.World, demon));
        Assert.StartsWith("Demon Lord (sealed)", Sim.Views.CharacterScreen.Build(ctx.World, ctx.Log, demon.Id).Kicker);

        // The world events tab tells the saga from its rise to its sealing.
        var events = Sim.Views.WorldEventsScreen.Build(ctx.World, ctx.Log).Sagas.Single();
        Assert.False(events.Active);
        Assert.Contains("sealed", events.State);
        Assert.Equal(("DemonLordRose", "SagaEnded"), (ctx.Log.Get(events.Events[0].EventId).Type, ctx.Log.Get(events.Events[^1].EventId).Type));
        Assert.Contains(events.Events, r => ctx.Log.Get(r.EventId).Type == "DemonLordSealed");
    }

    [Fact]
    public void AnEpisode_TellsTheWorldEvents_InASectionOfTheirOwn_AndHowTheStruggleStands()
    {
        var (ctx, system, saga) = Risen();
        var risen = Sim.Chronicle.Episode.Build(ctx.World, ctx.Log, 0, "2026-10-30");
        var world = risen.Parts.Single(p => p.Kind == Sim.Chronicle.EpisodeSection.World);
        Assert.Contains(ctx.World.Characters[saga.DemonLordId].Name, world.Text);
        Assert.DoesNotContain(risen.Parts, p => p.Kind == Sim.Chronicle.EpisodeSection.Story && Sim.Chronicle.EpisodeSection.WorldEvents.Contains(ctx.Log.Get(p.EventId).Type));

        // A year with no world event still says how it stands while the demon lord is abroad.
        var last = ctx.Log.All[^1].Id;
        ctx.Log.Append(ctx.World.Date, new EventDraft("FestivalHeld", Importance: 1, Tags: ["Culture"]));
        var quiet = Sim.Chronicle.Episode.Build(ctx.World, ctx.Log, last, "2026-10-31");
        Assert.Contains("The shadow still lies over the world", quiet.Parts.Single(p => p.Kind == Sim.Chronicle.EpisodeSection.World).Text);
    }

    [Fact]
    public void AWorldsOwnSeal_MustBeFound_BeforeAnySageCanSealTheDemonLord()
    {
        var (ctx, system, saga) = Risen(fight: "defender_kills", choice: "seal");
        var sage = ctx.World.ActiveCharacters.First(c => c.Role is not Role.Lord);
        sage.Role = Role.Mage;
        sage.Attributes.Lore = 20;
        ctx.World.Wonders.Clear();
        ctx.World.Wonders.Add(new WorldResource("starsilver", "Starsilver", "a pale metal", WorldResource.Seal, ctx.World.Regions.Keys.First()));
        Years(ctx, system, 40);
        Assert.DoesNotContain(ctx.Log.All, e => e.Type == "DemonLordSealed");

        // Found, it seals the demon lord, and the sealing cites its finding.
        var found = ctx.Log.Append(ctx.World.Date, new EventDraft("WonderFound"));
        ctx.World.Wonders[0] = ctx.World.Wonders[0] with { Hidden = false, FoundEventId = found.Id };
        Years(ctx, system, 10);
        var sealedEvent = ctx.Log.All.Single(e => e.Type == "DemonLordSealed");
        Assert.Equal("Starsilver", sealedEvent.Payload["with"]);
        Assert.Contains(sealedEvent.Causes, c => c.EventId == found.Id);
    }

    /// <summary>A typical fighter of the living lands at a level: no relic, no companion, beyond the Dominion.</summary>
    private static Character Fighter(World world, Saga saga, int level, double renown)
    {
        var dark = Realms.Territory(world, world.Realms[saga.RealmId]).Select(r => r.Id).ToHashSet();
        var home = world.Settlements.Values.Where(s => s.Population > 0 && !dark.Contains(s.RegionId)).OrderBy(s => s.Id).First();
        var c = Support.TestPeople.Add(world, home.Id, 30, role: Role.Adventurer);
        (c.Attributes.Prowess, c.Attributes.Martial, c.Renown, c.Level, c.Class) = (15, 12, renown, level, Classes.Warrior);
        return c;
    }

    private static double HeroWins(Encounter fight) => fight.Outcomes.Where(o => o.AttackerWins).Sum(o => o.Probability);

    [Fact]
    public void TheDemonLord_StandsAtLevelTen_AndAFullPartyOfFive_WinsThreeTimesInFour_ALoneHeroOfSeven_OnceInFour()
    {
        var (ctx, _, saga) = Risen();
        var world = ctx.World;
        var demon = world.Characters[saga.DemonLordId];
        Assert.Equal(Levels.Max, Levels.Of(demon));
        // The owner, 2026-10-30: a full party at level five beats the demon lord three times in four; a hero alone at seven, once in four.
        var hero = Fighter(world, saga, 5, 90);
        var party = Enumerable.Range(0, Saga.PartySize).Select(_ => Fighter(world, saga, 5, 30)).ToList();
        var withParty = HeroWins(Encounters.DuelBetween(world, hero, demon, toTheDeath: true, party: party));
        hero.Level = 7;
        var alone = HeroWins(Encounters.DuelBetween(world, hero, demon, toTheDeath: true, party: []));
        Assert.True(withParty >= 0.75, $"a full party of level five wins {withParty:0.00} (alone {alone:0.00})");
        Assert.True(alone is >= 0.2 and <= 0.3, $"a hero of seven alone wins {alone:0.00}");
        // A party makes the hero stronger than any number alone would, and party members are among those whose fates the fight decides.
        Assert.Equal(Saga.PartySize, Encounters.DuelBetween(world, hero, demon, true, party: party).Fellows.Count(f => f.Party));
    }

    [Fact]
    public void TheHero_GathersAParty_AndItsMembersMayFallEvenInVictory()
    {
        var (ctx, system, saga) = Risen();
        // (The saga runs alone here, without the yearly levelling: the living lands' fighters are given levels to stand with the hero.)
        foreach (var c in ctx.World.ActiveCharacters) c.Level = Math.Max(c.Level, 4);
        Years(ctx, system, 40);
        var log = ctx.Log;
        var joined = log.All.Where(e => e.Type == "HeroPartyJoined").ToList();
        Assert.True(joined.Count > 0, string.Join(", ", log.All.Where(e => e.Importance >= 40).Select(e => $"{e.Date.Year}:{e.Type}")));
        Assert.All(joined, e => Assert.True(Levels.Of(ctx.World.Characters[e.Actors["member"].Id]) >= SagaSystem.PartyLevel));
        Assert.All(ctx.World.Sagas.Values, s => Assert.True(s.PartyIds.Count <= Saga.PartySize));
        Assert.All(log.All.Where(e => e.Type == "FellowFate"), e =>
            Assert.Contains(e.Payload["fate"], new[] { "injured", "incapacitated", "disabled", "separated", "dead" }));
    }
}

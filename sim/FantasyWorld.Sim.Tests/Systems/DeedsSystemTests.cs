using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class DeedsSystemTests
{
    /// <summary>Takes the first option whose key starts with the wanted verb, else rests.</summary>
    private sealed class WantDecider(string verb) : IDecider
    {
        public DecisionOption Decide(DecisionBrief brief, Rng rng) =>
            brief.Options.FirstOrDefault(o => o.Key.StartsWith(verb, StringComparison.Ordinal)) ?? brief.Options[0];
    }

    /// <summary>Always returns the outcome with the given key.</summary>
    private sealed class FixedArbiter(string key) : IArbiter
    {
        public EncounterOutcome Resolve(Encounter encounter, Rng rng) => encounter.Outcomes.Single(o => o.Key == key);
    }

    private static Simulator Make(World world, string verb, string? outcome = null, EventLog? log = null) =>
        new(world, log ?? new EventLog(), new EventQueue(),
            new ISimSystem[] { new DeedsSystem(new WantDecider(verb), outcome is null ? null : new FixedArbiter(outcome)) },
            checkInvariants: true);

    [Fact]
    public void Hunter_SlaysMonsters_AndTamesTheRegion()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 80;
        var hunter = TestPeople.Add(world, 1, 25, role: Role.Knight);
        hunter.Ambition = Ambitions.Renown;
        var sim = Make(world, "hunt", "success");

        sim.AdvanceDays(360 * 3);

        var slain = sim.Log.All.First(e => e.Type == "MonstersSlain");
        Assert.Equal(world.Regions[2].Ref, slain.Place);
        Assert.True(world.Regions[2].Danger < 80);
        Assert.True(hunter.Renown > 0);
    }

    [Fact]
    public void FailedHunt_CanKill_AndTheDeathCitesTheHunt()
    {
        var world = TestPeople.World();
        world.Regions[2].Danger = 80;
        var hunter = TestPeople.Add(world, 1, 25);
        hunter.Ambition = Ambitions.Renown;
        var sim = Make(world, "hunt", "failure_death");

        sim.AdvanceDays(360 * 5);

        var died = Assert.Single(sim.Log.All, e => e.Type == "CharacterDied");
        Assert.Equal("monsters", died.Payload["cause"]);
        Assert.Equal("MonsterHuntFailed", sim.Log.Get(died.Causes[0].EventId).Type);
    }

    [Fact]
    public void Delver_FindsTheLostArtifact()
    {
        var world = TestPeople.World();
        var seeker = TestPeople.Add(world, 1, 25);
        seeker.Ambition = Ambitions.Artifact;
        var relic = TestPeople.GiveArtifact(world, seeker);
        (relic.Place, relic.HolderId, relic.RegionId) = (ArtifactPlace.Lost, 0, 2);
        var sim = Make(world, "delve", "success");

        sim.AdvanceDays(360 * 3);

        Assert.Equal(ArtifactPlace.Held, relic.Place);
        Assert.Equal(seeker.Id, relic.HolderId);
        Assert.Equal("found", relic.Provenance.Single().How);
    }

    [Fact]
    public void Avenger_KillsTheKinslayer_InADuelCitingTheGrudge()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var murder = log.Append(world.Date, new EventDraft("CharacterDied"));
        var avenger = TestPeople.Add(world, 1, 25, traits: new[] { Traits.Brave });
        var foe = TestPeople.Add(world, 2, 40);
        avenger.Ambition = Ambitions.Vengeance;
        avenger.AmbitionTarget = foe.Id;
        avenger.Memories.Add(new Memory(murder.Id, foe.Id, -80, 80));
        var sim = Make(world, "avenge", "attacker_kills", log);

        sim.AdvanceDays(360 * 3);

        var duel = sim.Log.All.Single(e => e.Type == "DuelFought");
        Assert.Equal(murder.Id, duel.Causes.Single().EventId);
        Assert.False(foe.IsAlive);
        Assert.NotEqual(Ambitions.Vengeance, avenger.Ambition);
        Assert.Contains(new HistoryQuery(sim.Log).RootCauses(sim.Log.All.Single(e => e.Type == "CharacterDied" && e.Id != murder.Id).Id),
            e => e.Id == murder.Id);
    }

    [Fact]
    public void Vengeance_IsDropped_WhenTheFoeIsGone()
    {
        var world = TestPeople.World();
        var avenger = TestPeople.Add(world, 1, 25, role: Role.Knight);
        var foe = TestPeople.Add(world, 2, 40);
        avenger.Ambition = Ambitions.Vengeance;
        avenger.AmbitionTarget = foe.Id;
        foe.Died = world.Date;
        Make(world, "rest").AdvanceDays(360 * 5);
        Assert.Equal(Ambitions.Renown, avenger.Ambition);
        Assert.Equal(0, avenger.AmbitionTarget);
    }

    [Fact]
    public void Rivals_FightToFirstBlood()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var insult = log.Append(world.Date, new EventDraft("RivalryBegan"));
        var hothead = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Wrathful });
        var other = TestPeople.Add(world, 1, 30);
        hothead.Memories.Add(new Memory(insult.Id, other.Id, -90, 90));
        var sim = Make(world, "duel", "attacker_first_blood", log);

        sim.AdvanceDays(360 * 3);

        var duel = sim.Log.All.First(e => e.Type == "DuelFought");
        Assert.Equal("first blood", duel.Payload["stakes"]);
        Assert.True(hothead.IsAlive && other.IsAlive);
    }

    [Fact]
    public void WrathfulLosers_SwearVengeance()
    {
        var sworn = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var world = TestPeople.World(seed);
            var log = new EventLog();
            var insult = log.Append(world.Date, new EventDraft("RivalryBegan"));
            var bully = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Brave });
            var victim = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Wrathful });
            bully.Memories.Add(new Memory(insult.Id, victim.Id, -90, 90));
            Make(world, "duel", "attacker_first_blood", log).AdvanceDays(360 * 3);
            if (victim.Ambition == Ambitions.Vengeance && victim.AmbitionTarget == bully.Id) sworn++;
        }
        Assert.InRange(sworn, 5, 20);
    }

    [Fact]
    public void Thief_TakesTheArtifact()
    {
        var world = TestPeople.World();
        var thief = TestPeople.Add(world, 1, 30, traits: new[] { Traits.Deceitful });
        thief.Ambition = Ambitions.Artifact;
        thief.Attributes.Intrigue = 20;
        var victim = TestPeople.Add(world, 1, 30);
        victim.Attributes.Intrigue = 0;
        var crown = TestPeople.GiveArtifact(world, victim);
        var sim = Make(world, "steal");

        sim.AdvanceDays(360 * 6);

        Assert.Contains(sim.Log.All, e => e.Type is "ArtifactStolen" or "TheftFoiled");
        Assert.True(Relations.Opinion(victim, thief) < 0);
        if (crown.HolderId == thief.Id) Assert.Equal("stolen", crown.Provenance.Last().How);
    }

    [Fact]
    public void Mage_ForgesAnArtifact_WhoseOriginIsTheForging()
    {
        var world = TestPeople.World();
        world.Regions[1].LeyPower = 80;
        var mage = TestPeople.Add(world, 1, 40, role: Role.Mage);
        mage.Ambition = Ambitions.Magic;
        mage.Attributes.Lore = 20;
        var sim = Make(world, "forge");

        sim.AdvanceDays(360 * 6);

        var forged = sim.Log.All.First(e => e.Type == "ArtifactForged");
        var artifact = world.Artifacts[forged.Actors["artifact"].Id];
        Assert.Equal(forged.Id, artifact.OriginEventId);
        Assert.Equal(mage.Id, artifact.Provenance[0].To);
    }

    [Fact]
    public void Options_AlwaysIncludeRest_AndFitTheAmbition()
    {
        var world = TestPeople.World();
        var scholar = TestPeople.Add(world, 1, 30);
        scholar.Ambition = Ambitions.Magic;
        var ctx = new SimContext(world, new EventLog(), new EventQueue(), new Rng(1));
        var keys = DeedsSystem.Options(ctx, scholar).Select(o => o.Key).ToList();
        Assert.Contains("rest", keys);
        Assert.Contains("study", keys);
        Assert.DoesNotContain("forge", keys);
    }
}

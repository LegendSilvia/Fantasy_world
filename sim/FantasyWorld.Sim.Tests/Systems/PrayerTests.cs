using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Systems;

public class PrayerTests
{
    private static SimContext Context(World world) => new(world, new EventLog(), new EventQueue(), new Rng(4));

    /// <summary>Two kingdoms, realm 1's ruler wounded and renowned: their need becomes a prayer.</summary>
    private static (SimContext Ctx, Character Ruler) Wounded()
    {
        var world = TestRealms.TwoKingdoms();
        var ruler = Realms.RulerOf(world, world.Realms[1])!;
        ruler.Wounds.Add("a scar across the brow");
        ruler.Renown = 60;
        return (Context(world), ruler);
    }

    [Fact]
    public void TheWorldsNeeds_BecomePrayers_ThatThePlayersMayGrant()
    {
        var (ctx, ruler) = Wounded();
        new PrayerSystem().OnYearlyTick(ctx);
        var prayer = Assert.Single(ctx.World.Prayers.Values, p => p.SupplicantId == ruler.Id);
        Assert.Equal((Prayer.Healing, Prayer.Open), (prayer.Plea, prayer.State));
        var offered = ctx.Log.Get(prayer.EventId);
        Assert.Equal(("PrayerOffered", "healing"), (offered.Type, offered.Payload["plea"]));
        var zeal = ctx.World.Faiths[prayer.FaithId].Zeal;

        var told = PrayerSystem.Apply(ctx, new Tally(new Dictionary<long, (int, int)> { [prayer.Id] = (5, 2) }, new Dictionary<long, (int, int)>()));
        var answered = Assert.Single(told);
        Assert.Equal(("PrayerAnswered", "players", "5"), (answered.Type, answered.Payload["by"], answered.Payload["grant"]));
        Assert.Equal(Prayer.Granted, prayer.State);
        Assert.Empty(ruler.Wounds);
        Assert.Equal(zeal + 3, ctx.World.Faiths[prayer.FaithId].Zeal);
    }

    [Fact]
    public void ADeniedPrayer_GoesUnheeded_AndATieWaits()
    {
        var (ctx, ruler) = Wounded();
        new PrayerSystem().OnYearlyTick(ctx);
        var prayer = ctx.World.Prayers.Values.Single(p => p.SupplicantId == ruler.Id);
        Assert.Empty(PrayerSystem.Apply(ctx, new Tally(new Dictionary<long, (int, int)> { [prayer.Id] = (3, 3) }, new Dictionary<long, (int, int)>())));
        Assert.True(prayer.IsOpen);
        var told = PrayerSystem.Apply(ctx, new Tally(new Dictionary<long, (int, int)> { [prayer.Id] = (1, 4) }, new Dictionary<long, (int, int)>()));
        Assert.Equal("PrayerUnheeded", Assert.Single(told).Type);
        Assert.Equal((Prayer.Denied, 1), (prayer.State, ruler.Wounds.Count));
    }

    [Fact]
    public void APrayerTheyLeaveForAYear_IsDecidedByFate()
    {
        var (ctx, ruler) = Wounded();
        var system = new PrayerSystem();
        system.OnYearlyTick(ctx);
        var prayer = ctx.World.Prayers.Values.Single(p => p.SupplicantId == ruler.Id);
        ctx.World.Date = ctx.World.Date.AddDays(WorldDate.DaysPerYear);
        system.OnYearlyTick(ctx);
        Assert.False(prayer.IsOpen);
        Assert.Equal("fate", ctx.Log.Get(prayer.AnsweredEventId).Payload["by"]);
    }

    [Fact]
    public void ABlessedSide_FightsWithTheGodsFavour()
    {
        var world = TestRealms.TwoKingdoms();
        var ctx = Context(world);
        var war = new War
        {
            Id = world.Ids.Take(War.Kind), Name = "The Test War", AttackerId = 1, DefenderId = 2, Attackers = { 1 }, Defenders = { 2 },
            StartDay = world.Date.Day, HostilitiesDay = world.Date.Day + War.MusterDays,
        };
        world.Wars[war.Id] = war;
        var declared = ctx.Log.Append(world.Date, new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = world.Realms[1].Ref, ["defender"] = world.Realms[2].Ref, ["war"] = war.Ref }));
        Assert.Equal(new[] { declared.Id }, PrayerSystem.Blessable(world, ctx.Log).Select(e => e.Id));
        var before = DiplomacySystem.Strength(world, world.Realms[2]);

        var told = PrayerSystem.Apply(ctx, new Tally(new Dictionary<long, (int, int)>(), new Dictionary<long, (int, int)> { [declared.Id] = (1, 9) }));
        var blessed = Assert.Single(told);
        Assert.Equal(("GodsBlessed", world.Realms[2].Ref), (blessed.Type, blessed.Actors["blessed"]));
        Assert.True(PrayerSystem.Favoured(world, world.Realms[2].Ref));
        Assert.Empty(PrayerSystem.Blessable(world, ctx.Log)); // blessed once
        Assert.Empty(PrayerSystem.Apply(ctx, new Tally(new Dictionary<long, (int, int)>(), new Dictionary<long, (int, int)> { [declared.Id] = (1, 9) })));
        Assert.Equal(before * PrayerSystem.FavourStrength, DiplomacySystem.Strength(world, world.Realms[2]), 6);
        world.Date = world.Date.AddDays(PrayerSystem.FavourDays + 1);
        Assert.False(PrayerSystem.Favoured(world, world.Realms[2].Ref));
    }

    [Fact]
    public void TheSitesTally_IsRead_IgnoringWhatItCannotUse()
    {
        var tally = Tally.Parse("""{"prayers": {"12": {"grant": 3, "deny": 1}, "x": {"grant": 9}}, "blessings": {"456": {"a": 2}}, "other": 1}""");
        Assert.Equal((3, 1), tally.Prayers[12]);
        Assert.Single(tally.Prayers);
        Assert.Equal((2, 0), tally.Blessings[456]);
    }

    [Fact]
    public void TheEpisode_TellsThePrayers_AndCallsOnThePlayers_NotAmongItsStories()
    {
        var (ctx, ruler) = Wounded();
        new PrayerSystem().OnYearlyTick(ctx);
        var episode = FantasyWorld.Sim.Chronicle.Episode.Build(ctx.World, ctx.Log, 0, "2026-10-28");
        var section = Assert.Single(episode.Parts, p => p.Kind == FantasyWorld.Sim.Chronicle.EpisodeSection.Prayers);
        Assert.Contains("New prayers rise to the gods.", section.Text);
        Assert.Contains("You are their gods", section.Text);
        Assert.DoesNotContain(episode.Stories, s => ctx.Log.Get(s.EventId).Type == "PrayerOffered");
    }
}

using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class EpisodeTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void TheFirstEpisode_TellsTheLatestYear_AndSumsUpTheHistoryBehindIt()
    {
        var world = Aged.World;
        var episode = Episode.Build(world, Aged.Log, 0, "2026-10-27");
        Assert.StartsWith($"The chronicle of {world.Name} begins in Year {world.Date.Year}", episode.ColdOpen);
        Assert.All(episode.Stories, s => Assert.True(s.Year >= world.Date.Year - 1));
        Assert.True(episode.EventCount < Aged.Log.Count);
        Assert.Equal(Aged.Log.NextId - 1, episode.LastEventId);
        Assert.Equal(0, world.LastBroadcastEventId); // building never moves the marker
        var back = Episode.FromJson(episode.ToJson());
        Assert.Equal(episode, back with { Stories = episode.Stories, Sections = episode.Sections });
        Assert.Equal(episode.Sections, back.Sections);
        // The sections are the script, in order, and each is a chapter with a title.
        Assert.Equal(episode.Script, Episode.ScriptOf(episode.Parts));
        Assert.Equal(EpisodeSection.Opening, episode.Parts[0].Kind);
        Assert.Equal(EpisodeSection.Closing, episode.Parts[^1].Kind);
        Assert.Contains(episode.Parts, s => s.Kind == EpisodeSection.Headline && s.EventId == episode.Headline!.EventId);
        Assert.Equal(episode.Stories.Select(s => s.EventId), episode.Parts.Where(s => s.Kind == EpisodeSection.Story).Select(s => s.EventId));
        Assert.All(episode.Parts, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
    }

    [Fact]
    public void AnEpisode_StartsWhereTheAudienceLeftOff_AndTellsAGap()
    {
        var since = Aged.Log.All.Last(e => e.Date.Year == Aged.World.Date.Year - 4).Id;
        var episode = Episode.Build(Aged.World, Aged.Log, since, "2026-10-27");
        Assert.All(episode.Stories, s => Assert.True(s.EventId > since));
        Assert.StartsWith("Four years have passed", episode.ColdOpen);
        Assert.Contains("In Year ", episode.Script);
        Assert.Equal(Aged.Log.All.Count(e => e.Id > since), episode.EventCount);
    }

    [Fact]
    public void AQuietSpell_StillMakesAnEpisode()
    {
        var episode = Episode.Build(Aged.World, Aged.Log, Aged.Log.NextId - 1, "2026-10-27");
        Assert.Null(episode.Headline);
        Assert.Empty(episode.Stories);
        Assert.Contains("quiet", episode.ColdOpen);
        Assert.EndsWith(episode.SignOff, episode.Script);
    }

    [Fact]
    public void WorldNewsLeads_OneStoryAPerson_AndTheScriptFitsItsBudget()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 100;
        for (var i = 0; i < 4; i++)
            log.Append(world.Date, new EventDraft("DistinguishedInBattle", Actors: new Dictionary<string, EntityRef> { ["character"] = hero.Ref },
                Place: world.Regions[1].Ref, Importance: 70));
        var fell = log.Append(world.Date, new EventDraft("RealmFell", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[2].Ref },
            Place: world.Settlements[2].Ref, Importance: 60));
        for (var i = 0; i < 3; i++)
            log.Append(world.Date, new EventDraft("RegionCeded", Actors: new Dictionary<string, EntityRef> { ["from"] = world.Realms[2].Ref, ["to"] = world.Realms[1].Ref },
                Place: world.Regions[2].Ref, Importance: 45));
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        Assert.Equal(fell.Id, episode.Headline!.EventId);
        Assert.Equal(new[] { "DistinguishedInBattle", "RegionCeded" }, episode.Stories.Select(s => log.Get(s.EventId).Type));
        Assert.Null(episode.Why); // the fall has no causes in this log

        var tight = Episode.Build(Aged.World, Aged.Log, Aged.Log.All.Last(e => e.Date.Year == Aged.World.Date.Year - 10).Id, "2026-10-27", maxCharacters: 900);
        Assert.True(tight.Script.Length <= 900 || tight.Stories.Count == 0 && tight.Why is null);
    }

    [Fact]
    public void TheChroniclersView_ClosesTheNews_InsteadOfHowItCameToThis()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var drought = log.Append(new WorldDate(0), new EventDraft("Drought", Place: world.Regions[1].Ref));
        var flood = log.Append(new WorldDate(10), new EventDraft("Flood", Place: world.Regions[1].Ref));
        var shortage = log.Append(new WorldDate(400), new EventDraft("FoodShortageBegan", Place: world.Regions[1].Ref,
            Causes: new[] { new Cause(drought.Id, CauseKind.Trigger, 0.9), new Cause(flood.Id, CauseKind.Enabler, 0.2) }));
        var revolt = log.Append(new WorldDate(800), new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref,
            Causes: new[] { new Cause(shortage.Id, CauseKind.Trigger, 1) }));
        Assert.Equal(new[] { revolt.Id, shortage.Id, drought.Id }, Episode.Thread(log, revolt).Select(e => e.Id));
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        Assert.Equal(revolt.Id, episode.Headline!.EventId);
        // No "how it came to this" (the owner, 2026-10-27): the headline gives its cause in a line, and the chronicler's
        // view comes after the stories, before the sign-off.
        Assert.Null(episode.Why);
        Assert.DoesNotContain(episode.Sections!, s => s.Kind == EpisodeSection.Why);
        Assert.Contains("food ran short", episode.HeadlineBody);
        var kinds = episode.Sections!.Select(s => s.Kind).ToList();
        Assert.Equal(new[] { EpisodeSection.Comment, EpisodeSection.Closing }, kinds.TakeLast(2));
        var view = episode.Sections![^2];
        Assert.Equal("The chronicler's view", view.Title);
        Assert.StartsWith("What to make of it all?", view.Text);
        Assert.Contains(episode.Headline.Text, view.Text);
    }
}

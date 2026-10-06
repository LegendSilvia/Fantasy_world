using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class ImportanceTests
{
    [Fact]
    public void FameAndFollowing_RaiseAnEventsImportance()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var nobody = TestPeople.Add(world, 1, 30);
        var hero = TestPeople.Add(world, 1, 30);
        hero.Renown = 80;
        EventDraft Draft(Character c) => new("DuelFought", Actors: new Dictionary<string, EntityRef> { ["challenger"] = c.Ref }, Importance: 20);
        var plain = log.Append(world.Date, Draft(nobody));
        var famous = log.Append(world.Date, Draft(hero));
        var none = new HashSet<EntityRef>();

        Assert.Equal(20, Importance.Score(plain, world, none));
        Assert.Equal(36, Importance.Score(famous, world, none));
        Assert.Equal(50, Importance.Score(plain, world, new HashSet<EntityRef> { nobody.Ref }));
        var realmEvent = log.Append(world.Date, new EventDraft("RealmFell", Actors: new Dictionary<string, EntityRef> { ["realm"] = world.Realms[1].Ref }, Importance: 95));
        Assert.Equal(100, Importance.Score(realmEvent, world, none));
    }

    [Fact]
    public void EntityRefs_ParseAsPrinted()
    {
        Assert.Equal(new EntityRef("char", 12), Importance.ParseRef("char#12"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("char12"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("#3"));
        Assert.Throws<FormatException>(() => Importance.ParseRef("char#-3"));
    }

    [Fact]
    public void FollowingIsStored_AndChecked()
    {
        using var file = new TempFile();
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => DateTimeOffset.UnixEpoch);
        Assert.Equal(0, CliApp.Run(new[] { "follow", file.Path, "region#2" }, output, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("Following ", output.ToString());
        using (var store = WorldStore.Open(file.Path)) Assert.Contains(new EntityRef("region", 2), store.LoadFollowed());

        CliApp.Run(new[] { "unfollow", file.Path, "region#2" }, output, () => DateTimeOffset.UnixEpoch);
        using (var store = WorldStore.Open(file.Path)) Assert.Empty(store.LoadFollowed());

        var error = new StringWriter();
        Assert.Equal(1, CliApp.Run(new[] { "follow", file.Path, "char#99999" }, error, () => DateTimeOffset.UnixEpoch));
        Assert.Contains("error: No char#99999", error.ToString());
    }
}

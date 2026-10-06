using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class DigestScreenTests
{
    [Fact]
    public void Worlds_HaveAStableName_ThatPersists()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        Assert.NotEmpty(world.Name);
        Assert.Equal(world.Name, WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch).Name);
        using var file = new TempFile();
        using var store = WorldStore.Open(file.Path);
        store.Save(world, new EventLog(), new EventQueue());
        Assert.Equal(world.Name, store.Load().World.Name);
    }

    [Fact]
    public void TheDigestScreen_NumbersEventsByImportance_AndPinsTheirPlaces()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(5, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * 3);
        var since = world.Date;
        var seen = sim.Log.NextId - 1;
        sim.AdvanceDays(360 * 2);
        var digest = Digest.Build(sim.Log, world, seen, new HashSet<EntityRef>());

        var screen = DigestScreen.Build(world, sim.Log, digest, since);

        Assert.Equal(world.Name, screen.WorldName);
        Assert.Equal("Year 6.", screen.Title);
        Assert.StartsWith($"While you were away, two years passed in {world.Name}. The seasons turned, and ", screen.Subtitle);
        Assert.EndsWith(" events were recorded.", screen.Subtitle);
        Assert.Equal(screen.Pins.Count, screen.Pins.Select(p => p.Place).Distinct().Count());
        Assert.Equal("Caught up · Year 6, day 1", screen.Status);
        var numbered = new[] { screen.Headline!.Number }.Concat(screen.Items.Select(i => i.Number)).ToList();
        Assert.Equal(Enumerable.Range(1, digest.Entries.Count()), numbered);
        Assert.Equal(digest.Entries.MaxBy(e => e.Score)!.Score, digest.Entries.Single(e => e.Event.Id == screen.Headline.EventId).Score);
        Assert.InRange(screen.Pins.Count, 1, 5);
        Assert.All(screen.Pins, p => Assert.True(p.Position.X is > 0 and < 1 && p.Position.Y is > 0 and < 1));
        var nameOf = Names.Of(world);
        foreach (var text in screen.Items.Select(i => i.Text).Append(screen.Headline.Title))
            Assert.All(text.Links, l => Assert.True(LinkedText.Reads(text.Text.Substring(l.Start, l.Length), nameOf(l.Entity))));
    }

    [Fact]
    public void AnEmptyDigest_SaysSo()
    {
        var world = TestPeople.World();
        var screen = DigestScreen.Build(world, new EventLog(), Digest.Build(new EventLog(), world, 0, new HashSet<EntityRef>()), world.Date, daysBehind: 12);
        Assert.Null(screen.Headline);
        Assert.Empty(screen.Items);
        Assert.Equal("Catching up · 12 days behind", screen.Status);
        Assert.StartsWith("Nothing has passed", screen.Subtitle);
        Assert.Equal("The World", screen.WorldName);
    }
}

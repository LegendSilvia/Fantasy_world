using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Chronicle;

public class DigestTests
{
    private static readonly HashSet<EntityRef> None = new();

    private static EventLog Log(params (long Day, int Importance)[] events)
    {
        var log = new EventLog();
        foreach (var (day, importance) in events)
            log.Append(new WorldDate(day), new EventDraft("Rumour", Importance: importance));
        return log;
    }

    [Fact]
    public void AShortAbsence_ShowsTheMostImportantEvents_InOrder()
    {
        var world = TestPeople.World();
        var log = Log((1, 10), (2, 90), (3, 50), (4, 70), (5, 5));
        var digest = Digest.Build(log, world, 0, None, take: 3);
        Assert.Equal(5, digest.EventCount);
        var group = Assert.Single(digest.Groups);
        Assert.Equal("", group.Label);
        Assert.Equal(new long[] { 2, 3, 4 }, group.Entries.Select(e => e.Event.Id));
        Assert.Equal(new long[] { 4, 5 }, Digest.Build(log, world, 3, None, take: 3).Entries.Select(e => e.Event.Id));
    }

    [Fact]
    public void FollowedEntities_LeadTheDigest()
    {
        var world = TestPeople.World();
        var log = Log((1, 60), (2, 60));
        var followed = world.Regions[1].Ref;
        log.Append(new WorldDate(3), new EventDraft("Rumour", Place: followed, Importance: 35));
        Assert.Equal(new long[] { 3 }, Digest.Build(log, world, 0, new HashSet<EntityRef> { followed }, take: 1).Entries.Select(e => e.Event.Id));
    }

    [Fact]
    public void ALongAbsence_IsToldBySeason()
    {
        var world = TestPeople.World();
        var log = Log((10, 20), (100, 80), (200, 30), (400, 90), (700, 60));
        var digest = Digest.Build(log, world, 0, None, take: 8);
        Assert.All(digest.Groups, g => Assert.Contains(" of Year ", g.Label));
        // Five seasons saw events; the four with the most important ones are kept, in calendar order.
        Assert.Equal(new[] { "Summer of Year 1", "Autumn of Year 1", "Spring of Year 2", "Winter of Year 2" }, digest.Groups.Select(g => g.Label));
        Assert.Equal(digest.Groups.Select(g => g.Entries[0].Event.Id).OrderBy(id => id), digest.Groups.Select(g => g.Entries[0].Event.Id));
    }

    [Fact]
    public void TheDigest_IsNarrated_ByTemplateOrLlm()
    {
        var world = TestPeople.World();
        var log = Log((1, 50));
        var digest = Digest.Build(log, world, 0, None);
        Assert.StartsWith("From Y1 M1 D2 to Y1 M1 D2, one event was recorded. The chronicle remembers:\n- In Year 1, ", new TemplateNarrator(world).Digest(digest));
        Assert.Equal("Nothing of note has happened since you last looked.", new TemplateNarrator(world).Digest(Digest.Build(log, world, 1, None)));

        var fake = new FakeLlmProvider(_ => "{\"text\":\"While you slept, rumours spread.\"}");
        var story = new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal()));
        Assert.Equal("While you slept, rumours spread.", new LlmNarrator(story).Digest(digest));
        Assert.Contains("morning digest", fake.Requests.Single().Prompt);
    }

    [Fact]
    public void Catchup_EndsWithTheDigest_AndMarksItSeen()
    {
        using var file = new TempFile();
        var t0 = new DateTimeOffset(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);
        var output = new StringWriter();
        CliApp.Run(new[] { "new", file.Path, "--seed", "3", "--size", "tiny" }, output, () => t0);
        output = new StringWriter();
        Assert.Equal(0, CliApp.Run(new[] { "catchup", file.Path }, output, () => t0.AddDays(3)));
        Assert.Contains("The chronicle remembers:", output.ToString());

        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path }, output, () => t0.AddDays(3));
        Assert.Contains("Nothing of note has happened since you last looked.", output.ToString());
        output = new StringWriter();
        CliApp.Run(new[] { "digest", file.Path, "--since", "0", "--take", "2" }, output, () => t0.AddDays(3));
        Assert.Equal(2, output.ToString().Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
    }
}

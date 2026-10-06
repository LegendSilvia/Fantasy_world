using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Engine;

public class SimulatorTests
{
    private sealed class RecordingSystem : ISimSystem
    {
        private readonly List<string> _trace;
        public RecordingSystem(string name, List<string> trace, params string[] kinds)
        {
            Name = name;
            _trace = trace;
            HandledKinds = kinds;
        }
        public string Name { get; }
        public IEnumerable<string> HandledKinds { get; }
        public void OnMonthlyTick(SimContext ctx) => _trace.Add($"{Name}@{ctx.Date.Day}");
        public void OnScheduled(SimContext ctx, ScheduledAction action) => _trace.Add($"{Name}:{action.Kind}@{ctx.Date.Day}");
    }

    private sealed class YearlySystem : ISimSystem
    {
        private readonly List<string> _trace;
        public YearlySystem(List<string> trace) => _trace = trace;
        public string Name => "yearly";
        public void OnMonthlyTick(SimContext ctx) => _trace.Add($"month@{ctx.Date.Day}");
        public void OnYearlyTick(SimContext ctx) => _trace.Add($"year@{ctx.Date.Day}");
    }

    private static Simulator Make(params ISimSystem[] systems) =>
        new(WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch), new EventLog(), new EventQueue(), systems, checkInvariants: true);

    [Fact]
    public void MonthlyTick_FiresOnFirstOfEachMonth_InRegistrationOrder()
    {
        var trace = new List<string>();
        var sim = Make(new RecordingSystem("a", trace), new RecordingSystem("b", trace));
        sim.AdvanceDays(90);
        Assert.Equal(new[] { "a@30", "b@30", "a@60", "b@60", "a@90", "b@90" }, trace);
        Assert.Equal(90, sim.World.Date.Day);
    }

    [Fact]
    public void ScheduledActions_RunOnTheirDate_AfterMonthlyTick()
    {
        var trace = new List<string>();
        var sim = Make(new RecordingSystem("a", trace, "ping"));
        sim.Queue.Schedule(new WorldDate(30), "ping");
        sim.Queue.Schedule(new WorldDate(5), "ping");
        sim.AdvanceDays(30);
        Assert.Equal(new[] { "a:ping@5", "a@30", "a:ping@30" }, trace);
    }

    [Fact]
    public void YearlyTick_FiresOnFirstOfYear_AfterMonthlyTicks()
    {
        var trace = new List<string>();
        var sim = Make(new YearlySystem(trace));
        sim.AdvanceDays(720);
        Assert.Equal(new[] { "month@360", "year@360" }, trace.Where(t => t.EndsWith("@360")));
        Assert.Equal(2, trace.Count(t => t.StartsWith("year@")));
        Assert.Equal(24, trace.Count(t => t.StartsWith("month@")));
    }

    [Fact]
    public void UnknownScheduledKind_Throws()
    {
        var sim = Make();
        sim.Queue.Schedule(new WorldDate(1), "nobody");
        var ex = Assert.Throws<InvalidOperationException>(() => sim.AdvanceDays(1));
        Assert.Contains("nobody", ex.Message);
    }

    [Fact]
    public void DuplicateKindHandlers_AreRejected()
    {
        var trace = new List<string>();
        Assert.Throws<ArgumentException>(() =>
            Make(new RecordingSystem("a", trace, "x"), new RecordingSystem("b", trace, "x")));
    }

    [Fact]
    public void Progress_ReportsEachNewMonth()
    {
        var sim = Make();
        var reports = new List<WorldDate>();
        sim.AdvanceDays(65, new Support.ListProgress<WorldDate>(reports.Add));
        Assert.Equal(new long[] { 30, 60 }, reports.Select(r => r.Day));
    }

    [Fact]
    public void Cancellation_StopsAdvancing()
    {
        var sim = Make();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => sim.AdvanceDays(10, ct: cts.Token));
        Assert.Equal(0, sim.World.Date.Day);
    }

    [Fact]
    public void Invariants_DetectMissingCause()
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        world.Date = new WorldDate(10);
        var log = new EventLog();
        log.Restore(new WorldEvent(1, new WorldDate(5), "Broken",
            new SortedDictionary<string, EntityRef>(StringComparer.Ordinal), null,
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new[] { new Cause(99, CauseKind.Trigger, 1) }, 10, Array.Empty<string>()));
        var errors = Invariants.Check(world, log);
        Assert.Contains(errors, e => e.Contains("#99"));
    }

    [Fact]
    public void Invariants_DetectFutureEvent()
    {
        var world = WorldFactory.CreateNew(1, DateTimeOffset.UnixEpoch);
        var log = new EventLog();
        log.Append(new WorldDate(50), new EventDraft("TooEarly"));
        Assert.Contains(Invariants.Check(world, log), e => e.Contains("future"));
    }

    [Fact]
    public void SystemsGetDistinctRandomStreams()
    {
        var draws = new Dictionary<string, ulong>();
        var a = new DrawSystem("a", draws);
        var b = new DrawSystem("b", draws);
        Make(a, b).AdvanceDays(30);
        Assert.NotEqual(draws["a"], draws["b"]);
    }

    private sealed class DrawSystem : ISimSystem
    {
        private readonly Dictionary<string, ulong> _draws;
        public DrawSystem(string name, Dictionary<string, ulong> draws) { Name = name; _draws = draws; }
        public string Name { get; }
        public void OnMonthlyTick(SimContext ctx) => _draws[Name] = ctx.Rng.NextULong();
    }
}

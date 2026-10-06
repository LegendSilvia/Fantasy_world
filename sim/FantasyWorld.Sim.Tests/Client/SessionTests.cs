using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class SessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = T0;
    }

    [Fact]
    public async Task ASession_CatchesUpOnAWorker_ThenLivesAndSaves()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (var created = Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }

        clock.Now = T0.AddHours(8);
        using (var session = Session.Open(file.Path, () => clock.Now))
        {
            Assert.Equal(120, session.DaysBehind);
            var months = new List<WorldDate>();
            var result = await session.CatchUpAsync(new Progress<WorldDate>(months.Add));
            Assert.Equal(120, result.DaysAdvanced);
            Assert.Equal(0, session.DaysBehind);
            Assert.False(session.Busy);

            clock.Now = clock.Now.AddMinutes(4);
            Assert.Equal(1, session.Step().Days);
            Assert.Equal(0, session.Step().Days);
            Assert.Equal(clock.Now.AddMinutes(4), session.NextDayAt);
            Assert.NotNull(session.TryRead((w, _) => w.Name));
        }
        Assert.True(File.Exists(Backups.PathOf(file.Path, 1)));
        using var reopened = WorldStore.Open(file.Path);
        Assert.Equal(121, reopened.Load().World.Date.Day);
    }

    [Fact]
    public void TheDigest_IsReadOnce_AndFollowingLeadsIt()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using var session = Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now, prehistoryYears: 5);

        var digest = session.Digest();
        Assert.NotNull(digest.Headline);
        Assert.Equal("Caught up · Year 6, day 1", digest.Status);
        session.MarkSeen();
        Assert.Null(session.Digest().Headline);

        var lord = session.Read((w, _) => w.Settlements.Values.First(s => s.LordId != 0).LordId);
        session.Follow(new EntityRef("char", lord));
        Assert.True(session.Character(lord).Followed);
        Assert.NotEmpty(session.Map().Settlements);
        Assert.NotEmpty(session.Map(year: 2).Regions);
    }

    [Fact]
    public void OpeningAMissingWorld_IsAClearError()
    {
        Assert.Throws<FileNotFoundException>(() => Session.Open("/nowhere/none.fw.db", () => T0));
        using var file = new TempFile();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => T0)) { }
        Assert.Throws<InvalidOperationException>(() => Session.Create(file.Path, 3, WorldSize.Tiny, () => T0));
    }

    [Fact]
    public async Task ACancelledCatchUp_KeepsTheWorldClockInStepWithTheDaysSimulated()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }
        clock.Now = T0.AddDays(20); // 20 real days: 20 world years owed
        using var cts = new CancellationTokenSource();
        using (var session = Session.Open(file.Path, () => clock.Now))
        {
            var owed = session.DaysBehind;
            var before = session.Read((w, _) => w.Date.Day);
            var progress = new Progress<WorldDate>(_ => cts.Cancel());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CatchUpAsync(progress, cts.Token));
            var simulated = session.Read((w, _) => w.Date.Day) - before;
            Assert.InRange(simulated, 1, owed - 1);
            Assert.Equal(owed - simulated, session.DaysBehind);
        }
        using var reopened = Session.Open(file.Path, () => clock.Now);
        Assert.True(reopened.DaysBehind > 0);
    }

    [Fact]
    public void AWorldThatFailsToLoad_DoesNotPushOutTheBackups()
    {
        using var file = new TempFile();
        var clock = new Clock();
        using (Session.Create(file.Path, 3, WorldSize.Tiny, () => clock.Now)) { }
        using (Session.Open(file.Path, () => clock.Now)) { }
        var backup = Backups.PathOf(file.Path, 1);
        Assert.True(File.Exists(backup));
        var good = File.ReadAllBytes(backup);
        File.WriteAllText(file.Path, "not a world");
        Assert.ThrowsAny<Exception>(() => Session.Open(file.Path, () => clock.Now));
        Assert.Equal(good, File.ReadAllBytes(backup));
        Assert.False(File.Exists(Backups.PathOf(file.Path, 2)));
    }
}

using FantasyWorld.Cli;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Cli;

public class CliAppTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(DateTimeOffset now, params string[] args)
    {
        var output = new StringWriter();
        var code = CliApp.Run(args, output, () => now);
        return (code, output.ToString());
    }

    [Fact]
    public void NoArguments_PrintsUsage()
    {
        var (code, output) = Run(T0);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void UnknownCommand_PrintsUsage()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "dance", file.Path);
        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public void New_CreatesWorldFile()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "new", file.Path, "--seed", "42");
        Assert.Equal(0, code);
        Assert.Contains("seed 42", output);
        using var store = WorldStore.Open(file.Path);
        Assert.True(store.HasWorld);
        Assert.Equal(42UL, store.Load().World.Seed);
    }

    [Fact]
    public void New_RefusesToOverwrite()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "1");
        var (code, output) = Run(T0, "new", file.Path, "--seed", "2");
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void Advance_MovesTheWorldAndSaves()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3");
        var (code, output) = Run(T0, "advance", file.Path, "2y");
        Assert.Equal(0, code);
        Assert.Contains("Y3 M1 D1", output);
        using var store = WorldStore.Open(file.Path);
        Assert.Equal(720, store.Load().World.Date.Day);
    }

    [Fact]
    public void CatchUp_UsesTheClock()
    {
        using var file = new TempFile();
        Run(T0, "new", file.Path, "--seed", "3");
        var (code, output) = Run(T0.AddHours(8), "catchup", file.Path);
        Assert.Equal(0, code);
        Assert.Contains("Advanced 120 days", output);
    }

    [Fact]
    public void MissingWorldFile_IsAnError()
    {
        using var file = new TempFile();
        var (code, output) = Run(T0, "log", file.Path);
        Assert.Equal(1, code);
        Assert.Contains("error:", output);
    }

    [Fact]
    public void Why_PrintsTheCausalChain()
    {
        using var file = new TempFile();
        var world = WorldFactory.CreateNew(3, T0);
        world.Stats["grain"] = 10;
        world.Stats["unrest"] = 45;
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());

        Run(T0, "advance", file.Path, "1mo");
        long revoltId;
        using (var store = WorldStore.Open(file.Path))
            revoltId = store.Load().Log.All.Single(e => e.Type == "RevoltBegan").Id;

        var (code, output) = Run(T0, "why", file.Path, revoltId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, code);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("RevoltBegan", lines[0]);
        Assert.StartsWith("  because #", lines[1]);
        Assert.Contains("GrainShortageBegan", lines[1]);
    }

    [Fact]
    public void Log_ListsEventsSinceId()
    {
        using var file = new TempFile();
        var world = WorldFactory.CreateNew(3, T0);
        world.Stats["grain"] = 10;
        world.Stats["unrest"] = 45;
        using (var store = WorldStore.Open(file.Path)) store.Save(world, new EventLog(), new EventQueue());
        Run(T0, "advance", file.Path, "1mo");

        var (code, output) = Run(T0, "log", file.Path, "--since", "1");
        Assert.Equal(0, code);
        Assert.DoesNotContain("#1 ", output);
        Assert.Contains("#2 ", output);
    }
}

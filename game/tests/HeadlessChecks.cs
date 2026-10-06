using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// Runs the real scenes headless against a fresh tiny world and checks what they show (sub-project 4 §7). Exits 0 when
/// every check passes, 1 otherwise; CI runs it with `godot --headless --path game res://tests/HeadlessChecks.tscn`.
/// </summary>
public partial class HeadlessChecks : Node
{
    private readonly List<string> _failures = new();
    private int _checks;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "checks", $"check-{System.Guid.NewGuid():N}.fw.db");
        try
        {
            var session = await Task.Run(() => Session.Create(path, 3, WorldSize.Tiny, () => System.DateTimeOffset.UtcNow, prehistoryYears: 8));
            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            main.Use(session);
            AddChild(main);
            await Until(() => main.Ready, "the world catches up");
            await RunChecks(main);
        }
        catch (System.Exception e)
        {
            Fail($"unexpected {e}");
        }
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GD.Print(_failures.Count == 0 ? $"HEADLESS CHECKS PASSED ({_checks})" : $"HEADLESS CHECKS FAILED: {string.Join("; ", _failures)}");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>The checks for each screen; later tasks add theirs.</summary>
    private async Task RunChecks(Main main)
    {
        Check(main.Status.Text.StartsWith("Caught up · "), $"status reads caught up (\"{main.Status.Text}\")");
        await Frames(2);
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) Fail(what);
    }

    private void Fail(string what)
    {
        _failures.Add(what);
        GD.PrintErr("FAIL: " + what);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Until(System.Func<bool> condition, string what, int maxFrames = 600)
    {
        for (var i = 0; i < maxFrames && !condition(); i++) await Frames(1);
        Check(condition(), what);
    }
}

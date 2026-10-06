using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
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
            await Until(() => main.CaughtUp, "the world catches up");
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
        await DioramaChecks(main);
    }

    private async Task DioramaChecks(Main main)
    {
        var diorama = main.Diorama;
        var map = main.Session!.Map();
        Check(diorama.Block.Mesh is ArrayMesh { } block && block.GetSurfaceCount() == 2, "the block has a top and walls");
        Check(diorama.GetNodeOrNull<MeshInstance3D>("Water")?.Mesh is not null, "the sea is drawn");
        Check(diorama.MarkerLayer.GetChildCount() == map.Settlements.Count(s => s.Position is not null) + map.Structures.Count(s => s.Position is not null),
            $"one marker per settlement and structure ({diorama.MarkerLayer.GetChildCount()})");
        Check(map.Borders.Count == 0 || diorama.BorderLines.Mesh is ArrayMesh { } lines && lines.GetSurfaceCount() == 1, "realm borders are drawn");

        diorama.Rig.Reset();
        await Frames(90);
        await Shot("diorama");
        Check(diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Eye)) < 0.01f, "the camera follows the rig");
        var target = diorama.Markers[0];
        var onScreen = diorama.ScreenOf(target.Position);
        Check(diorama.Pick(onScreen) is { } picked && (picked == target.Ref || diorama.Markers.Any(m => m.Ref == picked && diorama.ScreenOf(m.Position).DistanceTo(onScreen) < 1)),
            "clicking a marker picks it");
        Check(diorama.Pick(new Godot.Vector2(-500, -500)) is null, "clicking empty space picks nothing");

        var settlement = map.Settlements.First(s => s.Position is not null);
        diorama.Focus(settlement.Position!, Zoom.Town);
        await Frames(120);
        await Shot("diorama-town");
        Check(diorama.Rig.NearestZoom == Zoom.Town && diorama.Camera.Position.DistanceTo(Diorama.ToGodot(diorama.Rig.Target)) < 1f, "focusing descends to the town");
    }

    /// <summary>With `-- --shots &lt;dir&gt;` (and a real display), saves what the screen shows, for visual review.</summary>
    private async Task Shot(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = System.Array.IndexOf(args, "--shots");
        if (at < 0 || at + 1 >= args.Length || DisplayServer.GetName() == "headless") return;
        await Frames(4);
        System.IO.Directory.CreateDirectory(args[at + 1]);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(args[at + 1], name + ".png"));
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game.Film;

/// <summary>A shot of the episode film from a moment on: a point on the map (0–1) and how close (1 = the whole map).</summary>
public sealed record FilmShot(double Start, double X, double Y, double Zoom);

/// <summary>
/// The episode film on the diorama (sub-project 7 F2). Renders one stretch of an episode's timed shot list from the day's
/// world as PNG frames, so stretches can be filmed on several machines at once and joined:
/// <c>godot --path game --fixed-fps 24 --resolution 1280x720 res://film/Film.tscn -- --world w.fw.db --shots shots.json
/// --from 40 --to 60 --frames out/</c> (needs a display; on a server, <c>xvfb-run</c> and Vulkan through lavapipe).
/// The world is only read: its clock is held at its own time, so nothing is simulated, and no LLM is asked. The camera
/// eases to each shot from its start and turns slowly round it; a stretch starting mid-film begins settled on the shot
/// before its start, as the film's own camera would be. Frames are saved from the first drawn frame of the stretch, so no
/// loading frame is ever filmed. Exit code 0 when every frame was saved.
/// </summary>
public partial class Film : Node
{
    /// <summary>The slow orbit, radians a second: about three degrees, a turn that is felt but never seen to spin.</summary>
    public const float OrbitSpeed = 0.05f;

    /// <summary>The camera's turn when the film starts.</summary>
    public const float StartYaw = 0.785f;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var code = 1;
        try
        {
            code = await FilmAsync();
        }
        catch (Exception e)
        {
            GD.PushError($"film: {e}");
        }
        GetTree().Quit(code);
    }

    private async Task<int> FilmAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1]
            : throw new ArgumentException($"film: {name} is required.");
        var (worldPath, shotsPath, framesDir) = (Arg("--world"), Arg("--shots"), Arg("--frames"));
        var (from, to) = (double.Parse(Arg("--from"), CultureInfo.InvariantCulture), double.Parse(Arg("--to"), CultureInfo.InvariantCulture));
        // The same rate as Godot's --fixed-fps, so each frame advances the camera by exactly one frame's time.
        var fps = Array.IndexOf(args, "--fps") >= 0 ? int.Parse(Arg("--fps"), CultureInfo.InvariantCulture) : 24;
        var shots = JsonSerializer.Deserialize<List<FilmShot>>(System.IO.File.ReadAllText(shotsPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (shots.Count == 0 || to <= from) throw new ArgumentException("film: no shots, or an empty stretch.");
        System.IO.Directory.CreateDirectory(framesDir);

        // The world as stored: its clock stands at its own last moment, so catch-up finds nothing to simulate.
        var now = DateTimeOffset.MinValue;
        var session = await Task.Run(() => Session.Open(worldPath, () => now, _ => null, rotate: false));
        now = session.Read((w, _) => w.LastRealUtc);
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.Use(session);
        AddChild(main);
        while (!main.CaughtUp) await Frames(1);
        main.Router.Go(new Screen(ScreenKind.Map));
        await Frames(2);
        if (main.Current is MapHudView hud) hud.ShowsControls = false;

        // Settled on the shot the film's camera would be holding at the stretch's start, then a few frames for the
        // diorama's meshes and labels to settle before the first frame is kept.
        Aim(main.Diorama, Settled(shots, from), from);
        main.Diorama.Rig.Snap();
        await Frames(6);

        var count = (int)Math.Round((to - from) * fps);
        for (var n = 0; n < count; n++)
        {
            var t = from + n / (double)fps;
            Aim(main.Diorama, ShotAt(shots, t), t);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(System.IO.Path.Combine(framesDir, $"frame{n:000000}.png"));
            if (error != Error.Ok) throw new InvalidOperationException($"film: frame {n} could not be saved ({error}).");
        }
        GD.Print($"film: {count} frames, {from:0.##}–{to:0.##} s, at {fps} fps.");
        return 0;
    }

    /// <summary>The shot the camera is making for at <paramref name="t"/>: the last one started by then.</summary>
    private static FilmShot ShotAt(IReadOnlyList<FilmShot> shots, double t) =>
        shots.LastOrDefault(s => s.Start <= t) ?? shots[0];

    /// <summary>
    /// Where the camera has come to rest just before <paramref name="t"/>: the shot started before it. A stretch beginning
    /// as a new shot starts begins there and makes the move, as the whole film would.
    /// </summary>
    private static FilmShot Settled(IReadOnlyList<FilmShot> shots, double t) =>
        shots.LastOrDefault(s => s.Start < t) ?? shots[0];

    /// <summary>Sets the camera's goal: the shot's place, close or far by its zoom, and the orbit's turn at time t.</summary>
    private static void Aim(Diorama diorama, FilmShot shot, double t)
    {
        diorama.Focus(new MapPoint(shot.X, shot.Y), shot.Zoom >= 2 ? Zoom.Region : Zoom.Realm);
        diorama.Rig.Turn(StartYaw + OrbitSpeed * (float)t);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

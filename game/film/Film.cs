using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game.Film;

/// <summary>
/// A shot of the episode film from a moment on: a point on the map (0–1), how close (1 = the whole map), its section's title,
/// for a face-off both sides' figures and the odds, and for a war its moves in the episode.
/// </summary>
public sealed record FilmShot(double Start, double X, double Y, double Zoom, string Title = "", FaceOff? FaceOff = null,
    IReadOnlyList<CampaignMove>? Moves = null);

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

    /// <summary>How far round the place in view (as a share of the map's width) towns are named; the whole map names none.</summary>
    public const double NamedWithin = 0.09;

    /// <summary>The place in view when the camera is close, else none.</summary>
    private MapPoint? _focus;

    /// <summary>The face-off card's width and its bars' longest reach either side of the figures' names.</summary>
    public const float FaceOffWidth = 900, BarReach = 300;

    /// <summary>The face-off card shown now (its event), its bars with their full lengths, and when its section began.</summary>
    private PanelContainer? _faceOffCard;
    private long _faceOffEvent;
    private readonly List<(ColorRect Bar, float Width)> _bars = new();
    private double _faceOffStart;
    private Control _hud = null!;

    private VBoxContainer _log = null!;
    private PanelContainer _logCard = null!;
    private int _logged;

    /// <summary>A section lasts until the next begins; the last one, about this long.</summary>
    public const double LastSectionSeconds = 20;

    /// <summary>The log's lines in the order they are due: each section's title, then its war's moves in turn.</summary>
    private readonly List<(double At, string Text, bool Move)> _lines = new();

    /// <summary>The move arrows on the diorama: those of the section told now, each drawn in as its move is told.</summary>
    private Node3D _moves = null!;
    private int _movesOf = -1;
    private readonly HashSet<long> _drawn = new();

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
        if (main.Current is not MapHudView hud) throw new InvalidOperationException("film: the map screen did not open.");
        // A film shows the world, not the player's controls; it names only the place in view and its neighbours, and keeps
        // a log of the episode's sections at the top right, a line added as each is narrated.
        hud.ShowsControls = false;
        hud.NamesArmies = false;
        hud.NamesOnly = s => _focus is { } f && s.Position is { } p && Math.Abs(p.X - f.X) <= NamedWithin && Math.Abs(p.Y - f.Y) <= NamedWithin;
        _logCard = Ui.Place(Ui.Card("Log", 320), 1, 0, -40, 40);
        _log = Ui.Column("Lines", 6);
        _log.AddChild(Ui.Kicker("In this episode"));
        _logCard.AddChild(_log);
        _logCard.Visible = false;
        hud.AddChild(_logCard);
        _hud = hud;
        _lines.AddRange(LogLines(shots));
        _moves = new Node3D { Name = "Moves" };
        main.Diorama.AddChild(_moves);

        // Settled on the shot the film's camera would be holding at the stretch's start, then a few frames for the
        // diorama's meshes and labels to settle before the first frame is kept.
        Aim(main.Diorama, Settled(shots, from), from);
        main.Diorama.Rig.Snap();
        Log(shots, from);
        await Frames(6);

        var count = (int)Math.Round((to - from) * fps);
        for (var n = 0; n < count; n++)
        {
            var t = from + n / (double)fps;
            Aim(main.Diorama, ShotAt(shots, t), t);
            Log(shots, t);
            ShowMoves(shots, t, main.Geography!);
            ShowFaceOff(ShotAt(shots, t), t);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(System.IO.Path.Combine(framesDir, $"frame{n:000000}.png"));
            if (error != Error.Ok) throw new InvalidOperationException($"film: frame {n} could not be saved ({error}).");
        }
        GD.Print($"film: {count} frames, {from:0.##}–{to:0.##} s, at {fps} fps.");
        return 0;
    }

    /// <summary>
    /// The face-off card while a face-off's section is told (sub-project 8 §7): both sides' names in their colours, each
    /// figure as two bars growing out from its name, and the odds as one bar in the two colours; the bars grow in over the
    /// section's first second. Hidden for any other section.
    /// </summary>
    private void ShowFaceOff(FilmShot shot, double t)
    {
        if (shot.FaceOff is not { } f)
        {
            if (_faceOffCard is not null) _faceOffCard.Visible = false;
            return;
        }
        if (_faceOffCard is null || _faceOffEvent != f.EventId)
        {
            _faceOffCard?.QueueFree();
            _bars.Clear();
            _faceOffCard = FaceOffCard(f);
            _faceOffEvent = f.EventId;
            _faceOffStart = shot.Start;
            _hud.AddChild(_faceOffCard);
        }
        _faceOffCard.Visible = true;
        var grown = (float)Math.Clamp((t - _faceOffStart) / 1.0, 0, 1);
        grown = 1 - (1 - grown) * (1 - grown);
        foreach (var (bar, width) in _bars) bar.CustomMinimumSize = new Vector2(Math.Max(1, width * grown), bar.CustomMinimumSize.Y);
    }

    private PanelContainer FaceOffCard(FaceOff f)
    {
        var card = Ui.Place(Ui.Card("FaceOff", FaceOffWidth), 0, 1, (1920 - FaceOffWidth) / 2, -56);
        var column = Ui.Column("Column", 10);
        card.AddChild(column);
        Color Of(FaceOffSide side) => Ui.C(Client.WarLayout.Colour(side.Colour));

        // The two names, each with a band of its colour and a line of notes, either side of the kicker.
        var names = Ui.Row("Names", 12);
        Control Name(FaceOffSide side, bool right)
        {
            var box = Ui.Column(right ? "Right" : "Left", 2);
            box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var line = Ui.Row("Line", 8);
            line.Alignment = right ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
            var band = new ColorRect { Name = "Band", Color = Of(side), CustomMinimumSize = new Vector2(8, 26) };
            var title = Ui.Line(side.Name, "Name", Palette.Ink, 22);
            if (right) { line.AddChild(title); line.AddChild(band); }
            else { line.AddChild(band); line.AddChild(title); }
            box.AddChild(line);
            var notes = Ui.Line(string.Join(" · ", side.Notes), "Notes", Palette.Muted, 13);
            notes.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            box.AddChild(notes);
            return box;
        }
        names.AddChild(Name(f.A, false));
        var kicker = Ui.Kicker(f.Kind == FaceOff.Duel ? $"Face-off · {f.Stakes}" : "Face-off · war");
        kicker.HorizontalAlignment = HorizontalAlignment.Center;
        kicker.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        names.AddChild(kicker);
        names.AddChild(Name(f.B, true));
        column.AddChild(names);

        // Each figure: A's bar grows left from the name, B's grows right, both on the figure's own scale.
        for (var i = 0; i < f.A.Stats.Count && i < f.B.Stats.Count; i++)
        {
            var (a, b) = (f.A.Stats[i], f.B.Stats[i]);
            var row = Ui.Row($"Stat{i}", 10);
            row.AddChild(Figure(a.Value, true));
            row.AddChild(Bar(a.Value / Math.Max(1, a.Max), Of(f.A), true));
            var label = Ui.Line(a.Name, "Name", Palette.Body, 14);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.CustomMinimumSize = new Vector2(120, 0);
            row.AddChild(label);
            row.AddChild(Bar(b.Value / Math.Max(1, b.Max), Of(f.B), false));
            row.AddChild(Figure(b.Value, false));
            column.AddChild(row);
        }

        // The odds: one bar, the first side's share in its colour, the rest in the other's.
        var odds = Ui.Row("Odds", 10);
        odds.AddChild(Figure(f.OddsA, true, "%"));
        var split = Ui.Row("Split", 0);
        split.CustomMinimumSize = new Vector2(BarReach * 2 + 140, 14);
        var full = BarReach * 2 + 140;
        var left = new ColorRect { Name = "A", Color = Of(f.A), CustomMinimumSize = new Vector2(1, 14) };
        var right = new ColorRect { Name = "B", Color = Of(f.B), CustomMinimumSize = new Vector2(1, 14) };
        split.AddChild(left);
        split.AddChild(right);
        _bars.Add((left, full * f.OddsA / 100f));
        _bars.Add((right, full * (100 - f.OddsA) / 100f));
        odds.AddChild(split);
        odds.AddChild(Figure(100 - f.OddsA, false, "%"));
        var chance = Ui.Kicker("Chance of winning");
        chance.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(chance);
        column.AddChild(odds);
        return card;
    }

    private static Label Figure(double value, bool right, string unit = "")
    {
        var label = Ui.Line($"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}{unit}", "Figure", Palette.Ink, 15);
        label.CustomMinimumSize = new Vector2(52, 0);
        label.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        return label;
    }

    /// <summary>A bar of up to <see cref="BarReach"/>, growing towards the middle's figure name from its own side.</summary>
    private Control Bar(double share, Color colour, bool fromRight)
    {
        var track = Ui.Row("Track", 0);
        track.CustomMinimumSize = new Vector2(BarReach, 12);
        track.Alignment = fromRight ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
        var bar = new ColorRect { Name = "Bar", Color = colour, CustomMinimumSize = new Vector2(1, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        track.AddChild(bar);
        _bars.Add((bar, BarReach * (float)Math.Clamp(share, 0, 1)));
        return track;
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

    /// <summary>
    /// When each of a section's moves is told: spread evenly over the section, the first after a pause for its opening
    /// words, the last well before the next section.
    /// </summary>
    public static IReadOnlyList<double> MoveTimes(IReadOnlyList<FilmShot> shots, int index)
    {
        var shot = shots[index];
        if (shot.Moves is not { Count: > 0 } moves) return [];
        var end = index + 1 < shots.Count ? shots[index + 1].Start : shot.Start + LastSectionSeconds;
        return moves.Select((_, k) => shot.Start + (end - shot.Start) * (k + 1) / (moves.Count + 1)).ToList();
    }

    /// <summary>The log's lines: every section's title but the opening's (it names the world), each followed by its moves.</summary>
    private static IEnumerable<(double At, string Text, bool Move)> LogLines(IReadOnlyList<FilmShot> shots)
    {
        for (var i = 1; i < shots.Count; i++)
        {
            if (shots[i].Title.Length > 0) yield return (shots[i].Start, shots[i].Title, false);
            var times = MoveTimes(shots, i);
            for (var k = 0; k < times.Count; k++) yield return (times[k], shots[i].Moves![k].Line, true);
        }
    }

    /// <summary>The log holds a line for every section begun by <paramref name="t"/>, and for every move told by then.</summary>
    private void Log(IReadOnlyList<FilmShot> shots, double t)
    {
        for (; _logged < _lines.Count && _lines[_logged].At <= t; _logged++)
        {
            var (_, text, move) = _lines[_logged];
            _log.AddChild(move ? Ui.Body($"· {text}", $"Line{_logged}", Palette.Muted, 12) : Ui.Body(text, $"Line{_logged}", Palette.Body, 14));
        }
        _logCard.Visible = _logged > 0;
    }

    /// <summary>
    /// The moves of the section told at <paramref name="t"/>, each an arrow in its side's colour from where the host stood
    /// to where it went, drawn in as the move is told; a new section clears the last one's.
    /// </summary>
    private void ShowMoves(IReadOnlyList<FilmShot> shots, double t, FantasyWorld.Sim.Model.Geography geography)
    {
        var index = shots.Count - 1;
        while (index > 0 && shots[index].Start > t) index--;
        if (index != _movesOf)
        {
            Ui.Clear(_moves);
            _drawn.Clear();
            _movesOf = index;
        }
        if (shots[index].Moves is not { Count: > 0 } moves) return;
        var times = MoveTimes(shots, index);
        for (var k = 0; k < moves.Count; k++)
        {
            var move = moves[k];
            if (times[k] > t || !_drawn.Add(move.EventId)) continue;
            var colour = WarLayout.SideColour(move.Side);
            var route = new List<System.Numerics.Vector2> { new((float)move.From.X, (float)move.From.Y), new((float)move.To.X, (float)move.To.Y) };
            if (WarLayout.ArrowMesh(route, geography.Terrain, colour) is not { } mesh) continue;
            var material = new ShaderMaterial { Shader = new Shader { Code = WarLayer.ArrowShader } };
            material.SetShaderParameter("progress", 0f);
            var node = new MeshInstance3D { Name = $"Move{move.EventId}", Mesh = WarLayer.Surface(mesh, material) };
            _moves.AddChild(node);
            node.CreateTween().TweenMethod(Callable.From<float>(p => material.SetShaderParameter("progress", p)), 0f, 1f, 1.2)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>Sets the camera's goal: the shot's place, close or far by its zoom, and the orbit's turn at time t.</summary>
    private void Aim(Diorama diorama, FilmShot shot, double t)
    {
        var close = shot.Zoom >= 2;
        _focus = close ? new MapPoint(shot.X, shot.Y) : null;
        diorama.Focus(new MapPoint(shot.X, shot.Y), close ? Zoom.Region : Zoom.Realm);
        diorama.Rig.Turn(StartYaw + OrbitSpeed * (float)t);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game.Film;

/// <summary>
/// A shot of the episode film from a moment on: a point on the map (0–1), how close (1 = the whole map), its section's title,
/// for a face-off both sides' figures and the odds, for a war its moves in the episode, and for a fight its still image and
/// its days as fought.
/// </summary>
public sealed record FilmShot(double Start, double X, double Y, double Zoom, string Title = "", FaceOff? FaceOff = null,
    IReadOnlyList<CampaignMove>? Moves = null, string? Still = null, IReadOnlyList<FightBeat>? Beats = null);

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

    /// <summary>The Short's card (the owner, 2026-10-28: read on a phone): nearly the screen's width, its type and figures 1.8 times as big.</summary>
    public const float TallFaceOffWidth = 1020, TallBarReach = 255, TallType = 1.8f;

    private float Type => _vertical ? TallType : 1;
    private int Px(float size) => (int)Math.Round(size * Type);
    private float Reach => _vertical ? TallBarReach : BarReach;

    private Label Kicker(string text)
    {
        var kicker = Ui.Kicker(text);
        if (_vertical) kicker.AddThemeFontSizeOverride("font_size", Px(11));
        return kicker;
    }

    /// <summary>A side's image (a portrait or a banner) beside the card: its box, about the card's height.</summary>
    public const float ImageWidth = 400, ImageHeight = 585;

    /// <summary>
    /// The face-off's own scene over the diorama: the map behind blurred and darkened, both sides' images either side of the
    /// card. Its event, its bars with their full lengths, the blur's material and when its section began.
    /// </summary>
    private Control? _faceOffStage;
    private ShaderMaterial? _blur;
    private readonly List<Control> _likenesses = new();

    // The showdown's entrance (the owner, 2026-10-28: a dramatic transition): a flash, both sides' colours sweeping in as
    // slanted bands, the images flying in from their sides, a great VS slamming down and shaking the screen, then the card
    // rising into place and its bars growing.
    private Slide? _slideA, _slideB, _slideCard, _bigVersus;
    private Control? _rowVersus;
    private ColorRect? _flash, _bandA, _bandB;
    private string? _imagesDir;

    /// <summary>The map behind a face-off: blurred (taps from a coarse mip level) and darkened as <c>amount</c> rises to 1.</summary>
    private const string BlurShader = """
        shader_type canvas_item;
        uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
        uniform float amount = 0.0;
        void fragment() {
            vec2 px = 1.0 / vec2(textureSize(screen, 0));
            vec3 sum = vec3(0.0);
            float lod = amount * 3.5;
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                    sum += textureLod(screen, SCREEN_UV + vec2(float(x), float(y)) * px * 9.0 * amount, lod).rgb;
            vec3 blurred = sum / 25.0;
            COLOR = vec4(mix(blurred, blurred * 0.5 + vec3(0.03, 0.03, 0.05), amount), 1.0);
        }
        """;
    private long _faceOffEvent;
    private readonly List<(ColorRect Bar, float Width)> _bars = new();
    private double _faceOffStart;
    private Control _hud = null!;

    private VBoxContainer _log = null!;
    private PanelContainer _logCard = null!;

    /// <summary>
    /// The vertical film (the owner, 2026-10-28: a version for Shorts and TikTok, <c>--vertical</c> at 1080×1920): the camera
    /// keeps the map's width in view, the section's title and latest line are big captions in the lower third instead of
    /// the log, and the face-off's images stand above its card.
    /// </summary>
    private bool _vertical;
    private Label? _caption;
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
        // Where the face-offs' images were fetched to (named as in the store); without it, the cards show none.
        _imagesDir = Array.IndexOf(args, "--images") >= 0 ? Arg("--images") : null;
        var (from, to) = (double.Parse(Arg("--from"), CultureInfo.InvariantCulture), double.Parse(Arg("--to"), CultureInfo.InvariantCulture));
        // The same rate as Godot's --fixed-fps, so each frame advances the camera by exactly one frame's time.
        var fps = Array.IndexOf(args, "--fps") >= 0 ? int.Parse(Arg("--fps"), CultureInfo.InvariantCulture) : 24;
        _vertical = Array.IndexOf(args, "--vertical") >= 0;
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
        if (_vertical)
        {
            main.Diorama.Camera.KeepAspect = Camera3D.KeepAspectEnum.Width;
            // Closer in the Short, and the land raised to the middle, so it fills the tall frame rather than its lower half.
            main.Diorama.Camera.Fov = 24;
            (main.Diorama.FrameAcross, main.Diorama.FrameUp) = (0, -0.05f);
            var captions = Ui.Card("Captions", 960);
            var column = Ui.Column("Lines", 8);
            _caption = Ui.Line("", "Title", Palette.Ink, 40);
            // The section's title alone: no subtitles (the owner, 2026-10-29), which could not keep time with the voice.
            _caption.HorizontalAlignment = HorizontalAlignment.Center;
            _caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _caption.CustomMinimumSize = new Vector2(900, 0);
            column.AddChild(_caption);
            captions.AddChild(column);
            (captions.AnchorLeft, captions.AnchorRight, captions.AnchorTop, captions.AnchorBottom) = (0.5f, 0.5f, 0.72f, 0.72f);
            (captions.GrowHorizontal, captions.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Both);
            captions.Name = "Captions";
            hud.AddChild(captions);
        }
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
            ShowStill(shots, t);
            ShowFaceOff(shots, t);
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
    private void ShowFaceOff(IReadOnlyList<FilmShot> shots, double t)
    {
        var index = shots.Count - 1;
        while (index > 0 && shots[index].Start > t) index--;
        var shot = shots[index];
        var from = CardFrom(shots, index);
        if (shot.FaceOff is not { } f || t < from)
        {
            if (_faceOffStage is not null) _faceOffStage.Visible = false;
            return;
        }
        if (_faceOffStage is null || _faceOffEvent != f.EventId)
        {
            _faceOffStage?.QueueFree();
            _bars.Clear();
            _likenesses.Clear();
            _rowVersus = null;
            _faceOffStage = FaceOffStage(f);
            _faceOffEvent = f.EventId;
            _faceOffStart = from;
            _hud.AddChild(_faceOffStage);
        }
        _faceOffStage.Visible = true;
        Entrance((float)(t - _faceOffStart));
    }

    /// <summary>Where the Short's two fighters stand during the entrance (shares of the screen) and how large.</summary>
    private static readonly Vector2 TallA = new(0.3f, 0.3f), TallB = new(0.7f, 0.66f);
    private const float TallStandZoom = 1.6f;

    /// <summary>The share of the way from <paramref name="from"/> to <paramref name="to"/> seconds, 0 to 1.</summary>
    private static float Span(float s, float from, float to) => Math.Clamp((s - from) / (to - from), 0, 1);

    private static float EaseOut(float u) => 1 - (1 - u) * (1 - u) * (1 - u);

    /// <summary>Overshoots a little before settling, as a thing thrown into place does.</summary>
    private static float EaseBack(float u)
    {
        const float c = 1.70158f;
        return 1 + (c + 1) * MathF.Pow(u - 1, 3) + c * MathF.Pow(u - 1, 2);
    }

    /// <summary>The entrance, <paramref name="s"/> seconds after the card's moment came: all of it a pure function of the time, so every frame of a film filmed in stretches agrees.</summary>
    private void Entrance(float s)
    {
        var screen = _hud.Size;
        _blur!.SetShaderParameter("amount", EaseOut(Span(s, 0, 0.5f)));
        if (_flash is not null) _flash.Color = new Color(1, 1, 1, 0.85f * (1 - Span(s, 0, 0.3f)));

        // The bands sweep in across each other, then sink to a glow behind the card.
        var sweep = EaseOut(Span(s, 0.05f, 0.5f));
        var glow = 0.85f - 0.6f * Span(s, 1.2f, 1.7f);
        // Each side's colour sweeps in from its own edge, the first high and the second low (in the Short, behind each
        // fighter as they stand during the entrance).
        void Band(ColorRect? band, float from, float rest, float y)
        {
            if (band is null) return;
            band.Size = new Vector2(screen.X * 1.5f, screen.Y * (_vertical ? 0.3f : 0.28f));
            band.PivotOffset = band.Size / 2;
            var x = from + (rest - from) * sweep;
            band.Position = new Vector2(x - band.Size.X / 2, y - band.Size.Y / 2);
            band.Modulate = new Color(1, 1, 1, glow * Span(s, 0.05f, 0.15f));
        }
        Band(_bandA, -screen.X, screen.X * 0.35f, screen.Y * (_vertical ? TallA.Y : 0.3f));
        Band(_bandB, screen.X * 2, screen.X * 0.65f, screen.Y * (_vertical ? TallB.Y : 0.7f));

        // The images fly in from their own sides. In the Short they first stand large on a diagonal, the first high on
        // the left and the second low on the right with the VS between, then shrink into their row as the card rises.
        var fly = Span(s, 0.2f, 0.75f);
        var settle = _vertical ? EaseOut(Span(s, 1.0f, 1.4f)) : 1;
        foreach (var (slide, side, stand) in new[] { (_slideA, -1f, TallA), (_slideB, 1f, TallB) })
        {
            if (slide is null) continue;
            var home = slide.GetGlobalRect().GetCenter() - _faceOffStage!.GlobalPosition;
            var away = _vertical ? new Vector2(stand.X * screen.X, stand.Y * screen.Y) - home : Vector2.Zero;
            slide.Shift = away * (1 - settle) + new Vector2(side * screen.X * 0.6f * (1 - EaseBack(fly)), 0);
            slide.Zoom = 1 + (TallStandZoom - 1) * (_vertical ? 1 - settle : 0);
            slide.Modulate = new Color(1, 1, 1, Span(s, 0.2f, 0.45f));
            slide.Place();
        }

        // The great VS slams down, holds, and gives way to the card.
        if (_bigVersus is not null)
        {
            var slam = EaseOut(Span(s, 0.45f, 0.75f));
            var away = Span(s, 1.1f, 1.4f);
            _bigVersus.Zoom = 3.2f - 2.2f * slam - 0.4f * away;
            _bigVersus.Turn = -0.35f * (1 - slam);
            _bigVersus.Modulate = new Color(1, 1, 1, Span(s, 0.45f, 0.6f) * (1 - away));
            _bigVersus.Place();
        }
        // The Short's small VS between the images takes over as the great one goes.
        if (_rowVersus is not null) _rowVersus.Modulate = new Color(1, 1, 1, Span(s, 1.1f, 1.4f));

        // The blow shakes the screen, dying away.
        var shake = Span(s, 0.75f, 1.15f);
        var amplitude = shake is > 0 and < 1 ? 18 * (1 - shake) : 0;
        _faceOffStage!.Position = new Vector2(amplitude * MathF.Sin(shake * 40), amplitude * MathF.Cos(shake * 33));

        // The card rises into place, then its bars grow.
        if (_slideCard is not null)
        {
            var rise = EaseBack(Span(s, 1.0f, 1.4f));
            _slideCard.Shift = new Vector2(0, 160 * (1 - rise));
            _slideCard.Zoom = 0.9f + 0.1f * rise;
            _slideCard.Modulate = new Color(1, 1, 1, Span(s, 1.0f, 1.2f));
            _slideCard.Place();
        }
        var grown = EaseOut(Span(s, 1.2f, 2.2f));
        foreach (var image in _likenesses) image.Modulate = Colors.White;
        foreach (var (bar, width) in _bars) bar.CustomMinimumSize = new Vector2(Math.Max(1, width * grown), bar.CustomMinimumSize.Y);
    }

    private static Slide Sliding(string name, Control child)
    {
        var slide = new Slide { Name = name, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        slide.AddChild(child);
        return slide;
    }

    /// <summary>The face-off's scene: the blur over the whole screen, then the images and the card in a row at its middle.</summary>
    private Control FaceOffStage(FaceOff f)
    {
        (var stage, _blur) = Blurred("FaceOffStage");
        Color Of(FaceOffSide side) => Ui.C(Client.WarLayout.Colour(side.Colour));
        _bandA = new ColorRect { Name = "BandA", Color = Of(f.A), Rotation = -0.21f, MouseFilter = Control.MouseFilterEnum.Ignore };
        _bandB = new ColorRect { Name = "BandB", Color = Of(f.B), Rotation = -0.21f, MouseFilter = Control.MouseFilterEnum.Ignore };
        stage.AddChild(_bandA);
        stage.AddChild(_bandB);

        var row = Ui.Row("Row", 36);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.SetAnchorsPreset(Control.LayoutPreset.Center);
        (row.GrowHorizontal, row.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Both);
        var (imageA, imageB) = (Likeness(f.A, "ImageA"), Likeness(f.B, "ImageB"));
        var shown = imageA is not null && imageB is not null;
        _slideA = shown ? Sliding("SlideA", imageA ?? Spacer("ImageA")) : null;
        _slideB = shown ? Sliding("SlideB", imageB ?? Spacer("ImageB")) : null;
        Control? a = _slideA, b = _slideB;
        var card = _slideCard = Sliding("SlideCard", FaceOffCard(f));
        if (_vertical)
        {
            // Tall: the two images side by side above the card.
            var column = Ui.Column("Column", 28);
            column.Alignment = BoxContainer.AlignmentMode.Center;
            column.SetAnchorsPreset(Control.LayoutPreset.Center);
            (column.GrowHorizontal, column.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Both);
            if (shown)
            {
                row.AddChild(a!);
                var vs = _rowVersus = Versus("VersusTall", 150);
                vs.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(vs);
                row.AddChild(b!);
                column.AddChild(row);
            }
            column.AddChild(card);
            stage.AddChild(column);
            return Finish(stage);
        }
        if (shown) row.AddChild(a!);
        row.AddChild(card);
        if (shown) row.AddChild(b!);
        stage.AddChild(row);
        return Finish(stage);

        // Over all of it, the great VS and the flash.
        Control Finish(Control stage)
        {
            _bigVersus = Sliding("BigVersus", Versus("Versus", 300));
            _bigVersus.SetAnchorsPreset(Control.LayoutPreset.Center);
            (_bigVersus.GrowHorizontal, _bigVersus.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Both);
            stage.AddChild(_bigVersus);
            _flash = new ColorRect { Name = "Flash", Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            stage.AddChild(_flash);
            return stage;
        }
    }

    /// <summary>The showdown's VS emblem, <paramref name="height"/> pixels tall, centred.</summary>
    private static Control Versus(string name, float height) => Emblem(name, "versus", height);

    /// <summary>
    /// One of the painted emblems (the owner, 2026-10-28; made with the image API and kept in <c>game/emblems</c>): the VS
    /// or a class's, <paramref name="height"/> pixels square; an empty box of that size if it is missing.
    /// </summary>
    private static Control Emblem(string name, string key, float height)
    {
        var path = $"res://emblems/{key}.png";
        var size = new Vector2(height, height);
        if (!ResourceLoader.Exists(path)) return new Control { Name = name, CustomMinimumSize = size };
        return new TextureRect
        {
            Name = name, Texture = GD.Load<Texture2D>(path), CustomMinimumSize = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>A scene of its own over the diorama: a full-screen layer that blurs and darkens the map behind it.</summary>
    private static (Control Stage, ShaderMaterial Blur) Blurred(string name)
    {
        var stage = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        stage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var material = new ShaderMaterial { Shader = new Shader { Code = BlurShader } };
        var blur = new ColorRect { Name = "Blur", Material = material, MouseFilter = Control.MouseFilterEnum.Ignore };
        blur.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // A margin past the screen's edges, so a shaken scene never shows the map unblurred.
        (blur.OffsetLeft, blur.OffsetTop, blur.OffsetRight, blur.OffsetBottom) = (-48, -48, 48, 48);
        stage.AddChild(blur);
        return (stage, material);
    }

    /// <summary>A fight's still: its size on screen (16:9), and the one shown now (its key) with when its section began.</summary>
    public const float StillWidth = 1344, StillHeight = 756;

    private Control? _stillStage;
    private ShaderMaterial? _stillBlur;
    private PanelContainer? _stillFrame;
    private string? _stillKey;
    private double _stillStart;

    /// <summary>
    /// How long the map shows where a fight is before its still comes up (the owner, 2026-10-29: let the viewer see where
    /// on the map it happens first); at most half the still's time in a short section.
    /// </summary>
    public const double StillDelaySeconds = 3.0;

    /// <summary>
    /// A fight's still image (the owner, 2026-10-27) while its section is told: the map blurred behind, the picture framed
    /// at the middle with the section's title under it, fading in over a second once the map has shown where (StillFrom) and then held, unmoving (for the
    /// first half only when the section also tells moves). Hidden for any other section, or when the image was not fetched.
    /// </summary>
    private void ShowStill(IReadOnlyList<FilmShot> shots, double t)
    {
        var index = shots.Count - 1;
        while (index > 0 && shots[index].Start > t) index--;
        var shot = shots[index];
        var until = StillUntil(shots, index) ?? 0;
        if (shot.Still is not { Length: > 0 } key || _imagesDir is null || t >= until || t < StillFrom(shots, index))
        {
            if (_stillStage is not null) _stillStage.Visible = false;
            return;
        }
        if (_stillStage is null || _stillKey != key)
        {
            _stillStage?.QueueFree();
            _stillStage = null;
            if (Load(key) is not { } image) return;
            (_stillStage, _stillBlur) = Blurred("StillStage");
            _stillFrame = new PanelContainer { Name = "Frame", Modulate = new Color(1, 1, 1, 0) };
            _stillFrame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Ui.C(Palette.Card), CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14,
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12,
                ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 28, ShadowOffset = new Vector2(0, 14),
            });
            var column = Ui.Column("Column", 10);
            column.AddChild(new TextureRect
            {
                Name = "Picture", Texture = ImageTexture.CreateFromImage(image),
                CustomMinimumSize = _vertical ? new Vector2(1000, 1000 * StillHeight / StillWidth) : new Vector2(StillWidth, StillHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            });
            var caption = Ui.Line(shot.Title, "Caption", Palette.Ink, 22);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(caption);
            _stillFrame.AddChild(column);
            _stillFrame.SetAnchorsPreset(Control.LayoutPreset.Center);
            (_stillFrame.GrowHorizontal, _stillFrame.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Both);
            _stillStage.AddChild(_stillFrame);
            _hud.AddChild(_stillStage);
            _stillKey = key;
            _stillStart = StillFrom(shots, index);
        }
        _stillStage.Visible = true;
        // In over the first second; out over the last half second before the map comes back for the moves.
        var shown = (float)Math.Min(Math.Clamp((t - _stillStart) / 1.0, 0, 1), Math.Clamp((until - t) / 0.5, 0, 1));
        shown = 1 - (1 - shown) * (1 - shown);
        _stillBlur!.SetShaderParameter("amount", shown);
        _stillFrame!.Modulate = new Color(1, 1, 1, shown);
    }

    /// <summary>Images that could not be read, so a missing or broken one is not tried again every frame.</summary>
    private readonly HashSet<string> _unreadable = new();

    /// <summary>
    /// An image fetched for the film, by its key, read by what its bytes are rather than its name: the image API answers
    /// PNG for a transparent figure and JPEG (or WebP) for an opaque scene, all kept under <c>.png</c> keys.
    /// </summary>
    private Image? Load(string key)
    {
        if (_imagesDir is null || _unreadable.Contains(key)) return null;
        var path = System.IO.Path.Combine(_imagesDir, System.IO.Path.GetFileName(key));
        var bytes = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : [];
        var image = new Image();
        var error = bytes switch
        {
            [0x89, 0x50, 0x4e, 0x47, ..] => image.LoadPngFromBuffer(bytes),
            [0xff, 0xd8, ..] => image.LoadJpgFromBuffer(bytes),
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => image.LoadWebpFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        if (error == Error.Ok && !image.IsEmpty()) return image;
        GD.PushWarning($"film: the image {key} could not be read ({error}); it is left out.");
        _unreadable.Add(key);
        return null;
    }

    private static Control Spacer(string name) => new() { Name = name, CustomMinimumSize = new Vector2(ImageWidth, ImageHeight) };

    /// <summary>
    /// A side's image, if it was fetched: the figure or banner standing in its box, fading in with the card. Once the fight
    /// is fought, its fate is stamped across the image's foot (the owner, 2026-10-29), and a slain duellist is greyed.
    /// </summary>
    private Control? Likeness(FaceOffSide side, string name)
    {
        var image = _imagesDir is not null && side.Image is { Length: > 0 } key ? Load(key) : null;
        // No painting (none made yet, the image budget spent, or not fetched): a framed stand-in in the side's colours, so
        // the card keeps its two flanks (the owner, 2026-10-29: "fix layout").
        Control rect = image is not null
            ? new TextureRect
            {
                Name = name, Texture = ImageTexture.CreateFromImage(image), CustomMinimumSize = new Vector2(ImageWidth, ImageHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                Material = side.Fate == Fates.Slain ? new ShaderMaterial { Shader = new Shader { Code = GreyShader } } : null,
            }
            : StandIn(side, name);
        Control shown = rect;
        if (side.Fate is { Length: > 0 } fate)
        {
            shown = new Control { Name = name, CustomMinimumSize = rect.CustomMinimumSize, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            rect.Name = "Picture";
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            shown.AddChild(rect);
            shown.AddChild(Stamp(fate));
        }
        shown.Modulate = new Color(1, 1, 1, 0);
        _likenesses.Add(shown);
        return shown;
    }

    /// <summary>
    /// A side without its painting: a dark panel framed in its colour, its class's emblem large (a realm's banner colour
    /// alone for a war), its first letter beneath; greyed for the dead like a painting.
    /// </summary>
    private Control StandIn(FaceOffSide side, string name)
    {
        var colour = Ui.C(Client.WarLayout.Colour(side.Colour));
        var panel = new PanelContainer
        {
            Name = name, CustomMinimumSize = new Vector2(ImageWidth, ImageHeight), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = colour.Darkened(0.72f), BorderColor = colour,
            BorderWidthLeft = 6, BorderWidthRight = 6, BorderWidthTop = 6, BorderWidthBottom = 6,
            CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14,
        });
        var column = Ui.Column("Column", 18);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        if (side.Class is { Length: > 0 } k) column.AddChild(Emblem("Class", Classes.Base(k), ImageWidth * 0.55f));
        var initial = Ui.Line(side.Name.Length > 0 ? side.Name[..1].ToUpperInvariant() : "?", "Initial", null, 150);
        initial.AddThemeColorOverride("font_color", colour.Lightened(0.35f));
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(initial);
        panel.AddChild(column);
        if (side.Fate == Fates.Slain)
        {
            panel.SelfModulate = new Color(0.55f, 0.55f, 0.55f);
            column.Modulate = new Color(0.5f, 0.5f, 0.5f);
        }
        return panel;
    }

    /// <summary>A picture drained of colour and darkened a little, for the dead.</summary>
    private const string GreyShader = """
        shader_type canvas_item;
        void fragment() {
            vec4 c = texture(TEXTURE, UV);
            float grey = dot(c.rgb, vec3(0.299, 0.587, 0.114)) * 0.85;
            COLOR = vec4(vec3(grey), c.a);
        }
        """;

    /// <summary>A side's fate as a stamp, tilted across the foot of its image: gold for the victor, red for the fallen.</summary>
    private Control Stamp(string fate)
    {
        var colour = fate is Fates.Victor or Fates.Victorious ? new Color("b8860b")
            : fate is Fates.Slain or Fates.Defeated ? new Color("9b1c1c") : new Color("5b4a36");
        var stamp = new PanelContainer { Name = "Stamp" };
        stamp.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1, 0.98f, 0.94f, 0.88f), BorderColor = colour,
            BorderWidthLeft = 5, BorderWidthRight = 5, BorderWidthTop = 5, BorderWidthBottom = 5,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var label = Ui.Line(fate.ToUpperInvariant(), "Fate", null, 44);
        label.AddThemeColorOverride("font_color", colour);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stamp.AddChild(label);
        stamp.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        (stamp.GrowHorizontal, stamp.GrowVertical) = (Control.GrowDirection.Both, Control.GrowDirection.Begin);
        stamp.OffsetBottom = -ImageHeight * 0.1f;
        stamp.Rotation = Mathf.DegToRad(-10);
        stamp.Resized += () => stamp.PivotOffset = stamp.Size / 2;
        return stamp;
    }

    private PanelContainer FaceOffCard(FaceOff f)
    {
        var card = Ui.Card("FaceOff", _vertical ? TallFaceOffWidth : FaceOffWidth);
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
            var band = new ColorRect { Name = "Band", Color = Of(side), CustomMinimumSize = new Vector2(Px(8), Px(26)) };
            var title = Ui.Line(side.Name, "Name", Palette.Ink, Px(22));
            // A duellist's class: its emblem by the name, its title first among the notes.
            var emblem = side.Class is { Length: > 0 } k ? Emblem("Class", Classes.Base(k), Px(40)) : null;
            var parts = new Control?[] { band, emblem, title };
            foreach (var part in right ? parts.Reverse() : parts)
                if (part is not null) line.AddChild(part);
            box.AddChild(line);
            var said = side.Class is { Length: > 0 } calling ? side.Notes.Prepend(Classes.Title(calling)) : side.Notes;
            // Wrapped in the Short, where the bigger type would not fit on one line.
            // Wrapped, so a long line of notes never runs under the VS between the names.
            var notes = Ui.Body(string.Join(" · ", said), "Notes", Palette.Muted, _vertical ? Px(13) : 13);
            notes.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            box.AddChild(notes);
            return box;
        }
        names.AddChild(Name(f.A, false));
        // Between them, the VS emblem over the moment: the challenge, the clash under way, or the verdict.
        var middle = Ui.Column("Middle", 4);
        middle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // In the Short the VS already stands between the two images above the card: the card does without its own.
        if (!(_vertical && (f.A.Image is { Length: > 0 } || f.B.Image is { Length: > 0 }))) middle.AddChild(Versus("Versus", _vertical ? 120 : 84));
        var moment = f.Stage switch { FaceOff.Fought => "The verdict", FaceOff.UnderWay => "The clash rages", _ => "The challenge" };
        var kicker = Kicker(f.Kind == FaceOff.Duel || f.Stage != FaceOff.Challenge ? $"{moment} · {f.Stakes}" : "War is declared");
        kicker.HorizontalAlignment = HorizontalAlignment.Center;
        middle.AddChild(kicker);
        // Which calling counters the other's, and what it is worth.
        var (ka, kb) = (f.A.Class, f.B.Class);
        if ((Classes.Counters(ka, kb) ? (ka, kb) : Classes.Counters(kb, ka) ? (kb, ka) : (null, null)) is ({ } win, { } lose))
        {
            var edge = Ui.Line($"{Classes.Title(win)} counters {Classes.Title(lose)} · +15%", "Edge", Client.WarLayout.Colour((win == ka ? f.A : f.B).Colour), Px(14));
            edge.HorizontalAlignment = HorizontalAlignment.Center;
            middle.AddChild(edge);
        }
        // In the Short the middle stands on its own line above the names, so the bigger names have the width.
        if (_vertical)
        {
            middle.Alignment = BoxContainer.AlignmentMode.Center;
            column.AddChild(middle);
        }
        else names.AddChild(middle);
        names.AddChild(Name(f.B, true));
        column.AddChild(names);

        // Each figure: A's bar grows left from the name, B's grows right, both on the figure's own scale.
        for (var i = 0; i < f.A.Stats.Count && i < f.B.Stats.Count; i++)
        {
            var (a, b) = (f.A.Stats[i], f.B.Stats[i]);
            // A figure neither side has (no relics on either) says nothing: left out.
            if (a.Value == 0 && b.Value == 0) continue;
            var row = Ui.Row($"Stat{i}", 10);
            row.AddChild(Figure(a.Value, true));
            row.AddChild(Bar(a.Value / Math.Max(1, a.Max), Of(f.A), true));
            var label = Ui.Line(a.Name, "Name", Palette.Body, Px(14));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.CustomMinimumSize = new Vector2(_vertical ? 190 : 120, 0);
            row.AddChild(label);
            row.AddChild(Bar(b.Value / Math.Max(1, b.Max), Of(f.B), false));
            row.AddChild(Figure(b.Value, false));
            column.AddChild(row);
        }

        // The odds: one bar, the first side's share in its colour, the rest in the other's.
        var odds = Ui.Row("Odds", 10);
        odds.AddChild(Figure(f.OddsA, true, "%"));
        var split = Ui.Row("Split", 0);
        var full = Reach * 2 + (_vertical ? 210 : 140);
        split.CustomMinimumSize = new Vector2(full, Px(14));
        var left = new ColorRect { Name = "A", Color = Of(f.A), CustomMinimumSize = new Vector2(1, Px(14)) };
        var right = new ColorRect { Name = "B", Color = Of(f.B), CustomMinimumSize = new Vector2(1, Px(14)) };
        split.AddChild(left);
        split.AddChild(right);
        _bars.Add((left, full * f.OddsA / 100f));
        _bars.Add((right, full * (100 - f.OddsA) / 100f));
        odds.AddChild(split);
        odds.AddChild(Figure(100 - f.OddsA, false, "%"));
        // A fought fight's bar is all the victor's; one under way still shows the chance reckoned at its challenge.
        var chance = Kicker(f.Stage switch { FaceOff.Fought => "Victory", FaceOff.UnderWay => "The odds, as they stood at the challenge", _ => "Who will prevail?" });
        chance.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(chance);
        column.AddChild(odds);
        if (f.Result is { Length: > 0 } result)
        {
            var told = Ui.Line(char.ToUpperInvariant(result[0]) + result[1..], "Result", Palette.Ink, Px(17));
            told.HorizontalAlignment = HorizontalAlignment.Center;
            told.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(told);
        }
        return card;
    }

    private Label Figure(double value, bool right, string unit = "")
    {
        var label = Ui.Line($"{FaceOffStat.Compact(value)}{unit}", "Figure", Palette.Ink, Px(15));
        label.CustomMinimumSize = new Vector2(Px(52), 0);
        label.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        return label;
    }

    /// <summary>A bar of up to <see cref="BarReach"/> (<see cref="TallBarReach"/> in the Short), growing towards the middle's figure name from its own side.</summary>
    private Control Bar(double share, Color colour, bool fromRight)
    {
        var track = Ui.Row("Track", 0);
        track.CustomMinimumSize = new Vector2(Reach, Px(12));
        track.Alignment = fromRight ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
        var bar = new ColorRect { Name = "Bar", Color = colour, CustomMinimumSize = new Vector2(1, Px(12)), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        track.AddChild(bar);
        _bars.Add((bar, Reach * (float)Math.Clamp(share, 0, 1)));
        return track;
    }

    private static int IndexOf(IReadOnlyList<FilmShot> shots, FilmShot shot)
    {
        for (var i = 0; i < shots.Count; i++)
            if (ReferenceEquals(shots[i], shot)) return i;
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

    /// <summary>
    /// When each of a section's moves is told: spread evenly over the section (or over its second half, after its still),
    /// the first after a pause, the last well before the next section.
    /// </summary>
    public static IReadOnlyList<double> MoveTimes(IReadOnlyList<FilmShot> shots, int index)
    {
        var shot = shots[index];
        if (shot.Moves is not { Count: > 0 } moves) return [];
        var (from, end) = (StillUntil(shots, index) ?? shot.Start, ResultFrom(shots, index) ?? SectionEnd(shots, index));
        return moves.Select((_, k) => from + (end - from) * (k + 1) / (moves.Count + 1)).ToList();
    }

    /// <summary>
    /// When a fight's face-off (its result, or its course so far; the owner, 2026-10-28) comes up: over the section's last
    /// two fifths, after its still and moves; null for a section without one. A challenge's card holds the whole section.
    /// </summary>
    public static double? ResultFrom(IReadOnlyList<FilmShot> shots, int index)
    {
        var shot = shots[index];
        if (shot.FaceOff is not { } f || f.Stage == FaceOff.Challenge) return null;
        return shot.Start + (SectionEnd(shots, index) - shot.Start) * 0.6;
    }

    private static double CardFrom(IReadOnlyList<FilmShot> shots, int index) => ResultFrom(shots, index) ?? shots[index].Start;

    private static double SectionEnd(IReadOnlyList<FilmShot> shots, int index) =>
        index + 1 < shots.Count ? shots[index + 1].Start : shots[index].Start + LastSectionSeconds;

    /// <summary>When a section's still comes up: a few seconds in, once the camera has settled on the place.</summary>
    public static double StillFrom(IReadOnlyList<FilmShot> shots, int index)
    {
        var start = shots[index].Start;
        var until = StillUntil(shots, index) ?? SectionEnd(shots, index);
        return start + Math.Min(StillDelaySeconds, (until - start) / 2);
    }

    /// <summary>
    /// When a section's still gives way to the map: at its end, or halfway when the section also tells a war's moves (the
    /// still first, then the moves on the map); null without a still.
    /// </summary>
    public static double? StillUntil(IReadOnlyList<FilmShot> shots, int index)
    {
        var shot = shots[index];
        if (shot.Still is not { Length: > 0 }) return null;
        var end = ResultFrom(shots, index) ?? SectionEnd(shots, index);
        return shot.Moves is { Count: > 0 } ? shot.Start + (end - shot.Start) / 2 : end;
    }

    /// <summary>
    /// The log's lines: every section's title but the opening's (it names the world), each followed by its moves (at the
    /// times their arrows are drawn) or, for a fight, its days, spread over the section like the moves.
    /// </summary>
    private static IEnumerable<(double At, string Text, bool Move)> LogLines(IReadOnlyList<FilmShot> shots)
    {
        for (var i = 1; i < shots.Count; i++)
        {
            if (shots[i].Title.Length > 0) yield return (shots[i].Start, shots[i].Title, false);
            var times = MoveTimes(shots, i);
            for (var k = 0; k < times.Count; k++) yield return (times[k], shots[i].Moves![k].Line, true);
            if (times.Count > 0 || shots[i].Beats is not { Count: > 1 } beats) continue;
            var (from, end) = (shots[i].Start, SectionEnd(shots, i));
            for (var k = 0; k < beats.Count; k++)
                yield return (from + (end - from) * (k + 1) / (beats.Count + 1), $"Day {beats[k].Day}: {FirstSentence(beats[k].Text)}", true);
        }
    }

    /// <summary>A day's account cut to its first sentence, for a line of the log.</summary>
    private static string FirstSentence(string text)
    {
        var end = text.IndexOfAny(['.', '!', '?']);
        var first = end > 0 ? text[..(end + 1)] : text;
        return first.Length <= 110 ? first : first[..107].TrimEnd() + "…";
    }

    /// <summary>The log holds a line for every section begun by <paramref name="t"/>, and for every move told by then.</summary>
    private void Log(IReadOnlyList<FilmShot> shots, double t)
    {
        for (; _logged < _lines.Count && _lines[_logged].At <= t; _logged++)
        {
            var (_, text, move) = _lines[_logged];
            _log.AddChild(move ? Ui.Body($"· {text}", $"Line{_logged}", Palette.Muted, 12) : Ui.Body(text, $"Line{_logged}", Palette.Body, 14));
        }
        _logCard.Visible = _logged > 0 && !_vertical;
        if (_caption is null) return;
        // The vertical film's caption: the title of the section told now.
        var shot = ShotAt(shots, t);
        var index = IndexOf(shots, shot);
        _caption.Text = shot.Title;
        // Shown while the map is: after the section's still, before its face-off card comes up.
        var (shown, hidden) = (StillUntil(shots, index) ?? shot.Start, shot.FaceOff is null ? double.MaxValue : CardFrom(shots, index));
        _caption.GetParent().GetParent<Control>().Visible = shot.Title.Length > 0 && t >= shown && t < hidden;
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

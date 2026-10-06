using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The root of the game (sub-project 4 §2): opens or creates the world, catches up on a worker thread with progress,
/// then keeps it live, and hosts the diorama and the screens. The world file comes from `-- --world &lt;path&gt;`, else
/// user://worlds/world.fw.db (created as a medium world with a century of prehistory on first launch).
/// </summary>
public partial class Main : Control
{
    public const double LiveInterval = 2.0;

    private Session? _session;
    private double _sinceStep;
    private bool _stepping;
    private bool _liveStopped;
    private readonly System.Threading.CancellationTokenSource _closing = new();

    public Session? Session => _session;

    public ScreenRouter Router { get; } = new();

    /// <summary>One line of state: creating, catching up, caught up.</summary>
    public Label Status { get; private set; } = null!;

    public bool CaughtUp { get; private set; }

    public Diorama Diorama { get; private set; } = null!;

    /// <summary>The layer the current screen's controls live in, above the diorama.</summary>
    public Control Screens { get; private set; } = null!;

    public Panel StatusLight { get; private set; } = null!;

    private ImageTexture? _miniMap;

    /// <summary>The painted world map for page insets, made once per world.</summary>
    public ImageTexture? MiniMap()
    {
        if (_miniMap is null && Geography is { } geography)
        {
            const int size = 192;
            _miniMap = ImageTexture.CreateFromImage(Image.CreateFromData(size, size, false, Image.Format.Rgba8, Client.MiniMap.Pixels(geography.Terrain, size)));
        }
        return _miniMap;
    }

    public Geography? Geography { get; private set; }

    /// <summary>The digest built when the world caught up (it is marked seen at once, so it is kept, not rebuilt).</summary>
    public DigestScreen? Digest { get; private set; }

    /// <summary>The control showing the current screen, if that screen has one yet.</summary>
    public Control? Current { get; private set; }

    /// <summary>Lets a test (or another host) supply an open session before the node enters the tree.</summary>
    public void Use(Session session) => _session = session;

    public override void _Ready()
    {
        Name = "Main";
        Theme = Ui.CreateTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // The diorama draws behind every card; the root lets clicks it does not use fall through to it.
        MouseFilter = MouseFilterEnum.Ignore;
        Diorama = new Diorama();
        AddChild(Diorama);
        // A soft wash of the ground colour behind the left-hand text, so titles stay legible over busy terrain.
        var wash = new Gradient();
        wash.SetColor(0, Ui.C(Palette.Ground, 0.9f));
        wash.SetColor(1, Ui.C(Palette.Ground, 0));
        wash.AddPoint(0.55f, Ui.C(Palette.Ground, 0.6f));
        var scrim = new TextureRect
        {
            Name = "Scrim", MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = new GradientTexture2D { Gradient = wash, Width = 256, Height = 4, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 0) },
        };
        scrim.AnchorBottom = 1;
        (scrim.OffsetLeft, scrim.OffsetRight) = (0, 560);
        AddChild(scrim);
        Screens = new Control { Name = "Screens", MouseFilter = MouseFilterEnum.Ignore };
        Screens.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Screens);
        var pill = Ui.Capsule("StatusPill", Ui.C(Palette.Card), Ui.C(Palette.Border), 14, 8);
        Ui.Place(pill, 1, 1, -40, -40);
        var row = Ui.Row("Row", 8);
        StatusLight = Ui.Dot(Palette.Muted, 8, "Light");
        row.AddChild(StatusLight);
        Status = Ui.Body("Opening…", "Status", Palette.Body, 12);
        Status.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(Status);
        pill.AddChild(row);
        AddChild(pill);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            if (_session is null)
            {
                var path = WorldPath();
                if (!System.IO.File.Exists(path))
                {
                    Status.Text = "Creating your world…";
                    _session = await Task.Run(() => Session.Create(path, (ulong)DateTimeOffset.UtcNow.UtcTicks, WorldSize.Medium, () => DateTimeOffset.UtcNow));
                }
                else _session = await Task.Run(() => Session.Open(path, () => DateTimeOffset.UtcNow));
            }
            var progress = new Progress<WorldDate>(date =>
            {
                if (IsInsideTree()) Status.Text = $"Catching up… {DigestScreen.StatusLine(date, 0)["Caught up · ".Length..]}";
            });
            // Catch-up comes in chunks of at most 30 world years; run them all before the world is shown.
            while ((await _session.CatchUpAsync(progress, _closing.Token)).DaysRemaining > 0) { }
            if (!IsInsideTree()) return;
            await OnCaughtUpAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (!IsInsideTree()) return;
            Status.Text = $"Could not open the world: {e.Message}";
            GD.PushError(e.ToString());
        }
    }

    /// <summary>
    /// The world is current: build the diorama and the digest on a worker (the digest may ask the LLM to narrate),
    /// then show them. Only Godot objects are made on the main thread.
    /// </summary>
    private async Task OnCaughtUpAsync()
    {
        var session = _session!;
        Status.Text = "Setting out the world…";
        var (geography, today, parts, digest) = await Task.Run(() =>
        {
            var geography = session.Read((w, _) => w.Geography);
            var today = session.Map();
            var parts = geography is null ? null : Diorama.Prepare(geography, today.Settlements.Where(s => s.Position is not null)
                .Select(s => Client.Projection.ToBlock(geography.Terrain, s.Position!, 0)).ToList());
            var digest = session.Digest();
            session.MarkSeen();
            return (geography, today, parts, digest);
        });
        if (!IsInsideTree()) return;
        Geography = geography;
        if (geography is not null && parts is not null)
        {
            Diorama.Build(geography, parts);
            Diorama.Show(today);
        }
        Digest = digest;
        Diorama.Picked += Picked;
        Router.Changed += Show;
        CaughtUp = true;
        UpdateStatus();
        Show(Router.Current);
    }

    /// <summary>A click on the diorama: a battle opens its why chain, a host its commander, anything else its page.</summary>
    private void Picked(FantasyWorld.Sim.Events.EntityRef picked)
    {
        if (picked.Kind == "event") Router.Explain(picked.Id);
        else if (picked.Kind == Army.Kind)
        {
            if (Diorama.War.Armies.FirstOrDefault(a => a.Ref == picked)?.Commander is { } commander) Router.Open(commander.Ref);
        }
        else Router.Open(picked);
    }

    /// <summary>Replaces the current screen's controls (and the diorama's pins) for a screen.</summary>
    public void Show(Screen screen)
    {
        Ui.Clear(Screens);
        // Only the map travels in time; every other screen shows today's world.
        if (screen.Kind != ScreenKind.Map && Diorama.ShownYear is not null) Diorama.Show(_session!.Map());
        Diorama.ShowPins(screen.Kind == ScreenKind.Digest && Digest is not null && Geography is not null
            ? Markers.Pins(Digest, Geography) : Array.Empty<Pin>());
        try
        {
            Current = Build(screen);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // The page's subject is gone (an old minor event compacted away, a stale link): say so and stay put.
            GD.PushWarning($"Could not open {screen}: {e.Message}");
            Current = null;
            Toast("That page is no longer in the chronicle.", 0);
            if (Router.CanGoBack) Callable.From(() => Router.Back()).CallDeferred();
        }
        // Pages cover the diorama; there is no need to draw it underneath them.
        Diorama.Visible = screen.Kind is ScreenKind.Digest or ScreenKind.Map || Current is null;
        if (Current is not null) Screens.AddChild(Current);
    }

    private Control? Build(Screen screen) =>
        screen.Kind switch
        {
            ScreenKind.Digest when Digest is not null => new DigestView { Host = this, Digest = Digest },
            ScreenKind.Map => new MapHudView { Host = this, Focus = screen.Focus },
            ScreenKind.Character => new CharacterView { Host = this, Character = _session!.Character(screen.Id) },
            ScreenKind.Family => new FamilyView { Host = this, Tree = _session!.Family(screen.Id) },
            ScreenKind.Why => new WhyView { Host = this, Why = _session!.Why(screen.Id) },
            ScreenKind.Realm => new RealmView { Host = this, Realm = _session!.Realm(screen.Id) },
            ScreenKind.Artifact => new ArtifactView { Host = this, Artifact = _session!.Artifact(screen.Id) },
            ScreenKind.Chronicle => new ChronicleView { Host = this, Only = screen.Focus is { Kind: "war" } ? Category.War : null },
            _ => null,
        };

    public override void _Process(double delta)
    {
        if (!CaughtUp || _session is null) return;
        _sinceStep += delta;
        if (_sinceStep < LiveInterval) return;
        _sinceStep = 0;
        if (_stepping || _liveStopped) return;
        _ = StepLiveAsync();
    }

    /// <summary>
    /// One live step on a worker (never more than one at a time): the simulation, any LLM calls and the save stay off
    /// the main thread. Then the status, today's diorama and the map follow, and the day's big news is announced.
    /// </summary>
    private async Task StepLiveAsync()
    {
        var session = _session!;
        _stepping = true;
        try
        {
            var (days, map, news) = await Task.Run(() =>
            {
                var tick = session.Step();
                if (tick.Days == 0) return (0L, (MapScreen?)null, ((string, long)?)null);
                var top = tick.Events.Where(e => e.Importance >= LiveToastImportance).MaxBy(e => e.Importance);
                var line = top is null ? null : session.Read((w, _) => new FantasyWorld.Sim.Narration.TemplateNarrator(w).Sentence(top));
                return (tick.Days, session.Map(), top is null ? null : ((string, long)?)(line!, top.Id));
            });
            if (!IsInsideTree() || days == 0) return;
            UpdateStatus();
            // The world moved on: today's diorama follows it (a past map on the history slider stays put).
            if (map is not null && Diorama.ShownYear is null && Router.Current.Kind is ScreenKind.Digest or ScreenKind.Map)
            {
                Diorama.Show(map);
                (Current as MapHudView)?.Today(map);
            }
            if (news is { } n) Toast(n.Item1, n.Item2);
        }
        catch (Exception e)
        {
            // A save refused (another session changed the file) or a failing provider: stop living rather than lose work.
            _liveStopped = true;
            if (!IsInsideTree()) return;
            Status.Text = $"Live play paused: {e.Message}";
            StatusLight.AddThemeStyleboxOverride("panel", Ui.Round(Ui.C(Palette.Conflict), null));
            GD.PushError(e.ToString());
        }
        finally
        {
            _stepping = false;
        }
    }

    /// <summary>Events at least this important are announced as they happen while the game is open.</summary>
    public const int LiveToastImportance = 40;

    public PanelContainer? CurrentToast { get; private set; }

    /// <summary>A short-lived notice at the bottom of the screen; clicking it opens the event's why chain.</summary>
    public void Toast(string text, long eventId)
    {
        if (CurrentToast is { } old && IsInstanceValid(old)) old.QueueFree();
        var toast = Ui.Capsule("Toast", Ui.C(Palette.Ink), null, 18, 10);
        var row = Ui.Row("Row", 10);
        row.AddChild(Ui.Dot(Palette.Conflict, 8));
        row.AddChild(Ui.Line(text, "Text", Palette.Card, 13));
        toast.AddChild(row);
        toast.MouseDefaultCursorShape = CursorShape.PointingHand;
        toast.GuiInput += input =>
        {
            if (eventId != 0 && input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Router.Explain(eventId);
        };
        AddChild(toast);
        toast.AnchorLeft = toast.AnchorRight = 0.5f;
        toast.AnchorTop = toast.AnchorBottom = 1;
        toast.GrowHorizontal = GrowDirection.Both;
        toast.GrowVertical = GrowDirection.Begin;
        (toast.OffsetTop, toast.OffsetBottom) = (-40, -40);
        CurrentToast = toast;
        var fade = toast.CreateTween();
        fade.TweenInterval(6);
        fade.TweenProperty(toast, "modulate:a", 0f, 0.6f);
        fade.TweenCallback(Callable.From(() =>
        {
            if (CurrentToast == toast) CurrentToast = null;
            if (IsInstanceValid(toast)) toast.QueueFree();
        }));
    }

    /// <summary>Esc or Backspace goes back, H goes home to the digest, M opens the map.</summary>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!CaughtUp || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.CtrlPressed || key.AltPressed || key.MetaPressed || key.ShiftPressed) return;
        switch (key.Keycode)
        {
            case Key.Escape or Key.Backspace:
                Router.Back();
                break;
            case Key.H:
                Router.Home();
                break;
            case Key.M:
                Router.Go(new Screen(ScreenKind.Map));
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void UpdateStatus()
    {
        if (_session?.TryRead((w, _) => DigestScreen.StatusLine(w.Date, _session.DaysBehind)) is { } line)
        {
            Status.Text = line;
            StatusLight.AddThemeStyleboxOverride("panel", Ui.Round(Ui.C(_session.DaysBehind > 0 ? Palette.Conflict : Palette.Realm), null));
        }
    }

    /// <summary>Closing stops a catch-up in progress (the world keeps the days it simulated) and saves.</summary>
    public override void _ExitTree()
    {
        _closing.Cancel();
        try
        {
            _session?.Dispose();
        }
        catch (Exception e)
        {
            GD.PushError($"Could not save the world on exit: {e.Message}");
        }
    }

    private static string WorldPath()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--world");
        return at >= 0 && at + 1 < args.Length
            ? args[at + 1]
            : System.IO.Path.Combine(OS.GetUserDataDir(), "worlds", "world.fw.db");
    }
}

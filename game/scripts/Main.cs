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

    public Session? Session => _session;

    public ScreenRouter Router { get; } = new();

    /// <summary>One line of state: creating, catching up, caught up.</summary>
    public Label Status { get; private set; } = null!;

    public bool CaughtUp { get; private set; }

    public Diorama Diorama { get; private set; } = null!;

    /// <summary>The layer the current screen's controls live in, above the diorama.</summary>
    public Control Screens { get; private set; } = null!;

    public Panel StatusLight { get; private set; } = null!;

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
                else _session = Session.Open(path, () => DateTimeOffset.UtcNow);
            }
            var progress = new Progress<WorldDate>(date => Status.Text = $"Catching up… {date}");
            await _session.CatchUpAsync(progress);
            OnCaughtUp();
        }
        catch (Exception e)
        {
            Status.Text = $"Could not open the world: {e.Message}";
            GD.PushError(e.ToString());
        }
    }

    /// <summary>Called once the world is current: subclasses and later tasks show the screens here.</summary>
    protected virtual void OnCaughtUp()
    {
        Geography = _session!.Read((w, _) => w.Geography);
        if (Geography is not null)
        {
            var today = _session.Map();
            var clearings = today.Settlements.Where(s => s.Position is not null)
                .Select(s => Client.Projection.ToBlock(Geography.Terrain, s.Position!, 0));
            Diorama.Build(Geography, clearings);
            Diorama.Show(today);
        }
        Digest = _session.Digest();
        _session.MarkSeen();
        Diorama.Picked += Router.Open;
        Router.Changed += Show;
        CaughtUp = true;
        UpdateStatus();
        Show(Router.Current);
    }

    /// <summary>Replaces the current screen's controls (and the diorama's pins) for a screen.</summary>
    public void Show(Screen screen)
    {
        Ui.Clear(Screens);
        // Only the map travels in time; every other screen shows today's world.
        if (screen.Kind != ScreenKind.Map && Diorama.ShownYear is not null) Diorama.Show(_session!.Map());
        Diorama.ShowPins(screen.Kind == ScreenKind.Digest && Digest is not null && Geography is not null
            ? Markers.Pins(Digest, Geography) : Array.Empty<Pin>());
        Current = screen.Kind switch
        {
            ScreenKind.Digest when Digest is not null => new DigestView { Host = this, Digest = Digest },
            ScreenKind.Map => new MapHudView { Host = this, Focus = screen.Focus },
            _ => null,
        };
        if (Current is not null) Screens.AddChild(Current);
    }

    public override void _Process(double delta)
    {
        if (!CaughtUp || _session is null) return;
        _sinceStep += delta;
        if (_sinceStep < LiveInterval) return;
        _sinceStep = 0;
        if (_session.Step().Days > 0) UpdateStatus();
    }

    public void UpdateStatus()
    {
        if (_session?.TryRead((w, _) => DigestScreen.StatusLine(w.Date, _session.DaysBehind)) is { } line)
        {
            Status.Text = line;
            StatusLight.AddThemeStyleboxOverride("panel", Ui.Round(Ui.C(_session.DaysBehind > 0 ? Palette.Conflict : Palette.Realm), null));
        }
    }

    public override void _ExitTree() => _session?.Dispose();

    private static string WorldPath()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--world");
        return at >= 0 && at + 1 < args.Length
            ? args[at + 1]
            : System.IO.Path.Combine(OS.GetUserDataDir(), "worlds", "world.fw.db");
    }
}

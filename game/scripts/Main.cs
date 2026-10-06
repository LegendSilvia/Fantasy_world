using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
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

    public bool Ready { get; private set; }

    /// <summary>Lets a test (or another host) supply an open session before the node enters the tree.</summary>
    public void Use(Session session) => _session = session;

    public override void _Ready()
    {
        Name = "Main";
        Theme = Ui.CreateTheme();
        SetAnchorsPreset(LayoutPreset.FullRect);
        var ground = new ColorRect { Name = "Ground", Color = Ui.C(Palette.Ground), MouseFilter = MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(ground);
        Status = Ui.Body("Opening…", "Status", Palette.Body, 12);
        Status.SetAnchorsPreset(LayoutPreset.BottomRight);
        Status.Position = new Vector2(-320, -44);
        AddChild(Status);
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
        Ready = true;
        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        if (!Ready || _session is null) return;
        _sinceStep += delta;
        if (_sinceStep < LiveInterval) return;
        _sinceStep = 0;
        if (_session.Step().Days > 0) UpdateStatus();
    }

    public void UpdateStatus()
    {
        if (_session?.TryRead((w, _) => w.Date.ToString()) is { } date)
            Status.Text = _session.DaysBehind > 0 ? $"Catching up · {_session.DaysBehind} days behind" : $"Caught up · {date}";
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

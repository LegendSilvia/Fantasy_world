using System.Linq;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// Renders a full-size world from the angles a player sees, for visual review and the README:
/// `godot --path game res://tests/Showcase.tscn -- --shots &lt;dir&gt; [--seed n] [--years n]` (needs a display).
/// </summary>
public partial class Showcase : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name) => System.Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var dir = Arg("--shots") ?? System.IO.Path.Combine(OS.GetUserDataDir(), "showcase");
        var seed = ulong.Parse(Arg("--seed") ?? "7");
        var years = int.Parse(Arg("--years") ?? "40");
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "showcase", $"showcase-{System.Guid.NewGuid():N}.fw.db");
        var session = await Task.Run(() => Session.Create(path, seed, WorldSize.Medium, () => System.DateTimeOffset.UtcNow, prehistoryYears: years));
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.Use(session);
        AddChild(main);
        while (!main.CaughtUp) await Frames(1);
        System.IO.Directory.CreateDirectory(dir);

        await Shot(main, dir, "1-digest");
        main.Router.Go(new Screen(ScreenKind.Map));
        await Shot(main, dir, "2-map");
        var city = main.Diorama.Markers.Where(m => m.Shape == MarkerShape.Settlement).MaxBy(m => m.Size)!;
        main.Diorama.Focus(FantasyWorld.Client.Projection.ToMap(city.Position), Zoom.Region);
        await Shot(main, dir, "3-region");
        main.Diorama.Rig.Preset(Zoom.Town);
        await Shot(main, dir, "4-town");
        main.Diorama.Rig.Reset();
        main.Diorama.Rig.Drag(-260, -90);
        await Shot(main, dir, "5-low-angle");

        var notable = session.Read((w, l) => w.Characters.Values.Where(c => c.IsActive).MaxBy(c => l.Involving(c.Ref).Count())!.Id);
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("char", notable));
        await Shot(main, dir, "6-character", 10);
        main.Router.Go(new Screen(ScreenKind.Family, notable));
        await Shot(main, dir, "7-family", 10);
        var war = session.Read((w, l) => l.All.Last(e => e.Type == "WarDeclared").Id);
        main.Router.Explain(war);
        await Shot(main, dir, "8-why", 10);

        main.QueueFree();
        await Frames(2);
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GD.Print($"SHOWCASE SAVED TO {dir}");
        GetTree().Quit();
    }

    private async Task Shot(Main main, string dir, string name, int frames = 150)
    {
        await Frames(frames);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name + ".png"));
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

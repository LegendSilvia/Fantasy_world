using System.Linq;
using System.Threading.Tasks;
using FantasyWorld.Client;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;
using Godot;

namespace FantasyWorld.Game.Tests;

/// <summary>
/// A short film of war on the diorama, for review: record with
/// `godot --path game --write-movie war.avi --fixed-fps 30 res://tests/WarMovie.tscn` (needs a display). A host marches
/// two regions along its road toward the war's target while the camera turns slowly.
/// </summary>
public partial class WarMovie : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var path = System.IO.Path.Combine(OS.GetUserDataDir(), "movie", $"movie-{System.Guid.NewGuid():N}.fw.db");
        var session = await Task.Run(() => Session.Create(path, 7, WorldSize.Medium, () => System.DateTimeOffset.UtcNow, prehistoryYears: 40));
        // A host three regions from an enemy region it means to take.
        var army = session.Read((world, _) =>
        {
            var graph = world.Geography!.Graph;
            foreach (var home in world.Regions.Values.Where(r => r.OwnerRealmId != 0).OrderBy(r => r.Id))
                foreach (var target in world.Regions.Values.Where(r => r.OwnerRealmId != 0 && r.OwnerRealmId != home.OwnerRealmId).OrderBy(r => r.Id))
                    if (graph.ShortestPath(home.Id, target.Id) is { } route && route.Path.Count == 4)
                    {
                        var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
                        var war = new War { Id = world.Ids.Take(War.Kind), Name = "The Film War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id } };
                        world.Wars[war.Id] = war;
                        var host = new Army
                        {
                            Id = world.Ids.Take(Army.Kind), Name = $"The Host of {a.Name}", RealmId = a.Id, WarId = war.Id, Levies = 2400, MenAtArms = 300,
                            CommanderId = world.Characters.Values.First(c => c.IsActive).Id, RegionId = home.Id, TargetRegionId = target.Id,
                            Objective = ArmyObjective.Siege,
                        };
                        host.Path.AddRange(route.Path.Skip(1));
                        world.Armies[host.Id] = host;
                        return host;
                    }
            throw new System.InvalidOperationException("No road for the film.");
        });
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.Use(session);
        AddChild(main);
        while (!main.CaughtUp) await Frames(1);
        main.Router.Go(new Screen(ScreenKind.Map));
        // A film shows the world, not the player's controls.
        if (main.Current is MapHudView hud) hud.ShowsControls = false;
        var at = session.Read((w, _) => FantasyWorld.Sim.Views.MapPoint.Of(w, w.Regions[army.Path[1]].Ref)!);
        main.Diorama.Focus(at, Zoom.Region);
        main.Diorama.Rig.Drag(-80, 30);
        for (var step = 0; step < 2; step++)
        {
            await Turn(main, 75);
            session.Read((_, _) =>
            {
                army.RegionId = army.Path[0];
                army.Path.RemoveAt(0);
                return 0;
            });
            var map = session.Map();
            main.Diorama.Show(map);
            (main.Current as MapHudView)?.Today(map);
        }
        await Turn(main, 105);
        main.QueueFree();
        await Frames(2);
        foreach (var suffix in new[] { "", ".bak1", ".bak2", ".bak3", "-wal", "-shm" })
            if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
        GetTree().Quit();
    }

    /// <summary>Lets frames pass while the camera turns slowly round the war.</summary>
    private async Task Turn(Main main, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            main.Diorama.Rig.Drag(-0.35f, 0); // about 3° a second: a slow turn, not a spin
            await Frames(1);
        }
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

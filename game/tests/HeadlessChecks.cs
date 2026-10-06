using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
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
        await DigestChecks(main);
        await MapChecks(main);
    }

    private async Task MapChecks(Main main)
    {
        main.Diorama.Rig.Reset();
        main.Router.Go(new Screen(ScreenKind.Map));
        await Frames(30);
        var view = main.Current as MapHudView;
        Check(view is not null, "the map screen opens");
        if (view is null) return;
        var map = view.Map;
        Check(view.GetNode<Label>("Intro/Title").Text == $"{map.WorldName}.", "the map is titled with the world's name");
        Check(view.GetNode<Label>("Intro/Summary").Text == map.Summary, "and summarised");
        var tagged = map.Settlements.Count(s => MapLabels.Shows(s, view.Host.Diorama.Rig.NearestZoom, main.Diorama.Layers));
        Check(view.TagLayer.GetChildCount() == tagged, $"settlements big enough for the zoom are named ({tagged})");
        Check(view.GetNode("Legend").GetChildCount() == map.Legend.Count, "the legend lists the realms");
        await Shot("map");

        var tags = view.TagLayer.GetChildren().OfType<SettlementTag>().ToList();
        if (tags.Count > 0)
        {
            view.ShowCard(tags[0]);
            Check(view.HoverCard.Visible && view.HoverCard.FindChildren("Title", "Label", true, false).OfType<Label>().Single().Text == tags[0].Settlement.Card.Title,
                "hovering a settlement shows who holds it");
            await Shot("map-card");
        }

        var borders = view.GetNode<CheckBox>("Card/Column/Layers/Borders");
        borders.ButtonPressed = false;
        Check(!main.Diorama.BorderLines.Visible && !main.Diorama.Layers.HasFlag(MapLayers.Borders), "the borders layer can be hidden");
        borders.ButtonPressed = true;
        Check(view.GetNode<CheckBox>("Card/Column/Layers/TradeRoutes").Disabled, "trade routes are not modelled yet");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = false;
        Check(!main.Diorama.SettlementLayer.Visible && view.TagLayer.GetChildren().OfType<SettlementTag>().All(t => t.Settlement.Marker is PinMarker.Conflict or PinMarker.RecentEvent),
            "hiding settlements leaves only places with news");
        view.GetNode<CheckBox>("Card/Column/Layers/Settlements").ButtonPressed = true;

        view.History.Value = 2;
        await Frames(2);
        Check(view.Year == 2 && view.Map.Historical && view.Map.Year == 2 && main.Diorama.ShownYear == 2, "the history slider shows the map of Year 2");
        await Shot("map-year-2");
        view.GetNode<Button>("Card/Column/ActionsRow/Actions/Reset").EmitSignal(BaseButton.SignalName.Pressed);
        Check(view.Year == view.Now && main.Diorama.ShownYear is null && view.History.Value == view.Now, "reset view returns to today");

        view.GetNode<Button>("Card/Column/Zoom/Presets/Region").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(90);
        Check(main.Diorama.Rig.NearestZoom == Zoom.Region && view.GetNode<Label>("Intro/Kicker").Text.EndsWith("REGION VIEW"), "the zoom presets descend");

        view.History.Value = 2;
        var settlement = map.Settlements.First(s => s.Position is not null);
        main.Router.Open(settlement.Ref);
        await Frames(2);
        Check(main.Current is MapHudView { Focus: { } focus } && focus == settlement.Ref, "a linked place opens the map");
        Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Town)
              && Diorama.ToGodot(main.Diorama.Rig.GoalTarget).DistanceTo(Diorama.ToGodot(FantasyWorld.Client.Projection.ToBlock(main.Geography!.Terrain, settlement.Position!, 0))) < 0.01f,
            "and flies to it");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.ShownYear is null, "leaving the map returns the diorama to today");
    }

    private async Task DigestChecks(Main main)
    {
        main.Router.Home();
        main.Diorama.Rig.Reset();
        await Frames(60);
        var view = main.Current as DigestView;
        Check(view is not null, "the digest is the first screen");
        if (view is null) return;
        var digest = main.Digest!;
        Check(view.GetNode<Label>("Intro/Title").Text == digest.Title, "the digest shows the year");
        Check(view.GetNode<Label>("Intro/Subtitle").Text == digest.Subtitle, "and the time away");
        Check(main.Session!.Digest().Items.Count == 0 && main.Session.Digest().Headline is null, "showing the digest marks it seen");
        Check(digest.Headline is null || view.Content.GetNode<RichTextLabel>("Headline/HeadlineTitle").GetParsedText() == digest.Headline.Title.Text,
            "the headline is shown");
        Check(view.Content.GetNodeOrNull("Items")?.GetChildCount() == digest.Items.Count, $"every item is listed ({digest.Items.Count})");
        Check(view.PinLayer.GetChildCount() == digest.Pins.Count && main.Diorama.Pins.Count == digest.Pins.Count, $"one pin per place ({digest.Pins.Count})");
        await Shot("digest");

        if (digest.Pins.Count > 0)
        {
            var pin = view.PinLayer.GetChild<PinTag>(0);
            var expected = main.Diorama.ScreenOf(main.Diorama.Pins[0].Position) - new Godot.Vector2(11, 11);
            Check(pin.Position.DistanceTo(expected) < 1, "pins follow their places on screen");
            view.FlyTo(digest.Pins[0].EventId);
            await Frames(2);
            Check(main.Diorama.Rig.GoalDistance == OrbitRig.DistanceFor(Zoom.Region), "an item flies the camera to its place");
        }

        view.ShowChronicle(true);
        await Frames(2);
        Check(view.Content.GetNodeOrNull("Chronicle") is not null && view.ShowingChronicle, "the toggle shows the full chronicle");
        await Shot("digest-chronicle");
        view.ShowChronicle(false);

        if (digest.Headline is { CanExplain: true } headline)
        {
            view.Content.GetNode<Button>("Headline/Why").EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.Router.Current == new Screen(ScreenKind.Why, headline.EventId), "\"Why did this happen?\" opens the why chain");
            main.Router.Back();
            await Frames(2);
            Check(main.Current is DigestView, "Back returns to the digest");
        }
    }

    private async Task DioramaChecks(Main main)
    {
        var diorama = main.Diorama;
        var map = main.Session!.Map();
        Check(diorama.Block.Mesh is ArrayMesh { } block && block.GetSurfaceCount() == 2, "the block has a top and walls");
        Check(diorama.GetNodeOrNull<MeshInstance3D>("Water")?.Mesh is not null, "the sea is drawn");
        Check(diorama.SettlementLayer.GetChildCount() + diorama.StructureLayer.GetChildCount() == map.Settlements.Count(s => s.Position is not null) + map.Structures.Count(s => s.Position is not null),
            "one marker per settlement and structure");
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

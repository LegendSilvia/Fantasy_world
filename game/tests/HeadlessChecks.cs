using System.Collections.Generic;
using System.Threading.Tasks;
using System;
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
        await PageChecks(main);
        await ReadingChecks(main);
        await WarChecks(main);
    }

    /// <summary>Stages a small war (a host marching across a border into land it occupies) and checks the war layer.</summary>
    private async Task WarChecks(Main main)
    {
        var session = main.Session!;
        var (armyRef, commander) = session.Read((world, log) =>
        {
            var link = world.Geography!.Graph.Links.First(l => world.Regions[l.A].OwnerRealmId != 0 && world.Regions[l.B].OwnerRealmId != 0
                                                             && world.Regions[l.A].OwnerRealmId != world.Regions[l.B].OwnerRealmId);
            var (home, target) = (world.Regions[link.A], world.Regions[link.B]);
            var (a, b) = (world.Realms[home.OwnerRealmId], world.Realms[target.OwnerRealmId]);
            var war = new FantasyWorld.Sim.Model.War { Id = world.Ids.Take("war"), Name = "The Staged War", AttackerId = a.Id, DefenderId = b.Id, Attackers = { a.Id }, Defenders = { b.Id } };
            world.Wars[war.Id] = war;
            var leader = world.Characters.Values.First(c => c.IsActive);
            var army = new FantasyWorld.Sim.Model.Army
            {
                Id = world.Ids.Take("army"), Name = $"The Host of {a.Name}", RealmId = a.Id, WarId = war.Id, CommanderId = leader.Id, Levies = 800,
                RegionId = home.Id, Path = { target.Id }, TargetRegionId = target.Id,
            };
            world.Armies[army.Id] = army;
            target.OccupierRealmId = a.Id;
            var beastId = world.Ids.Take("monster");
            var rose = log.Append(world.Date, new FantasyWorld.Sim.Events.EventDraft("MonsterRose",
                Actors: new Dictionary<string, FantasyWorld.Sim.Events.EntityRef> { ["monster"] = new("monster", beastId) }, Place: home.Ref));
            world.Monsters[beastId] = new FantasyWorld.Sim.Model.Monster
            {
                Id = beastId, Name = "The Wyrm of the Staged War", Breed = "wyrm", RegionId = home.Id, Might = 80, Legendary = true, RoseEventId = rose.Id,
            };
            return (army.Ref, leader.Ref);
        });
        main.Router.Go(new Screen(ScreenKind.Map));
        main.Diorama.Show(session.Map());
        (main.Current as MapHudView)?.Today(session.Map());
        main.Diorama.Rig.Reset();
        await Frames(90);
        var war = main.Diorama.War;
        Check(war.Banners.GetChildCount() == 1 && war.Arrows.GetChildCount() == 1, "a host on the march has a banner and a campaign arrow");
        Check(war.Hatch.Mesh is not null && war.Outline.Mesh is not null, "occupied land is hatched and outlined");
        Check(war.Fronts.Mesh is ArrayMesh { } fronts && fronts.GetSurfaceCount() >= 2, "the front between the sides is drawn with its teeth");
        var tag = (main.Current as MapHudView)?.TagLayer.GetChildren().OfType<ArmyTag>().FirstOrDefault();
        Check(tag is not null && tag.Army.Ref == armyRef, "the host is named on the map");
        Check(main.Diorama.Monsters.Count == 1 && main.Diorama.StructureLayer.GetNodeOrNull("monster" + main.Diorama.Monsters[0].Ref.Id) is not null,
            "a beast stands in its lair");
        await Shot("war");
        var banner = war.BannerAt(armyRef)!.Value;
        var screen = main.Diorama.Camera.UnprojectPosition(banner + new Godot.Vector3(0, WarLayer.BannerHeight * 0.7f, 0));
        Check(main.Diorama.Pick(screen) == armyRef, "clicking a banner picks its host");
        main.Router.Home();
        await Frames(2);
        main.Router.Open(commander);
        await Frames(2);
        Check(main.Current is CharacterView c && c.Character.Ref == commander, "a host's commander has a page");
        main.Router.Home();
    }

    private async Task ReadingChecks(Main main)
    {
        var session = main.Session!;
        var realm = session.Read((w, _) => w.Realms.Keys.First());
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("realm", realm));
        await Frames(2);
        Check(main.Current is RealmView r && r.GetNode<Label>("Left/Title").Text == $"{r.Realm.Title}.", "a realm link opens the realm's page");
        await Shot("realm");
        if (session.Read((w, _) => w.Artifacts.Keys.Cast<long?>().FirstOrDefault()) is { } artifact)
        {
            main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("artifact", artifact));
            await Frames(2);
            Check(main.Current is ArtifactView a && a.GetNode<Label>("Left/Title").Text == $"{a.Artifact.Name}.", "an artifact link opens its page");
        }
        main.Router.Go(new Screen(ScreenKind.Chronicle));
        await Frames(2);
        var chronicle = main.Current as ChronicleView;
        Check(chronicle is not null && chronicle.Page == chronicle.Chronicle.Pages && chronicle.Body.GetChildCount() == chronicle.Chronicle.Seasons.Count,
            "the full chronicle opens at its latest page, by season");
        if (chronicle is not null)
        {
            chronicle.Filter(FantasyWorld.Sim.Views.Category.War);
            Check(chronicle.Chronicle.Seasons.SelectMany(s => s.Rows).All(r => r.Category == FantasyWorld.Sim.Views.Category.War), "the chronicle filters by system");
            await Shot("chronicle");
        }

        main.Router.Home();
        main.Router.Go(new Screen(ScreenKind.Map));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView, "Esc goes back");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, Pressed = true });
        await Frames(2);
        Check(main.Current is MapHudView, "M opens the map");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, Pressed = true });
        await Frames(2);
        Check(main.Current is DigestView && !main.Router.CanGoBack, "H goes home");

        var news = session.Read((w, l) => l.All.Last(e => e.Importance >= Main.LiveToastImportance).Id);
        main.Toast("A test of the live notice.", news);
        await Frames(2);
        Check(main.CurrentToast is { Visible: true }, "notable events are announced while the game is open");
        main.CurrentToast!.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Check(main.Router.Current == new Screen(ScreenKind.Why, news), "and the notice opens its why chain");
        main.Router.Home();
    }

    private async Task PageChecks(Main main)
    {
        var session = main.Session!;
        var person = session.Read((w, l) => l.All.Reverse().SelectMany(e => e.Actors.Values).First(a => a.Kind == "char" && w.Characters.ContainsKey(a.Id)).Id);
        main.Router.Open(new FantasyWorld.Sim.Events.EntityRef("char", person));
        await Frames(3);
        var character = main.Current as CharacterView;
        Check(character is not null && !main.Diorama.Visible, "a character link opens their page over the diorama");
        if (character is null) return;
        var screen = character.Character;
        Check(character.GetNode<Label>("Left/Who/Title").Text == $"{screen.Name}.", "the page is titled with their name");
        Check(character.GetNode("Left/Chips").GetChildCount() == screen.Traits.Count + (screen.Ambition.Length > 0 ? 1 : 0) + (screen.Faith is null ? 0 : 1) + (screen.Saint ? 1 : 0),
            "faith, traits and ambition are chips");
        Check(screen.Faith is null || character.GetNodeOrNull("Left/Chips/Faith") is not null, "their faith is named");
        Check(character.Timeline.GetChildren().Count(c => c.Name.ToString().StartsWith("Event") || c.Name == "Highlight") == screen.Timeline.Count, "every deed is on the timeline");
        Check(character.GetNode("Ties/Column/List").GetChildCount() == Math.Max(1, screen.Ties.Count), "ties are listed");
        await Shot("character");
        var followed = session.IsFollowed(screen.Ref);
        character.ToggleFollow();
        Check(session.IsFollowed(screen.Ref) != followed && character.FollowButton.Text == (followed ? "Follow" : "Following"), "Follow follows");
        character.ToggleFollow();

        character.GetNode<Button>("Ties/Column/Actions/Family").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shown = (main.Current as FamilyView)?.FindChildren("Person*", "", true, false).Count ?? -1;
        Check(shown == session.Family(person).Nodes.Count, $"the family tree shows everyone ({shown} of {session.Family(person).Nodes.Count})");
        await Shot("family");
        main.Router.Back();
        await Frames(2);
        Check(main.Current is CharacterView, "Back returns to the character");

        var eventId = session.Read((w, l) => l.All.Last(e => e.Causes.Count > 0 && l.TryGet(e.Causes[0].EventId, out _)).Id);
        main.Router.Explain(eventId);
        await Frames(3);
        var why = main.Current as WhyView;
        Check(why is not null, "a why chain opens");
        if (why is null) return;
        Check(why.Graph.Cards.Count == 1 + why.Why.Layers.Sum(l => l.Count) && why.Canvas.FindChildren("Card*", "", false, false).Count == why.Graph.Cards.Count,
            "every cause is a card");
        Check(why.GetNode<Label>("Intro/Summary").Text == why.Why.Summary, "the chain is summarised");
        Check(why.Why.Story is null || why.GetNodeOrNull<RichTextLabel>("Intro/StoryScroll/Story") is { } story && story.GetParsedText() == why.Why.Story.Text,
            "the chain is told as a story");
        await Shot("why");
        Check(why.Canvas.FindChildren("Title", "RichTextLabel", true, false).Cast<Control>().All(t => t.MouseFilter == Control.MouseFilterEnum.Pass),
            "clicks on a card's words reach the card");
        var cause = why.Graph.Cards[1].Card.EventId;
        why.Canvas.GetNode<PanelContainer>($"Card{cause}").EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Frames(2);
        Check(main.Router.Current == new Screen(ScreenKind.Why, cause), "clicking a cause traces it further back");
        main.Router.Back();
        await Frames(2);
        var again = (WhyView)main.Current!;
        again.GetNode<Button>("Chain/Column/ActionsRow/Actions/TraceFurther").EmitSignal(BaseButton.SignalName.Pressed);
        Check(again.ShowingConsequences && again.Graph.Cards.Count == 1 + again.Why.Consequences.Count, "trace further shows what it led to");
        main.Router.Explain(long.MaxValue);
        await Frames(3);
        Check(main.Router.Current != new Screen(ScreenKind.Why, long.MaxValue) && main.Current is not null && main.CurrentToast is not null,
            "a page whose subject is gone says so and goes back instead of breaking");
        main.Router.Home();
        await Frames(2);
        Check(main.Diorama.Visible && main.Current is DigestView, "home is the digest over the diorama");

        var rig = main.Diorama.Rig;
        var peak = main.Diorama.Markers.Count == 0 ? Godot.Vector3.Zero : Diorama.ToGodot(main.Diorama.Markers[0].Position);
        rig.Focus(new System.Numerics.Vector3(peak.X, 0, peak.Z), Zoom.Town);
        rig.Drag(0, 100_000);
        rig.Drag(0, -100_000);
        rig.Scroll(100);
        await Frames(120);
        var eye = main.Diorama.Camera.Position;
        Check(eye.Y > Math.Max(0, FantasyWorld.Client.BlockMesh.HeightAt(main.Geography!.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)),
            "the camera never goes below the ground");
        rig.Reset();
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
        Check(view.TagLayer.GetChildren().OfType<SettlementTag>().Count() == tagged, $"settlements big enough for the zoom are named ({tagged})");
        Check(view.GetNode("Legend").GetChildCount() == map.Legend.Count + (map.Armies is { Count: > 0 } ? 2 : 0), "the legend lists the realms");
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
        var trade = view.GetNode<CheckBox>("Card/Column/Layers/TradeRoutes");
        trade.ButtonPressed = false;
        Check(!main.Diorama.RoadLines.Visible, "the trade routes layer can be hidden");
        trade.ButtonPressed = true;
        Check(main.Diorama.RoadLines.Visible && main.Diorama.Roads.Count == (map.Routes?.Count ?? 0), "every trade road is drawn");
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
        view.ShowsControls = false;
        Check(!view.GetNode<Control>("Card").Visible && view.GetNode<Control>("Tags").Visible, "a filmed map hides its control card, not its labels");
        view.ShowsControls = true;

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
        var settlements = map.Settlements.Count(s => s.Position is not null);
        Check(diorama.Markers.Count(m => m.Shape == MarkerShape.Settlement) == settlements
              && diorama.SettlementLayer.GetNode<MultiMeshInstance3D>("Roofs").Multimesh.InstanceCount >= 3 * settlements,
            "every settlement is a cluster of houses");
        Check(diorama.StructureLayer.GetChildren().Count(n => n.Name.ToString().StartsWith("structure")) == map.Structures.Count(s => s.Position is not null),
            "every landmark and labyrinth stands on the block");
        Check(diorama.StructureLayer.GetChildren().Count(n => n.Name.ToString().StartsWith("temple") || n.Name.ToString().StartsWith("tower"))
              == map.Settlements.Where(s => s.Position is not null).Sum(s => s.Marks?.Count ?? 0),
            "temples and mage towers stand beside their towns");
        Check(diorama.TreeCount > 100, $"woods are planted ({diorama.TreeCount} trees)");
        Check(map.Borders.Count == 0 || diorama.BorderLines.Mesh is ArrayMesh { } lines && lines.GetSurfaceCount() == 2, "realm borders are drawn, ink over a halo");

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

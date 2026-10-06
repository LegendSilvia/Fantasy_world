using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Diorama map HUD* (sub-project 4 §4): the world's name and summary, name tags over settlements (more as you
/// descend), a dark hover card per settlement, and the control card: zoom presets, layer toggles, the history slider
/// that rebuilds the map at any past year, reset and snapshot. A legend of the realms sits bottom left.
/// </summary>
public partial class MapHudView : Control
{
    public Main Host { get; init; } = null!;

    /// <summary>A place to fly to when the map opens (a linked settlement, region or structure).</summary>
    public EntityRef? Focus { get; init; }

    public MapScreen Map { get; private set; } = null!;
    public int Year { get; private set; }
    public int Now { get; private set; }
    public Control TagLayer { get; private set; } = null!;
    public PanelContainer HoverCard { get; private set; } = null!;
    public HSlider History { get; private set; } = null!;

    private Label _kicker = null!;
    private Label _summary = null!;
    private Label _yearLabel = null!;
    private VBoxContainer _legend = null!;
    private Zoom _zoom;
    private int? _pendingYear;

    public override void _Ready()
    {
        Name = "MapScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Map = Host.Session!.Map();
        Now = Map.Year;
        Year = Host.Diorama.ShownYear ?? Now;
        if (Year != Now) Map = Host.Session.Map(Year);
        _zoom = Host.Diorama.Rig.NearestZoom;

        TagLayer = new Control { Name = "Tags", MouseFilter = MouseFilterEnum.Ignore };
        TagLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(TagLayer);

        var intro = Ui.Place(Ui.Column("Intro", 10), 0, 0, 56, 48);
        intro.CustomMinimumSize = new Vector2(300, 0);
        _kicker = Ui.Kicker("", "Kicker");
        intro.AddChild(_kicker);
        intro.AddChild(Ui.FittedTitle($"{Map.WorldName}.", 64, 440));
        _summary = Ui.Body("", "Summary", Palette.Muted);
        intro.AddChild(_summary);
        AddChild(intro);

        AddChild(ControlCard());

        _legend = Ui.Place(Ui.Column("Legend", 8), 0, 1, 56, -44);
        AddChild(_legend);

        HoverCard = Ui.Card("HoverCard", 250);
        var dark = (StyleBoxFlat)HoverCard.GetThemeStylebox("panel").Duplicate();
        (dark.BgColor, dark.BorderColor, dark.CornerRadiusBottomLeft, dark.CornerRadiusBottomRight, dark.CornerRadiusTopLeft, dark.CornerRadiusTopRight) =
            (Ui.C(Palette.Ink), Ui.C(Palette.Ink), 12, 12, 12, 12);
        (dark.ContentMarginLeft, dark.ContentMarginRight, dark.ContentMarginTop, dark.ContentMarginBottom) = (16, 16, 14, 14);
        HoverCard.AddThemeStyleboxOverride("panel", dark);
        HoverCard.MouseFilter = MouseFilterEnum.Ignore;
        HoverCard.Visible = false;
        AddChild(HoverCard);

        Refresh();
        if (Focus is { } focus && MapLabels.Find(Map, focus) is { } point) Host.Diorama.Focus(point, MapLabels.ZoomFor(focus));
    }

    /// <summary>Shows the map as it was at the end of a year (today's map for the current year).</summary>
    public void ShowYear(int year)
    {
        Year = Math.Clamp(year, 1, Now);
        Map = Year == Now ? Host.Session!.Map() : Host.Session!.Map(Year);
        Host.Diorama.Show(Map, Year == Now ? null : Year);
        if (Math.Abs(History.Value - Year) > 0.5) History.SetValueNoSignal(Year);
        Refresh();
    }

    public void SetLayer(MapLayers layer, bool on)
    {
        Host.Diorama.SetLayers(on ? Host.Diorama.Layers | layer : Host.Diorama.Layers & ~layer);
        RebuildTags();
    }

    private void Refresh()
    {
        _kicker.Text = $"{Map.WorldName} · {_zoom} view".ToUpperInvariant();
        _summary.Text = Year != Now ? $"Year {Map.Year}. {Map.Summary}" : Map.Summary;
        _yearLabel.Text = Year == Now ? $"Year {Now}" : $"Year {Year} of {Now}";
        Ui.Clear(_legend);
        foreach (var entry in Map.Legend)
        {
            var row = Ui.Row("Entry", 8);
            row.AddChild(Ui.Dot(entry.Colour switch { "conflict" => Palette.Conflict, "event" => Palette.Ink, var name => Palette.Heraldic(name) }, 10));
            var label = Ui.Body(entry.Label, "Label", Palette.Body, 12);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            row.AddChild(label);
            _legend.AddChild(row);
        }
        RebuildTags();
    }

    /// <summary>The name tags for the settlements this zoom and these layers show.</summary>
    public void RebuildTags()
    {
        Ui.Clear(TagLayer);
        HoverCard.Visible = false;
        foreach (var settlement in Map.Settlements.Where(s => MapLabels.Shows(s, _zoom, Host.Diorama.Layers)))
            TagLayer.AddChild(Tag(settlement));
    }

    /// <summary>A live day passed: if the map shows today, it shows the new today.</summary>
    public void Today(MapScreen today)
    {
        var wasNow = Year == Now;
        Now = today.Year;
        History.MaxValue = Math.Max(2, Now);
        if (!wasNow) return;
        (Year, Map) = (Now, today);
        History.SetValueNoSignal(Now);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_pendingYear is { } pending)
        {
            _pendingYear = null;
            if (pending != Year) ShowYear(pending);
        }
        if (Host.Diorama.Rig.NearestZoom != _zoom)
        {
            _zoom = Host.Diorama.Rig.NearestZoom;
            Refresh();
        }
        var grid = Host.Geography?.Terrain;
        if (grid is null) return;
        var tags = TagLayer.GetChildren().OfType<SettlementTag>().ToList();
        var boxes = new List<LabelBox>(tags.Count);
        foreach (var tag in tags)
        {
            var at = Client.Projection.ToBlock(grid, tag.Settlement.Position!, 0.02f);
            tag.Position = Host.Diorama.ScreenOf(at) - new Vector2(11, 24);
            var behind = Host.Diorama.Camera.IsPositionBehind(Diorama.ToGodot(at));
            boxes.Add(behind ? new LabelBox(-1e5f, -1e5f, 0, 0, 0) : new LabelBox(tag.Position.X, tag.Position.Y, tag.Size.X, tag.Size.Y, Priority(tag.Settlement)));
        }
        var visible = Declutter.Visible(boxes, GetViewportRect().Size.X, GetViewportRect().Size.Y, Reserved(this, "Card", "Intro", "Legend"));
        for (var i = 0; i < tags.Count; i++) tags[i].Visible = visible[i];
    }

    /// <summary>News first, then cities, towns, villages: the order in which tags claim screen space.</summary>
    public static int Priority(MapSettlement settlement) => settlement.Marker switch
    {
        PinMarker.Conflict => 10,
        PinMarker.RecentEvent => 9,
        _ => Enum.TryParse<FantasyWorld.Sim.Model.SettlementTier>(settlement.Tier, out var tier) ? (int)tier : 0,
    };

    /// <summary>Screen boxes of a screen's own panels, which labels must not cover.</summary>
    public static IEnumerable<LabelBox> Reserved(Control screen, params string[] panels) =>
        panels.Select(p => screen.GetNodeOrNull<Control>(p)).Where(c => c is { Visible: true })
            .Select(c => c!.GetGlobalRect()).Select(r => new LabelBox(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, int.MaxValue));

    private SettlementTag Tag(MapSettlement settlement)
    {
        var tag = new SettlementTag { Name = $"Tag{settlement.Ref.Id}", Settlement = settlement, MouseDefaultCursorShape = CursorShape.PointingHand };
        tag.AddThemeConstantOverride("separation", 6);
        var marker = Host.Diorama.Markers.FirstOrDefault(m => m.Ref == settlement.Ref);
        var colour = settlement.Marker switch
        {
            PinMarker.Conflict => Palette.Conflict,
            PinMarker.RecentEvent => Palette.Ink,
            _ => marker?.Colour ?? Palette.Ink,
        };
        var dot = Ui.Capsule("Dot", Ui.C(colour), Ui.C(Palette.Card), 0, 0);
        dot.CustomMinimumSize = new Vector2(18, 18);
        dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var pill = Ui.Capsule("Name", Ui.C(Palette.Card), Ui.C(settlement.Marker == PinMarker.Conflict ? Palette.Ink : Palette.Border));
        var name = Ui.Body(settlement.Name, "Text", Palette.Ink, 12);
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        pill.AddChild(name);
        tag.AddChild(dot);
        tag.AddChild(pill);
        foreach (var part in new Control[] { dot, pill }) part.MouseFilter = MouseFilterEnum.Ignore;
        tag.MouseEntered += () => ShowCard(tag);
        tag.MouseExited += () => HoverCard.Visible = false;
        tag.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Host.Diorama.Focus(settlement.Position!, Zoom.Town);
        };
        return tag;
    }

    /// <summary>The settlement's dark card above its tag: "Carrow Ford · Town", who holds it, people and events.</summary>
    public void ShowCard(SettlementTag tag)
    {
        Ui.Clear(HoverCard);
        var column = Ui.Column("Column", 6);
        var kicker = Ui.Kicker(tag.Settlement.Card.Kicker);
        kicker.AddThemeColorOverride("font_color", new Color(0.73f, 0.72f, 0.69f));
        column.AddChild(kicker);
        var title = Ui.Title(tag.Settlement.Card.Title, 22, italic: false);
        title.AddThemeColorOverride("font_color", Ui.C(Palette.Card));
        column.AddChild(title);
        if (tag.Settlement.Card.Line.Length > 0)
            column.AddChild(Ui.Body(tag.Settlement.Card.Line, "Line", new System.Numerics.Vector3(0.83f, 0.82f, 0.8f), 12));
        HoverCard.AddChild(column);
        HoverCard.Position = tag.Position + new Vector2(30, -110);
        HoverCard.Visible = true;
    }

    private PanelContainer ControlCard()
    {
        var card = Ui.Place(Ui.Card("Card", 280), 1, 0, -40, 40);
        var column = Ui.Column("Column", 18);
        card.AddChild(column);

        var zoom = Ui.Column("Zoom", 8);
        zoom.AddChild(Ui.Kicker("Zoom"));
        var presets = Ui.Row("Presets", 6);
        foreach (var preset in Enum.GetValues<Zoom>())
        {
            var button = Ui.Button(preset.ToString(), preset.ToString());
            button.Pressed += () => Host.Diorama.Rig.Preset(preset);
            presets.AddChild(button);
        }
        zoom.AddChild(presets);
        column.AddChild(zoom);

        var layers = Ui.Column("Layers", 4);
        layers.AddChild(Ui.Kicker("Layers"));
        foreach (var (layer, label) in new[]
                 {
                     (MapLayers.Borders, "Borders"), (MapLayers.Settlements, "Settlements"), (MapLayers.Events, "Events"),
                     (MapLayers.Structures, "Landmarks & labyrinths"), (MapLayers.TradeRoutes, "Trade routes"),
                 })
        {
            var box = new CheckBox { Name = layer.ToString(), Text = label, ButtonPressed = Host.Diorama.Layers.HasFlag(layer) };
            box.AddThemeFontSizeOverride("font_size", 12);
            // Trade routes are not modelled yet (sub-project 3); the layer is shown but cannot be turned on.
            box.Disabled = layer == MapLayers.TradeRoutes;
            box.TooltipText = box.Disabled ? "Trade routes arrive with a later update." : "";
            box.Toggled += on => SetLayer(layer, on);
            layers.AddChild(box);
        }
        column.AddChild(layers);

        var history = Ui.Column("HistoryBlock", 8);
        var header = Ui.Row("Header", 0);
        var historyKicker = Ui.Kicker("History");
        historyKicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(historyKicker);
        _yearLabel = Ui.Kicker("", "Year");
        header.AddChild(_yearLabel);
        history.AddChild(header);
        History = new HSlider { Name = "History", MinValue = 1, MaxValue = Math.Max(2, Now), Step = 1, Value = Year, CustomMinimumSize = new Vector2(0, 18) };
        // Dragging the slider passes many years a frame; rebuild only for the last one each frame.
        History.ValueChanged += value => _pendingYear = (int)value;
        history.AddChild(History);
        var ends = Ui.Row("Ends", 0);
        var first = Ui.Line("Year 1", "First", Palette.Muted, 12);
        first.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ends.AddChild(first);
        ends.AddChild(Ui.Line("Now", "Now", Palette.Muted, 12));
        history.AddChild(ends);
        column.AddChild(history);

        var actions = Ui.Row("Actions", 6);
        var reset = Ui.Button("Reset view", "Reset");
        reset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        reset.Pressed += () =>
        {
            Host.Diorama.Rig.Reset();
            ShowYear(Now);
        };
        var snapshot = Ui.Button("Snapshot", "Snapshot");
        snapshot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        snapshot.Pressed += () => Snapshot();
        actions.AddChild(reset);
        actions.AddChild(snapshot);
        column.AddChild(Ui.Ruled("ActionsRow", actions));
        return card;
    }

    /// <summary>Saves what the screen shows to user://snapshots; returns the file, or null without a real display.</summary>
    public string? Snapshot()
    {
        var image = GetViewport().GetTexture()?.GetImage();
        if (image is null || image.IsEmpty()) return null;
        var folder = System.IO.Path.Combine(OS.GetUserDataDir(), "snapshots");
        System.IO.Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, $"{Map.WorldName.ToLowerInvariant().Replace(' ', '-')}-year-{Year}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        image.SavePng(file);
        Host.Status.Text = $"Snapshot saved · {System.IO.Path.GetFileName(file)}";
        return file;
    }
}

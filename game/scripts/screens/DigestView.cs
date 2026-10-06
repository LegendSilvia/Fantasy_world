using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Morning digest* (sub-project 4 §4): the year and time away on the left, the control card on the right with
/// the Digest / Full chronicle toggle, the headline and its "Why did this happen?", the other items, and numbered
/// pins over their places on the diorama. Clicking an item flies to its place; clicking a pin opens its story.
/// </summary>
public partial class DigestView : Control
{
    public Main Host { get; init; } = null!;
    public DigestScreen Digest { get; init; } = null!;

    public bool ShowingChronicle { get; private set; }
    public VBoxContainer Content { get; private set; } = null!;
    public Control PinLayer { get; private set; } = null!;

    public override void _Ready()
    {
        Name = "DigestScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        PinLayer = new Control { Name = "Pins", MouseFilter = MouseFilterEnum.Ignore };
        PinLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(PinLayer);
        foreach (var pin in Host.Diorama.Pins) PinLayer.AddChild(PinButton(pin));

        var intro = Ui.Column("Intro", 10);
        Ui.Place(intro, 0, 0, 56, 48);
        intro.CustomMinimumSize = new Vector2(340, 0);
        intro.AddChild(Ui.Kicker($"{Digest.WorldName} · Morning digest"));
        intro.AddChild(Ui.FittedTitle(Digest.Title, 72, 440));
        intro.AddChild(Ui.Body(Digest.Subtitle, "Subtitle", Palette.Muted));
        AddChild(intro);

        var card = Ui.Card("Card", 360);
        Ui.Place(card, 1, 0, -40, 40);
        var column = Ui.Column("Column", 20);
        card.AddChild(column);
        var top = Ui.Row("Top", 0);
        var overnight = Ui.Kicker("Overnight");
        overnight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(overnight);
        top.AddChild(Ui.Kicker(Markup.Elapsed(Digest.ElapsedDays), "Elapsed"));
        column.AddChild(top);
        var toggle = Ui.Row("Toggle", 6);
        var digestButton = Ui.Button("Digest", "DigestButton", primary: true);
        var chronicleButton = Ui.Button("Full chronicle", "ChronicleButton");
        digestButton.Pressed += () => ShowChronicle(false);
        chronicleButton.Pressed += () => ShowChronicle(true);
        toggle.AddChild(digestButton);
        toggle.AddChild(chronicleButton);
        column.AddChild(toggle);
        Content = Ui.Column("Content", 20);
        column.AddChild(Content);
        AddChild(card);

        var hint = Ui.Column("Hint", 2);
        hint.CustomMinimumSize = new Vector2(300, 0);
        Ui.Place(hint, 0, 1, 56, -44);
        hint.AddChild(Ui.Title("Drag to orbit. Scroll to descend.", 15));
        hint.AddChild(Ui.Body("Click a numbered pin to read its story.", "HintBody", Palette.Muted, 12));
        AddChild(hint);

        ShowChronicle(false);
    }

    /// <summary>Switches the card between the digest and the full chronicle of the last two years.</summary>
    public void ShowChronicle(bool chronicle)
    {
        ShowingChronicle = chronicle;
        Ui.Clear(Content);
        var toggle = GetNode<HBoxContainer>("Card/Column/Toggle");
        Restyle(toggle.GetNode<Button>("DigestButton"), !chronicle);
        Restyle(toggle.GetNode<Button>("ChronicleButton"), chronicle);
        if (chronicle) FillChronicle();
        else FillDigest();
    }

    private void FillDigest()
    {
        if (Digest.Headline is { } headline)
        {
            var block = Ui.Column("Headline", 8);
            var kicker = Ui.Row("KickerRow", 8);
            kicker.AddChild(Ui.Dot(Palette.For(headline.Category)));
            kicker.AddChild(Ui.Kicker($"{headline.Number} · {headline.Kicker}"));
            block.AddChild(kicker);
            block.AddChild(Ui.Linked(headline.Title, "HeadlineTitle", Host.Router.Open, 30, Palette.Ink, serif: true));
            block.AddChild(Ui.Linked(headline.Body, "HeadlineBody", Host.Router.Open));
            if (headline.CanExplain)
            {
                var why = Ui.Button("Why did this happen?", "Why", primary: true);
                why.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                why.Pressed += () => Host.Router.Explain(headline.EventId);
                block.AddChild(why);
            }
            Content.AddChild(block);
        }
        else Content.AddChild(Ui.Body("Nothing of note happened while you were away.", "Empty", Palette.Muted));

        var items = Ui.Column("Items", 0);
        for (var i = 0; i < Digest.Items.Count; i++)
        {
            var item = Digest.Items[i];
            var row = Ui.Row("Row", 12);
            var number = Ui.Body(item.Number.ToString(), "Number", Palette.Muted);
            number.CustomMinimumSize = new Vector2(14, 0);
            row.AddChild(number);
            row.AddChild(Ui.Linked(item.Text, "Text", Host.Router.Open, 13, Palette.Ink));
            var ruled = Ui.Ruled($"Item{item.Number}", row, ruleBelow: i == Digest.Items.Count - 1);
            var eventId = item.EventId;
            ruled.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) FlyTo(eventId);
            };
            items.AddChild(ruled);
        }
        Content.AddChild(items);
    }

    private void FillChronicle()
    {
        // This year and last, newest first: early in a year this year alone is nearly empty.
        var from = Math.Max(1, Digest.Year - 1);
        var first = Host.Session!.Chronicle(from, Digest.Year);
        var page = first.Pages > 1 ? Host.Session.Chronicle(from, Digest.Year, page: first.Pages) : first;
        var scroll = new ScrollContainer { Name = "Chronicle", CustomMinimumSize = new Vector2(0, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = Ui.Column("List", 12);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var season in page.Seasons.Reverse())
        {
            list.AddChild(Ui.Kicker(season.Label, "Season"));
            foreach (var row in season.Rows.Reverse())
            {
                var line = Ui.Row("Row", 8);
                line.AddChild(Ui.Dot(Palette.For(row.Category), 6));
                line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 12));
                list.AddChild(line);
            }
        }
        if (page.Total == 0) list.AddChild(Ui.Body("Nothing is recorded for these years yet.", "Empty", Palette.Muted));
        scroll.AddChild(list);
        Content.AddChild(scroll);
    }

    /// <summary>Eases the camera to the place of a digest entry, when it has one.</summary>
    public void FlyTo(long eventId)
    {
        if (Digest.Pins.FirstOrDefault(p => p.EventId == eventId) is { } pin) Host.Diorama.Focus(pin.Position, Zoom.Region);
    }

    public override void _Process(double delta)
    {
        var tags = PinLayer.GetChildren().OfType<PinTag>().ToList();
        var boxes = new List<LabelBox>(tags.Count);
        foreach (var tag in tags)
        {
            var pin = Host.Diorama.Pins.First(p => p.Number == tag.Number);
            tag.Position = Host.Diorama.ScreenOf(pin.Position) - new Vector2(11, 11);
            boxes.Add(Host.Diorama.Camera.IsPositionBehind(Diorama.ToGodot(pin.Position))
                ? new LabelBox(-1e5f, -1e5f, 0, 0, 0)
                : new LabelBox(tag.Position.X, tag.Position.Y, tag.Size.X, tag.Size.Y, 100 - tag.Number));
        }
        var visible = Declutter.Visible(boxes, GetViewportRect().Size.X, GetViewportRect().Size.Y, MapHudView.Reserved(this, "Card", "Intro"));
        for (var i = 0; i < tags.Count; i++) tags[i].Visible = visible[i];
    }

    private PinTag PinButton(Pin pin)
    {
        var tag = new PinTag { Name = $"Pin{pin.Number}", Number = pin.Number, EventId = pin.EventId, MouseDefaultCursorShape = CursorShape.PointingHand };
        tag.AddThemeConstantOverride("separation", 6);
        var number = Ui.Capsule("Number", Ui.C(Palette.Ink), padX: 7, padY: 2);
        number.AddChild(Ui.Body(pin.Number.ToString(), "Digit", Palette.Card, 11));
        var label = Ui.Capsule("Label", Ui.C(Palette.Card), Ui.C(Palette.Border));
        label.AddChild(Ui.Body(pin.Label, "Text", Palette.Ink, 12));
        tag.AddChild(number);
        tag.AddChild(label);
        foreach (var text in tag.FindChildren("*", "Label", owned: false)) ((Label)text).AutowrapMode = TextServer.AutowrapMode.Off;
        foreach (var capsule in new Control[] { number, label }) capsule.MouseFilter = MouseFilterEnum.Ignore;
        tag.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Host.Router.Explain(tag.EventId);
        };
        return tag;
    }

    private static void Restyle(Button button, bool active)
    {
        button.AddThemeStyleboxOverride("normal", Ui.Pill(filled: active));
        button.AddThemeStyleboxOverride("hover", Ui.Pill(filled: active));
        button.AddThemeColorOverride("font_color", active ? Colors.White : Ui.C(Palette.Ink));
        button.AddThemeColorOverride("font_hover_color", active ? Colors.White : Ui.C(Palette.Body));
    }
}

/// <summary>A numbered pin's label on screen: the number in a black circle and the place in a pill.</summary>
public partial class PinTag : HBoxContainer
{
    public int Number { get; init; }
    public long EventId { get; init; }
}

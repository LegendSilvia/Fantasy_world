using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Character page* (sub-project 4 §4): name, role line, trait and ambition chips and a painted inset map of
/// where they are on the left; their life and deeds down the middle with the latest great deed in a dark card; ties,
/// Follow and Family tree on the right. Every name is a link.
/// </summary>
public partial class CharacterView : Control
{
    public Main Host { get; init; } = null!;
    public CharacterScreen Character { get; init; } = null!;

    public VBoxContainer Timeline { get; private set; } = null!;
    public Button FollowButton { get; private set; } = null!;

    public override void _Ready()
    {
        Name = "CharacterScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Ui.Page());

        var left = Ui.Place(Ui.Column("Left", 24), 0, 0, 56, 40);
        left.CustomMinimumSize = new Vector2(380, 0);
        left.AddChild(Ui.BackLink(() => Host.Router.Back()));
        var who = Ui.Column("Who", 10);
        who.AddChild(Ui.Kicker(Character.Kicker));
        who.AddChild(Ui.FittedTitle($"{Character.Name}.", 76, 380));
        who.AddChild(Ui.Body(Character.Line, "Line", Palette.Body));
        left.AddChild(who);
        var chips = new HFlowContainer { Name = "Chips" };
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        if (Character.Saint) chips.AddChild(Ui.Chip("Saint", filled: true, name: "Saint", dot: Palette.Gold));
        if (Character.Faith is { } faith) chips.AddChild(Ui.Chip(faith.Name, name: "Faith", dot: Palette.Gold));
        foreach (var trait in Character.Traits) chips.AddChild(Ui.Chip(trait, name: "Trait"));
        if (Character.Ambition.Length > 0)
            chips.AddChild(Ui.Chip(Character.Ambition.StartsWith("Ambition", StringComparison.OrdinalIgnoreCase) ? Character.Ambition : $"Ambition: {Character.Ambition}",
                filled: true, name: "Ambition"));
        left.AddChild(chips);
        AddChild(left);
        AddChild(Inset());

        var middle = Ui.Place(Ui.Column("Middle", 18), 0, 0, 476, 92);
        middle.CustomMinimumSize = new Vector2(580, 0);
        middle.AddChild(Ui.Kicker("Life & deeds"));
        var scroll = new ScrollContainer { Name = "Scroll", CustomMinimumSize = new Vector2(580, 760), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Timeline = Ui.Column("Timeline", 0);
        Timeline.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (Character.Earlier > 0)
            Timeline.AddChild(Ui.Body($"{Character.Earlier} earlier deeds are in the full chronicle.", "Earlier", Palette.Muted, 12));
        foreach (var row in Character.Timeline) Timeline.AddChild(row.Highlight ? Highlight(row) : Row(row));
        if (Character.Folded > 0)
            Timeline.AddChild(Ui.Ruled("Folded", Ui.Body($"… and {Character.Folded} smaller moments.", "Text", Palette.Muted, 12), ruleBelow: true));
        scroll.AddChild(Timeline);
        middle.AddChild(scroll);
        AddChild(middle);
        // Lives read upward from the latest deed, as in the design: open at the bottom.
        Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();

        AddChild(TiesCard());
    }

    private Control Row(TimelineRow row)
    {
        var line = Ui.Row("Row", 0);
        var year = Ui.Line($"Year {row.Year}", "Year", Palette.Muted, 12);
        year.CustomMinimumSize = new Vector2(76, 0);
        line.AddChild(year);
        line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 14, Palette.Ink));
        var ruled = Ui.Ruled($"Event{row.EventId}", line);
        ((StyleBoxFlat)ruled.GetThemeStylebox("panel")).ContentMarginTop = 14;
        ((StyleBoxFlat)ruled.GetThemeStylebox("panel")).ContentMarginBottom = 14;
        return ruled;
    }

    /// <summary>The year's most important deed, in the design's dark card with a larger serif line.</summary>
    private Control Highlight(TimelineRow row)
    {
        var card = Ui.Card($"Event{row.EventId}", 0);
        var style = (StyleBoxFlat)card.GetThemeStylebox("panel").Duplicate();
        (style.BgColor, style.BorderColor) = (Ui.C(Palette.Ink), Ui.C(Palette.Ink));
        (style.CornerRadiusBottomLeft, style.CornerRadiusBottomRight, style.CornerRadiusTopLeft, style.CornerRadiusTopRight) = (12, 12, 12, 12);
        (style.ContentMarginTop, style.ContentMarginBottom, style.ContentMarginLeft, style.ContentMarginRight) = (18, 18, 20, 20);
        card.AddThemeStyleboxOverride("panel", style);
        var line = Ui.Row("Row", 0);
        var year = Ui.Line($"Year {row.Year}", "Year", new System.Numerics.Vector3(0.73f, 0.72f, 0.69f), 12);
        year.CustomMinimumSize = new Vector2(76, 0);
        line.AddChild(year);
        line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 22, Palette.Card, serif: true));
        card.AddChild(line);
        var spacer = new MarginContainer { Name = "Highlight" };
        spacer.AddThemeConstantOverride("margin_top", 4);
        spacer.AddChild(card);
        return spacer;
    }

    private PanelContainer TiesCard()
    {
        var card = Ui.Place(Ui.Card("Ties", 320), 1, 0, -40, 40);
        var column = Ui.Column("Column", 16);
        column.AddChild(Ui.Kicker("Ties"));
        var list = Ui.Column("List", 0);
        for (var i = 0; i < Character.Ties.Count; i++)
        {
            var tie = Character.Ties[i];
            var entry = Ui.Column("Entry", 2);
            var label = tie.Qualifier.Length > 0 ? $"{tie.Relation} · {tie.Qualifier}" : tie.Relation;
            var kicker = Ui.Body(label, "Relation", tie.Tone == Sim.Views.Tone.Hostile ? Palette.Conflict : Palette.Muted, 11);
            entry.AddChild(kicker);
            entry.AddChild(Ui.NameLink(new Mention(tie.Ref, tie.Name), "Name", Host.Router.Open));
            list.AddChild(Ui.Ruled($"Tie{i}", entry, ruleBelow: i == Character.Ties.Count - 1));
        }
        if (Character.Ties.Count == 0) list.AddChild(Ui.Body("No close ties are recorded.", "None", Palette.Muted, 12));
        column.AddChild(list);
        var actions = Ui.Row("Actions", 6);
        FollowButton = Ui.Button(Character.Followed ? "Following" : "Follow", "Follow", primary: !Character.Followed);
        FollowButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        FollowButton.Pressed += ToggleFollow;
        var family = Ui.Button("Family tree", "Family");
        family.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        family.Pressed += () => Host.Router.Go(new Screen(ScreenKind.Family, Character.Ref.Id));
        actions.AddChild(FollowButton);
        actions.AddChild(family);
        column.AddChild(actions);
        card.AddChild(column);
        return card;
    }

    /// <summary>Follows or unfollows: followed people lead the digest and are never compacted from memory.</summary>
    public void ToggleFollow()
    {
        var follow = !Host.Session!.IsFollowed(Character.Ref);
        Host.Session.Follow(Character.Ref, follow);
        FollowButton.Text = follow ? "Following" : "Follow";
        foreach (var (state, filled) in new[] { ("normal", !follow), ("hover", !follow) }) FollowButton.AddThemeStyleboxOverride(state, Ui.Pill(filled));
        FollowButton.AddThemeColorOverride("font_color", follow ? Ui.C(Palette.Ink) : Colors.White);
    }

    /// <summary>A painted map of the world with a pin where they are now (or were last seen).</summary>
    private Control Inset()
    {
        var frame = Ui.Place(new Control { Name = "Inset", CustomMinimumSize = new Vector2(320, 250), Size = new Vector2(320, 250) }, 0, 1, 56, -290);
        if (Host.MiniMap() is not { } texture) return frame;
        // The world square, framed like the design's cards.
        var border = new Panel { Name = "Frame", Size = new Vector2(252, 252), Position = new Vector2(-1, -1), MouseFilter = MouseFilterEnum.Ignore };
        border.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ui.C(Palette.Card), BorderColor = Ui.C(Palette.Border), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            ShadowColor = new Color(0.1f, 0.1f, 0.09f, 0.06f), ShadowSize = 16, ShadowOffset = new Vector2(0, 8),
        });
        frame.AddChild(border);
        var map = new TextureRect
        {
            Name = "Map", Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Size = new Vector2(250, 250), MouseFilter = MouseFilterEnum.Ignore,
        };
        frame.AddChild(map);
        if (Character.Location is { Position: { } position } location)
        {
            var at = new Vector2((float)position.X * 250, (float)position.Y * 250);
            var pin = Ui.Row("Pin", 6);
            var dot = Ui.Capsule("Dot", Ui.C(Palette.Realm), Ui.C(Palette.Card), 0, 0);
            dot.CustomMinimumSize = new Vector2(18, 18);
            dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pin.AddChild(dot);
            var label = Ui.Capsule("Label", Ui.C(Palette.Card), Ui.C(Palette.Border));
            label.AddChild(Ui.Line(location.Label, "Text", Palette.Ink, 12));
            pin.AddChild(label);
            pin.Position = at - new Vector2(9, 12);
            frame.AddChild(pin);
            var open = Ui.Button("Show on map", "ShowOnMap");
            open.Position = new Vector2(0, 258);
            open.Pressed += () => Host.Router.Open(location.Ref);
            frame.AddChild(open);
        }
        return frame;
    }
}

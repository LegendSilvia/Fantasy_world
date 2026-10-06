using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The layout the reading pages share (realm, artifact, chronicle): back link, kicker, title and line on the left;
/// a scrolling column of ruled sections in the middle; a facts card on the right.
/// </summary>
public abstract partial class DocumentPage : Control
{
    public Main Host { get; init; } = null!;

    public VBoxContainer Left { get; private set; } = null!;
    public VBoxContainer Body { get; private set; } = null!;
    public VBoxContainer Facts { get; private set; } = null!;

    protected void Frame(string name, string kicker, string title, string line)
    {
        Name = name;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Ui.Page());
        Left = Ui.Place(Ui.Column("Left", 10), 0, 0, 56, 40);
        Left.CustomMinimumSize = new Vector2(380, 0);
        Left.AddChild(Ui.BackLink(() => Host.Router.Back()));
        Left.AddChild(Ui.Kicker(kicker));
        Left.AddChild(Ui.FittedTitle(title, 64, 380));
        if (line.Length > 0) Left.AddChild(Ui.Body(line, "Line", Palette.Body));
        AddChild(Left);

        var scroll = Ui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 0, 476, 92);
        scroll.CustomMinimumSize = new Vector2(580, 770);
        Body = Ui.Column("Body", 28);
        Body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(Body);
        AddChild(scroll);

        var card = Ui.Place(Ui.Card("FactsCard", 320), 1, 0, -40, 40);
        Facts = Ui.Column("Facts", 0);
        card.AddChild(Facts);
        AddChild(card);
    }

    /// <summary>A titled section of ruled rows in the middle column.</summary>
    protected VBoxContainer Section(string title)
    {
        var section = Ui.Column(title.Replace(" ", ""), 0);
        var kicker = Ui.Kicker(title);
        var pad = new MarginContainer { Name = "Heading" };
        pad.AddThemeConstantOverride("margin_bottom", 10);
        pad.AddChild(kicker);
        section.AddChild(pad);
        Body.AddChild(section);
        return section;
    }

    /// <summary>A row: a muted lead (year, seat, kind) and linked text.</summary>
    protected Control Row(VBoxContainer section, string lead, LinkedText text, bool last = false)
    {
        var line = Ui.Row("Row", 0);
        var label = Ui.Line(lead, "Lead", Palette.Muted, 12);
        label.CustomMinimumSize = new Vector2(96, 0);
        line.AddChild(label);
        line.AddChild(Ui.Linked(text, "Text", Host.Router.Open, 14, Palette.Ink));
        var ruled = Ui.Ruled($"Row{section.GetChildCount()}", line, ruleBelow: last);
        section.AddChild(ruled);
        return ruled;
    }

    protected static LinkedText Named(Mention mention, string before = "", string after = "") =>
        new(before + mention.Name + after, new[] { new TextLink(before.Length, mention.Name.Length, mention.Ref) });

    /// <summary>A fact in the right card: muted label over a value (linked when it names something).</summary>
    protected void Fact(string label, string value, EntityRef? link = null)
    {
        var entry = Ui.Column("Fact", 2);
        entry.AddChild(Ui.Body(label, "Label", Palette.Muted, 11));
        entry.AddChild(link is { } r ? Ui.NameLink(new Mention(r, value), "Value", Host.Router.Open) : Ui.Body(value, "Value", Palette.Ink));
        Facts.AddChild(Ui.Ruled($"Fact{Facts.GetChildCount()}", entry));
    }
}

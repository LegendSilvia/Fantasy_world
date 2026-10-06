using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The *Why chain* (sub-project 4 §4): the outcome in a dark card, its causes in layers below joined by "because"
/// connectors, root causes in dashed cards, and a side panel of the people, places and systems involved with Show on
/// map and Trace further (what this event went on to cause). Clicking any card traces that event in turn.
/// </summary>
public partial class WhyView : Control
{
    public const float GraphLeft = 380;
    public const float GraphWidth = 680;

    public Main Host { get; init; } = null!;
    public WhyScreen Why { get; init; } = null!;

    public WhyGraph Graph { get; private set; } = null!;
    public Control Canvas { get; private set; } = null!;
    public bool ShowingConsequences { get; private set; }

    public override void _Ready()
    {
        Name = "WhyScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Ui.Page());

        var intro = Ui.Place(Ui.Column("Intro", 10), 0, 0, 56, 40);
        intro.CustomMinimumSize = new Vector2(300, 0);
        intro.AddChild(Ui.BackLink(() => Host.Router.Back()));
        var kicker = Ui.Kicker("Why did this happen?");
        intro.AddChild(kicker);
        var length = Why.Outcome.Title.Text.Length;
        var title = Ui.Title(Why.Outcome.Title.Text, length > 100 ? 28 : length > 60 ? 32 : 44);
        title.CustomMinimumSize = new Vector2(300, 0);
        intro.AddChild(title);
        intro.AddChild(Ui.Body(Why.Summary, "Summary", Palette.Muted));
        if (Why.Story is { } story)
        {
            // The story of why: the chain as one paragraph, oldest cause first.
            // Long chains make long stories: they scroll within the column rather than run off the page.
            var scroller = new ScrollContainer { Name = "StoryScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(300, 240) };
            var told = Ui.Linked(story, "Story", Host.Router.Open, 13, Palette.Body);
            told.CustomMinimumSize = new Vector2(288, 0);
            scroller.AddChild(told);
            intro.AddChild(scroller);
        }
        AddChild(intro);

        var scroll = Ui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 0, GraphLeft, 70);
        scroll.CustomMinimumSize = new Vector2(GraphWidth + 16, 620);
        Canvas = new WhyCanvas { Name = "Graph", View = this };
        scroll.AddChild(Canvas);
        AddChild(scroll);
        ShowCauses();

        AddChild(Panel());
        var footer = Ui.Place(Ui.Row("Footer", 6), 0, 1, GraphLeft, -64);
        var lead = Ui.Title("Dashed cards are root causes.", 15);
        lead.AutowrapMode = TextServer.AutowrapMode.Off;
        footer.AddChild(lead);
        footer.AddChild(Ui.Line("Click any card to trace it further back.", "Hint", Palette.Muted, 12));
        AddChild(footer);
    }

    /// <summary>The causes, laid out as the design's layered chain.</summary>
    public void ShowCauses()
    {
        ShowingConsequences = false;
        Graph = WhyLayout.Layout(Why, GraphWidth);
        Fill(Graph);
    }

    /// <summary>Trace further: the outcome on top and what it caused below, joined by "led to".</summary>
    public void ShowConsequences()
    {
        ShowingConsequences = true;
        var forward = new WhyScreen(Why.Outcome, Why.Consequences.Count > 0 ? new[] { Why.Consequences } : Array.Empty<IReadOnlyList<EventCard>>(),
            Why.Consequences.Select(c => new CauseEdge(c.EventId, Why.Outcome.EventId, Sim.Events.CauseKind.Trigger, "led to")).ToList(),
            Why.Summary, Why.People, Why.Places, Why.Systems, Array.Empty<EventCard>());
        Graph = WhyLayout.Layout(forward, GraphWidth);
        Fill(Graph);
    }

    private void Fill(WhyGraph graph)
    {
        Ui.Clear(Canvas);
        Canvas.CustomMinimumSize = new Vector2(GraphWidth, graph.Height + 24);
        foreach (var box in graph.Cards) Canvas.AddChild(Card(box));
        // Root cards' dashed outlines are drawn above the cards.
        var outlines = new WhyCanvas { Name = "Outlines", View = this, Outlines = true, MouseFilter = MouseFilterEnum.Ignore };
        outlines.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Canvas.AddChild(outlines);
        foreach (var label in graph.Connectors.GroupBy(c => c.To).Select(g => g.First()))
        {
            var word = Ui.Title(label.Label, 15);
            word.AutowrapMode = TextServer.AutowrapMode.Off;
            word.AddThemeColorOverride("font_color", Ui.C(Palette.Muted));
            word.Position = new Vector2(label.Points[0].X + 10, label.Points[0].Y + 12);
            Canvas.AddChild(word);
        }
        Canvas.QueueRedraw();
    }

    private PanelContainer Card(CardBox box)
    {
        var card = box.Card;
        var outcome = box.Layer == 0;
        var panel = new PanelContainer
        {
            Name = $"Card{card.EventId}", Position = new Vector2(box.X, box.Y), Size = new Vector2(box.Width, WhyLayout.CardHeight),
            CustomMinimumSize = new Vector2(box.Width, WhyLayout.CardHeight), MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ui.C(outcome ? Palette.Ink : Palette.Card),
            // Roots get no solid border: the canvas draws them a dashed one.
            BorderColor = Ui.C(card.IsRoot && !outcome ? Palette.Card : outcome ? Palette.Ink : Palette.Border),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        });
        var column = Ui.Column("Column", 6);
        var head = Ui.Row("Head", 8);
        if (!outcome) head.AddChild(Ui.Dot(Palette.For(card.Category)));
        var kicker = outcome ? $"Year {card.Year} · Outcome" : card.IsRoot ? $"Year {card.Year} · Root cause" : $"Year {card.Year} · {Categories.Label(card.Category)}";
        var label = Ui.Kicker(kicker);
        if (outcome) label.AddThemeColorOverride("font_color", new Color(0.73f, 0.72f, 0.69f));
        head.AddChild(label);
        column.AddChild(head);
        column.AddChild(Ui.Linked(card.Title, "Title", Host.Router.Open, outcome ? 20 : 15, outcome ? Palette.Card : Palette.Ink, serif: outcome));
        if (!string.IsNullOrWhiteSpace(card.Note) && !outcome)
        {
            var note = Ui.Body(card.Note!, "Note", Palette.Muted, 12);
            note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            note.MaxLinesVisible = 1;
            column.AddChild(note);
        }
        panel.AddChild(column);
        panel.ClipContents = true;
        panel.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && card.EventId != Why.Outcome.EventId)
                Host.Router.Explain(card.EventId);
        };
        return panel;
    }

    private PanelContainer Panel()
    {
        var card = Ui.Place(Ui.Card("Chain", 300), 1, 0, -40, 40);
        var column = Ui.Column("Column", 16);
        column.AddChild(Ui.Kicker("In this chain"));
        column.AddChild(Mentions("People", Why.People));
        column.AddChild(Mentions("Places", Why.Places));
        var systems = Ui.Column("Systems", 8);
        systems.AddChild(Ui.Body("Systems", "Label", Palette.Muted, 11));
        var chips = new HFlowContainer { Name = "Chips" };
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        foreach (var system in Why.Systems) chips.AddChild(Ui.Chip(Categories.Label(system), name: system.ToString(), dot: Palette.For(system)));
        systems.AddChild(chips);
        column.AddChild(systems);
        var actions = Ui.Row("Actions", 6);
        var map = Ui.Button("Show on map", "ShowOnMap", primary: true);
        map.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        map.Disabled = Why.Places.Count == 0;
        map.Pressed += () => Host.Router.Open(Why.Places[0].Ref);
        var further = Ui.Button("Trace further", "TraceFurther");
        further.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        further.Pressed += () =>
        {
            if (ShowingConsequences) ShowCauses();
            else ShowConsequences();
            further.Text = ShowingConsequences ? "Trace back" : "Trace further";
        };
        actions.AddChild(map);
        actions.AddChild(further);
        column.AddChild(Ui.Ruled("ActionsRow", actions));
        card.AddChild(column);
        return card;
    }

    private Control Mentions(string label, IReadOnlyList<Mention> mentions)
    {
        var block = Ui.Column(label, 4);
        block.AddChild(Ui.Body(label, "Label", Palette.Muted, 11));
        if (mentions.Count == 0) block.AddChild(Ui.Body("None recorded", "None", Palette.Muted, 13));
        var shown = mentions.Take(8).ToList();
        var text = string.Join(", ", shown.Select(m => m.Name)) + (mentions.Count > shown.Count ? $" and {mentions.Count - shown.Count} more" : "");
        var links = new List<Sim.Views.TextLink>();
        var at = 0;
        foreach (var m in shown)
        {
            links.Add(new Sim.Views.TextLink(at, m.Name.Length, m.Ref));
            at += m.Name.Length + 2;
        }
        if (mentions.Count > 0) block.AddChild(Ui.Linked(new LinkedText(text, links), "Names", Host.Router.Open, 13, Palette.Ink));
        return block;
    }
}

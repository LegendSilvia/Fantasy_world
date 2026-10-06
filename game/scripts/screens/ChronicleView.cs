using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>The full chronicle: every recorded event by season, newest last, filtered by system and paged.</summary>
public partial class ChronicleView : DocumentPage
{
    /// <summary>Opens filtered to one system (a war link opens the war chronicle).</summary>
    public Category? Only { get; set; }

    public int Page { get; private set; }
    public ChronicleScreen Chronicle { get; private set; } = null!;

    public override void _Ready()
    {
        var first = Host.Session!.Chronicle(only: Only);
        Frame("ChronicleScreen", "The full chronicle", $"Years {first.FromYear} to {first.ToYear}.", $"{first.Total:N0} events recorded.");
        Facts.AddChild(Ui.Kicker("Systems"));
        var chips = new HFlowContainer { Name = "Filters" };
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        var pad = new MarginContainer { Name = "FiltersPad" };
        pad.AddThemeConstantOverride("margin_top", 10);
        pad.AddThemeConstantOverride("margin_bottom", 14);
        pad.AddChild(chips);
        Facts.AddChild(pad);
        foreach (var category in new Category?[] { null }.Concat(Enum.GetValues<Category>().Cast<Category?>()))
        {
            var button = Ui.Button(category is { } c ? Categories.Label(c) : "All", category?.ToString() ?? "All", primary: category == Only);
            button.Pressed += () => Filter(category);
            chips.AddChild(button);
        }
        var paging = Ui.Row("Paging", 6);
        var earlier = Ui.Button("Earlier", "Earlier");
        var later = Ui.Button("Later", "Later");
        earlier.Pressed += () => Show(Page - 1);
        later.Pressed += () => Show(Page + 1);
        foreach (var b in new[] { earlier, later }) b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        paging.AddChild(earlier);
        paging.AddChild(later);
        Facts.AddChild(Ui.Ruled("PagingRow", paging));
        Show(first.Pages);
    }

    public void Filter(Category? category)
    {
        Only = category;
        foreach (var child in GetNode("FactsCard/Facts/FiltersPad/Filters").GetChildren().OfType<Button>())
        {
            var active = child.Name == (category?.ToString() ?? "All");
            child.AddThemeStyleboxOverride("normal", Ui.Pill(active));
            child.AddThemeStyleboxOverride("hover", Ui.Pill(active));
            child.AddThemeColorOverride("font_color", active ? Colors.White : Ui.C(Palette.Ink));
        }
        Show(Host.Session!.Chronicle(only: Only).Pages);
    }

    /// <summary>Shows a page (1 = the oldest), clamped to the pages there are.</summary>
    public void Show(int page)
    {
        Chronicle = Host.Session!.Chronicle(only: Only, page: Math.Max(1, page));
        Page = Chronicle.Page;
        Ui.Clear(Body);
        foreach (var season in Chronicle.Seasons)
        {
            var section = Section(season.Label);
            for (var i = 0; i < season.Rows.Count; i++)
            {
                var row = season.Rows[i];
                var line = Ui.Row("Row", 8);
                line.AddChild(Ui.Dot(Palette.For(row.Category), 6));
                line.AddChild(Ui.Linked(row.Text, "Text", Host.Router.Open, 13, Palette.Ink));
                var ruled = Ui.Ruled($"Event{row.EventId}", line, i == season.Rows.Count - 1);
                var eventId = row.EventId;
                ruled.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true }) Host.Router.Explain(eventId);
                };
                section.AddChild(ruled);
            }
        }
        if (Chronicle.Total == 0) Body.AddChild(Ui.Body("Nothing of this kind is recorded yet.", "Empty", Palette.Muted));
        GetNode<Button>("FactsCard/Facts/PagingRow/Paging/Earlier").Disabled = Page <= 1;
        GetNode<Button>("FactsCard/Facts/PagingRow/Paging/Later").Disabled = Page >= Chronicle.Pages;
    }
}

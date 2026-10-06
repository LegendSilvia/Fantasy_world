using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A character's family tree: generations as rows of linked names, oldest at the top, the person ringed.</summary>
public partial class FamilyView : Control
{
    public Main Host { get; init; } = null!;
    public FamilyTree Tree { get; init; } = null!;

    public override void _Ready()
    {
        Name = "FamilyScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(Ui.Page());
        var column = Ui.Place(Ui.Column("Column", 16), 0, 0, 56, 40);
        column.CustomMinimumSize = new Vector2(1100, 0);
        column.AddChild(Ui.BackLink(() => Host.Router.Back()));
        var root = Tree.Nodes.First(n => n.Ref == Tree.Root);
        column.AddChild(Ui.Kicker("Family tree"));
        column.AddChild(Ui.FittedTitle($"The family of {root.Name}.", 56, 1000));
        var generation = Generations();
        foreach (var group in Tree.Nodes.GroupBy(n => generation[n.Ref]).OrderBy(g => g.Key))
        {
            column.AddChild(Ui.Kicker(group.Key switch
            {
                <= -2 => "Grandparents", -1 => "Parents", 0 => "Their generation", 1 => "Children", _ => "Grandchildren",
            }, $"Label{group.Key}"));
            var row = new HFlowContainer { Name = $"Generation{group.Key}" };
            row.AddThemeConstantOverride("h_separation", 10);
            row.AddThemeConstantOverride("v_separation", 10);
            foreach (var node in group.OrderBy(n => n.Born))
            {
                string Year(int y) => y < 1 ? "before Year 1" : $"Year {y}";
                var years = node.Died is { } died ? $"{Year(node.Born)} – {Year(died)}" : $"born {Year(node.Born)}";
                var chip = Ui.Capsule($"Person{node.Ref.Id}", Ui.C(node.Ref == Tree.Root ? Palette.Ink : Palette.Card), Ui.C(Palette.Border), 14, 8);
                var inner = Ui.Column("Inner", 2);
                inner.AddChild(Ui.NameLink(new Mention(node.Ref, node.Name), "Name", Host.Router.Open));
                inner.AddChild(Ui.Line(years, "Years", Palette.Muted, 11));
                if (node.Ref == Tree.Root)
                {
                    inner.GetNode<RichTextLabel>("Name").AddThemeColorOverride("default_color", Ui.C(Palette.Card));
                    inner.GetNode<Label>("Years").AddThemeColorOverride("font_color", new Color(0.73f, 0.72f, 0.69f));
                }
                chip.AddChild(inner);
                chip.CustomMinimumSize = new Vector2(180, 0);
                row.AddChild(chip);
            }
            column.AddChild(row);
        }
        AddChild(column);
    }

    /// <summary>Generation of each person relative to the root (parents −1, children +1), from the parent edges.</summary>
    private Dictionary<Sim.Events.EntityRef, int> Generations()
    {
        var generation = new Dictionary<Sim.Events.EntityRef, int> { [Tree.Root] = 0 };
        var queue = new Queue<Sim.Events.EntityRef>(new[] { Tree.Root });
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var edge in Tree.Edges)
            {
                var (other, step) = edge.From == at ? (edge.To, edge.Kind == "parent" ? 1 : 0)
                    : edge.To == at ? (edge.From, edge.Kind == "parent" ? -1 : 0) : (default, 0);
                if (other == default || generation.ContainsKey(other)) continue;
                generation[other] = generation[at] + step;
                queue.Enqueue(other);
            }
        }
        foreach (var node in Tree.Nodes) generation.TryAdd(node.Ref, 0);
        return generation;
    }
}

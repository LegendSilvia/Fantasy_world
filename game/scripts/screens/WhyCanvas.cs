using FantasyWorld.Client;
using Godot;

namespace FantasyWorld.Game;

/// <summary>Draws the chain's connectors beneath the cards, or (as a second layer) the root cards' dashed outlines above them.</summary>
public partial class WhyCanvas : Control
{
    public WhyView View { get; init; } = null!;

    /// <summary>Draw the root cards' dashed outlines (above the cards) instead of the connectors (beneath them).</summary>
    public bool Outlines { get; init; }

    public override void _Draw()
    {
        var ink = Ui.C(Palette.Ink);
        if (!Outlines)
        {
            foreach (var connector in View.Graph.Connectors)
                for (var i = 0; i + 1 < connector.Points.Count; i++)
                    DrawLine(ToV(connector.Points[i]), ToV(connector.Points[i + 1]), ink, 1.2f, true);
            return;
        }
        foreach (var box in View.Graph.Cards.Where(b => b.Card.IsRoot && b.Layer > 0))
        {
            var rect = new Rect2(box.X, box.Y, box.Width, WhyLayout.CardHeight);
            var corners = new[] { rect.Position, rect.Position + new Vector2(rect.Size.X, 0), rect.End, rect.Position + new Vector2(0, rect.Size.Y), rect.Position };
            for (var i = 0; i < 4; i++) DrawDashedLine(corners[i], corners[i + 1], ink, 1.5f, 6, true);
        }
    }

    private static Vector2 ToV(System.Numerics.Vector2 v) => new(v.X, v.Y);
}

using System.Numerics;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>Where a why-chain card sits (pixels, top-left) and how wide it is.</summary>
public sealed record CardBox(EventCard Card, float X, float Y, float Width, int Layer);

/// <summary>An elbow line from an effect's bottom to a cause's top, and the word written on it.</summary>
public sealed record Connector(long From, long To, IReadOnlyList<Vector2> Points, string Label);

public sealed record WhyGraph(IReadOnlyList<CardBox> Cards, IReadOnlyList<Connector> Connectors, float Width, float Height);

/// <summary>
/// Lays a why chain out top-down like the design (sub-project 4 §4): the outcome on top, each layer of causes below
/// it centred in rows (wrapping when a layer is wider than the space), cards narrowing to fit, and elbow connectors
/// from each effect down to its causes, labelled "because" or "because, and".
/// </summary>
public static class WhyLayout
{
    public const float CardHeight = 128;
    public const float RowGap = 64;
    public const float ColumnGap = 24;
    public const float MaxCard = 300;
    public const float MinCard = 210;

    public static WhyGraph Layout(WhyScreen why, float width)
    {
        var cards = new List<CardBox>();
        var y = 0f;
        var layers = new List<IReadOnlyList<EventCard>> { new[] { why.Outcome } };
        layers.AddRange(why.Layers);
        for (var layer = 0; layer < layers.Count; layer++)
        {
            var perRow = Math.Max(1, (int)((width + ColumnGap) / (MinCard + ColumnGap)));
            foreach (var row in layers[layer].Chunk(perRow))
            {
                var cardWidth = Math.Min(layer == 0 ? MaxCard + 80 : MaxCard, (width - ColumnGap * (row.Length - 1)) / row.Length);
                var rowWidth = row.Length * cardWidth + (row.Length - 1) * ColumnGap;
                var x = (width - rowWidth) / 2;
                foreach (var card in row)
                {
                    cards.Add(new CardBox(card, x, y, cardWidth, layer));
                    x += cardWidth + ColumnGap;
                }
                y += CardHeight + RowGap;
            }
        }
        var at = cards.ToDictionary(c => c.Card.EventId);
        var connectors = new List<Connector>();
        foreach (var edge in why.Edges)
        {
            if (!at.TryGetValue(edge.To, out var effect) || !at.TryGetValue(edge.From, out var cause)) continue;
            var start = new Vector2(effect.X + effect.Width / 2, effect.Y + CardHeight);
            var end = new Vector2(cause.X + cause.Width / 2, cause.Y);
            var bend = end.Y > start.Y ? end.Y - RowGap / 2 : start.Y + RowGap / 2;
            connectors.Add(new Connector(edge.From, edge.To, new[] { start, new Vector2(start.X, bend), new Vector2(end.X, bend), end }, edge.Label));
        }
        return new WhyGraph(cards, connectors, width, Math.Max(0, y - RowGap));
    }
}

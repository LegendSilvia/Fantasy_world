namespace FantasyWorld.Client;

/// <summary>A label's box on screen (pixels) and how much it matters.</summary>
public readonly record struct LabelBox(float X, float Y, float Width, float Height, int Priority)
{
    public bool Overlaps(LabelBox other, float margin = 2) =>
        X < other.X + other.Width + margin && other.X < X + Width + margin && Y < other.Y + other.Height + margin && other.Y < Y + Height + margin;
}

/// <summary>
/// Keeps screen labels from piling up (sub-project 4 §3): the most important labels are placed first and any label
/// that would overlap one already placed, or a reserved area such as a card, or leave the screen, is hidden.
/// </summary>
public static class Declutter
{
    public static bool[] Visible(IReadOnlyList<LabelBox> labels, float screenWidth, float screenHeight, IEnumerable<LabelBox>? reserved = null)
    {
        var placed = new List<LabelBox>(reserved ?? Array.Empty<LabelBox>());
        var visible = new bool[labels.Count];
        foreach (var i in Enumerable.Range(0, labels.Count).OrderByDescending(i => labels[i].Priority).ThenBy(i => i))
        {
            var box = labels[i];
            if (box.X < 0 || box.Y < 0 || box.X + box.Width > screenWidth || box.Y + box.Height > screenHeight) continue;
            if (placed.Any(p => p.Overlaps(box))) continue;
            placed.Add(box);
            visible[i] = true;
        }
        return visible;
    }
}

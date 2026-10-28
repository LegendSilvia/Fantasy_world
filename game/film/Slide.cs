using Godot;

namespace FantasyWorld.Game.Film;

/// <summary>
/// A holder that lays its child out as any container would, then moves, scales and fades it by its own amounts, so a piece
/// of the face-off can fly in while its place in the row stays put (a container resets its children's position and scale).
/// </summary>
public partial class Slide : Container
{
    public Vector2 Shift { get; set; }
    public float Zoom { get; set; } = 1;
    public float Turn { get; set; }

    public override Vector2 _GetMinimumSize()
    {
        var size = Vector2.Zero;
        foreach (var node in GetChildren())
            if (node is Control c) size = size.Max(c.GetCombinedMinimumSize());
        return size;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren) Place();
    }

    /// <summary>Puts the child back in its place, then shifted, scaled and turned about its middle.</summary>
    public void Place()
    {
        foreach (var node in GetChildren())
            if (node is Control c)
            {
                FitChildInRect(c, new Rect2(Vector2.Zero, Size));
                c.PivotOffset = Size / 2;
                c.Scale = new Vector2(Zoom, Zoom);
                c.Rotation = Turn;
                c.Position += Shift;
            }
    }
}

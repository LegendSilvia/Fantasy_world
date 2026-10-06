using Godot;

namespace FantasyWorld.Game;

/// <summary>A numbered pin's label on screen: the number in a black circle and the place in a pill.</summary>
public partial class PinTag : HBoxContainer
{
    public int Number { get; init; }
    public long EventId { get; init; }
}

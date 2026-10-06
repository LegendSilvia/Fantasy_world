using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A settlement's name tag on screen: a dot coloured by its news or holder, and its name in a pill.</summary>
public partial class SettlementTag : HBoxContainer
{
    public MapSettlement Settlement { get; init; } = null!;
}

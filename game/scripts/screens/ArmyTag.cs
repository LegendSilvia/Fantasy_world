using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A host's name tag on screen, above its banner.</summary>
public partial class ArmyTag : MarginContainer
{
    public MapArmy Army { get; init; } = null!;
}

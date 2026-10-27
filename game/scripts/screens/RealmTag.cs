using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A realm's name above its standard, always shown: the realm's colour beside its name and rank.</summary>
public partial class RealmTag : MarginContainer
{
    public MapRealm Realm { get; init; } = null!;
}

namespace FantasyWorld.Sim.Events;

/// <summary>Reference to any simulated entity, e.g. ("char", 88) or ("region", 3).</summary>
public readonly record struct EntityRef(string Kind, long Id)
{
    public override string ToString() => $"{Kind}#{Id}";
}

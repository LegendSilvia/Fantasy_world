namespace FantasyWorld.Sim.Engine;

/// <summary>A simulation system. Systems never call each other; they communicate through events.</summary>
public interface ISimSystem
{
    string Name { get; }

    void OnMonthlyTick(SimContext ctx);

    /// <summary>Scheduled-action kinds this system handles. Each kind has exactly one owner.</summary>
    IEnumerable<string> HandledKinds => Array.Empty<string>();

    void OnScheduled(SimContext ctx, ScheduledAction action) =>
        throw new NotSupportedException($"System '{Name}' does not handle scheduled actions.");
}

using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

/// <summary>What a system sees during one tick: the world, the log, the queue and its own RNG stream.</summary>
public sealed class SimContext
{
    public SimContext(World world, EventLog log, EventQueue queue, Rng rng)
    {
        World = world;
        Log = log;
        Queue = queue;
        Rng = rng;
    }

    public World World { get; }
    public EventLog Log { get; }
    public EventQueue Queue { get; }
    public Rng Rng { get; }
    public WorldDate Date => World.Date;

    public WorldEvent Emit(EventDraft draft) => Log.Append(World.Date, draft);
}

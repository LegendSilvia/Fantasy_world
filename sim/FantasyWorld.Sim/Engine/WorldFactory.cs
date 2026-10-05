using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public static class WorldFactory
{
    public static World CreateNew(ulong seed, DateTimeOffset nowUtc) => new() { Seed = seed, LastRealUtc = nowUtc };
}

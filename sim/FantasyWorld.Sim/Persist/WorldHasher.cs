using System.Security.Cryptography;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>SHA-256 over the serialized snapshot and event log. Used to prove determinism.</summary>
public static class WorldHasher
{
    public static string Hash(World world, EventLog log, EventQueue queue)
    {
        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(WorldSnapshot.Capture(world, queue), SimJson.Options);
        byte[] events = JsonSerializer.SerializeToUtf8Bytes(log.All, SimJson.Options);
        var all = new byte[snapshot.Length + events.Length];
        snapshot.CopyTo(all, 0);
        events.CopyTo(all, snapshot.Length);
        return Convert.ToHexString(SHA256.HashData(all));
    }
}

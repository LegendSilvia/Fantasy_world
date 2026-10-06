using System.Security.Cryptography;
using System.Text.Json;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Persist;

/// <summary>SHA-256 over the serialized snapshot, geography and event log. Used to prove determinism.</summary>
public static class WorldHasher
{
    public static string Hash(World world, EventLog log, EventQueue queue)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(WorldSnapshot.Capture(world, queue), SimJson.Options));
        if (world.Geography is { } geography)
            sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(geography, SimJson.Options));
        sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(log.All, SimJson.Options));
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}

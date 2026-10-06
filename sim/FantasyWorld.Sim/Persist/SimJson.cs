using System.Text.Json;

namespace FantasyWorld.Sim.Persist;

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
}

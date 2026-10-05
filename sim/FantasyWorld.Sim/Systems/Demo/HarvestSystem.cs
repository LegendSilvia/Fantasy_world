using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems.Demo;

/// <summary>Demo economy: a single grain store that drifts monthly and refills at harvest.</summary>
public sealed class HarvestSystem : ISimSystem
{
    public const string ShortageMarker = "grainShortage";

    private readonly int _minDelta;
    private readonly int _maxDeltaExclusive;

    public HarvestSystem(int minDelta = -25, int maxDeltaExclusive = 21)
    {
        _minDelta = minDelta;
        _maxDeltaExclusive = maxDeltaExclusive;
    }

    public string Name => "harvest";

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var grain = world.Stats.GetValueOrDefault("grain", 100) + ctx.Rng.NextInt(_minDelta, _maxDeltaExclusive);
        if (ctx.Date.Month == 9) grain += 40;
        grain = Math.Clamp(grain, 0, 200);
        world.Stats["grain"] = grain;

        var active = world.Markers.TryGetValue(ShortageMarker, out var shortageId);
        if (!active && grain < 40)
        {
            var shortage = ctx.Emit(new EventDraft("GrainShortageBegan",
                Payload: new Dictionary<string, string> { ["grain"] = grain.ToString("0", CultureInfo.InvariantCulture) },
                Importance: 40,
                Tags: new[] { "Economy" }));
            world.Markers[ShortageMarker] = shortage.Id;
        }
        else if (active && grain >= 60)
        {
            ctx.Emit(new EventDraft("GrainShortageEnded",
                Causes: new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) },
                Importance: 20,
                Tags: new[] { "Economy" }));
            world.Markers.Remove(ShortageMarker);
        }
    }
}

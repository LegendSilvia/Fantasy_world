using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Random test system: flips coins monthly, chains them causally, schedules echoes.</summary>
public sealed class CoinSystem : ISimSystem
{
    public string Name => "coin";
    public IEnumerable<string> HandledKinds => new[] { "echo" };

    public void OnMonthlyTick(SimContext ctx)
    {
        if (!ctx.Rng.Chance(0.5)) return;
        var causes = ctx.World.Markers.TryGetValue("lastCoin", out var last)
            ? new[] { new Cause(last, CauseKind.Enabler, 0.5) }
            : Array.Empty<Cause>();
        var coin = ctx.Emit(new EventDraft("Coin",
            Payload: new Dictionary<string, string> { ["v"] = ctx.Rng.NextInt(0, 1000).ToString(CultureInfo.InvariantCulture) },
            Causes: causes));
        ctx.World.Markers["lastCoin"] = coin.Id;
        ctx.World.Stats["coins"] = ctx.World.Stats.GetValueOrDefault("coins", 0) + 1;
        ctx.Queue.Schedule(ctx.Date.AddDays(ctx.Rng.NextInt(1, 20)), "echo",
            new Dictionary<string, string> { ["of"] = coin.Id.ToString(CultureInfo.InvariantCulture) });
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action) =>
        ctx.Emit(new EventDraft("Echo", Causes: new[]
        {
            new Cause(long.Parse(action.Data["of"], CultureInfo.InvariantCulture), CauseKind.Trigger, 1.0),
        }));
}

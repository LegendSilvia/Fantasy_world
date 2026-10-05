using System.Globalization;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Systems.Demo;

/// <summary>Demo politics: shortages raise unrest; high unrest starts a 90-day revolt.</summary>
public sealed class UnrestSystem : ISimSystem
{
    public const string RevoltMarker = "revolt";
    public const string RevoltEndKind = "revolt.end";

    public string Name => "unrest";

    public IEnumerable<string> HandledKinds => new[] { RevoltEndKind };

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var inShortage = world.Markers.TryGetValue(HarvestSystem.ShortageMarker, out var shortageId);
        var unrest = Math.Max(0, world.Stats.GetValueOrDefault("unrest", 0) + (inShortage ? 15 : -5));
        world.Stats["unrest"] = unrest;

        if (unrest < 50 || world.Markers.ContainsKey(RevoltMarker)) return;

        var causes = inShortage
            ? new[] { new Cause(shortageId, CauseKind.Trigger, 1.0) }
            : Array.Empty<Cause>();
        var revolt = ctx.Emit(new EventDraft("RevoltBegan", Causes: causes, Importance: 60, Tags: new[] { "Politics" }));
        world.Markers[RevoltMarker] = revolt.Id;
        ctx.Queue.Schedule(ctx.Date.AddDays(90), RevoltEndKind,
            new Dictionary<string, string> { ["revoltEventId"] = revolt.Id.ToString(CultureInfo.InvariantCulture) });
    }

    public void OnScheduled(SimContext ctx, ScheduledAction action)
    {
        var revoltId = long.Parse(action.Data["revoltEventId"], CultureInfo.InvariantCulture);
        ctx.Emit(new EventDraft("RevoltEnded",
            Causes: new[] { new Cause(revoltId, CauseKind.Trigger, 1.0) },
            Importance: 30,
            Tags: new[] { "Politics" }));
        ctx.World.Stats["unrest"] = 20;
        ctx.World.Markers.Remove(RevoltMarker);
    }
}

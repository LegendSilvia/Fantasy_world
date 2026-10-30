using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Levels (the owner, 2026-10-30): each year every adult earns the experience of their deeds that year (<see cref="Levels.For"/>)
/// and of the year lived, and rises as far as it takes them. Rising to the soft cap or past it is news; the levels below are
/// not. A child is level 1. Nothing here is rolled.
/// </summary>
public sealed class LevelSystem : ISimSystem
{
    public string Name => "levels";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        Levels.Backfill(world, ctx.Log);
        var since = ctx.Date.Day - Core.WorldDate.DaysPerYear;
        var earned = new Dictionary<long, double>();
        foreach (var e in ctx.Log.All.Where(e => e.Date.Day >= since && e.Date.Day < ctx.Date.Day))
            foreach (var (role, who) in e.Actors)
                if (who.Kind == Character.Kind)
                    earned[who.Id] = earned.GetValueOrDefault(who.Id) + Levels.For(e, role);
        foreach (var c in world.ActiveCharacters.OrderBy(c => c.Id).ToList())
        {
            if (c.Stage(ctx.Date) == LifeStage.Child) { c.Level = Math.Max(1, c.Level); continue; }
            c.Experience += Levels.Training(c) + earned.GetValueOrDefault(c.Id);
            var was = Math.Max(1, c.Level);
            c.Level = Math.Max(was, Levels.Of(c.Experience));
            if (c.Level > was && c.Level >= Levels.SoftCap)
                ctx.Emit(new EventDraft("LevelRaised", Actors: new Dictionary<string, EntityRef> { ["character"] = c.Ref },
                    Place: world.Settlements.TryGetValue(c.SettlementId, out var home) ? home.Ref : (EntityRef?)null,
                    Payload: new Dictionary<string, string> { ["level"] = c.Level.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    Importance: c.Level > Levels.SoftCap ? 20 + 4 * c.Level : 12, Tags: new[] { "Characters" }));
        }
    }
}

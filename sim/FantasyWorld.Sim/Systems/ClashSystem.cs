using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Fights no one declared (the owner, 2026-10-29), yearly: enemies who live near each other may come to blows without a
/// challenge or a day set. A schemer lays an ambush; a lord or knight plans an attack on the foe's home; anyone else who
/// meets their enemy fights then and there. Each is told as a clash under way, and decided the next year like a duel
/// (<see cref="DeedsSystem.Clash"/>). A few a year at most; rolled from the people themselves, not the world's dice.
/// </summary>
public sealed class ClashSystem : ISimSystem
{
    /// <summary>The most clashes a year, and each enemy pair's chance a year in a hundred.</summary>
    public const int MostAYear = 5, Chance = 10;

    public string Name => "clashes";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        var year = ctx.Date.Year;
        var begun = 0;
        var engaged = new HashSet<long>();
        foreach (var a in world.ActiveCharacters.Where(c => c.Stage(ctx.Date) != LifeStage.Child && !SagaSystem.IsDemonLord(world, c)).OrderBy(c => c.Id).ToList())
        {
            if (begun >= MostAYear) return;
            if (engaged.Contains(a.Id) || DeedsSystem.Engaged(ctx.Queue, a.Id) || Hash.Percent(a.Id, 9400, year) >= Chance) continue;
            var grudge = a.Memories.Where(m => m.Feeling <= -50 && world.Characters.TryGetValue(m.About, out var o) && o.IsActive && o.Id != a.Id
                                               && o.Stage(ctx.Date) != LifeStage.Child)
                .OrderBy(m => m.Feeling).ThenBy(m => m.EventId).FirstOrDefault();
            if (grudge is null) continue;
            var b = world.Characters[grudge.About];
            if (engaged.Contains(b.Id) || DeedsSystem.Engaged(ctx.Queue, b.Id) || SagaSystem.IsDemonLord(world, b)) continue;
            var (here, there) = (world.Settlements[a.SettlementId].RegionId, world.Settlements[b.SettlementId].RegionId);
            var near = here == there || graph is not null && graph.Neighbours(here).Contains(there);
            if (!near) continue;
            var schemer = a.Attributes.Intrigue >= 12 || a.Has(Traits.Deceitful) || a.Has(Traits.Cruel) || Classes.Base(a.Class) is Classes.Rogue or Classes.Ranger;
            var how = schemer ? DeedsSystem.Ambush
                : a.Role is Role.Lord or Role.Knight && a.SettlementId != b.SettlementId ? DeedsSystem.Attack
                : DeedsSystem.Encounter;
            var toTheDeath = how != DeedsSystem.Encounter || grudge.Feeling <= -70;
            DeedsSystem.Clash(ctx, a, b, toTheDeath, how, grudge.EventId);
            engaged.Add(a.Id);
            engaged.Add(b.Id);
            begun++;
        }
    }
}

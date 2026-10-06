using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Monsters (sub-project 3 §3). Each year a named beast may rise in a dangerous land (more often after a ley surge),
/// of a breed that fits the land, mightier where the land is wilder; beasts grow with age, and very old ones may
/// slumber until danger or a surge wakes them. Each month a roaming beast may raid a settlement in or next to its
/// land: people, food and wealth are lost, unrest rises, and the beast's notoriety grows until it becomes a legend.
/// Heroes answer them (hunts in <see cref="DeedsSystem"/>).
/// </summary>
public sealed class MonsterSystem(IInventor? inventor = null) : ISimSystem
{
    public const double RiseDanger = 55;
    public const double RisePerYear = 0.05;
    public const double RaidPerMonth = 0.03;
    public const double LegendNotoriety = 60;
    public const int SlumberAgeYears = 120;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "monsters";

    public static int Cap(World world) => Math.Max(1, world.Regions.Count / 8);

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.State == MonsterState.Roaming).ToList())
        {
            var lair = world.Regions[beast.RegionId];
            lair.Danger = Math.Max(lair.Danger, beast.Might / 2);
            if (!ctx.Rng.Chance(RaidPerMonth * (0.5 + beast.Might / 100))) continue;
            Raid(ctx, beast, lair);
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var beast in world.Monsters.Values.Where(m => m.Alive).ToList())
        {
            var age = (ctx.Date.Day - beast.BornDay) / WorldDateYears;
            if (beast.State == MonsterState.Roaming)
            {
                beast.Might = Math.Min(100, beast.Might + (age < 60 ? 1 : 0));
                if (age >= SlumberAgeYears && ctx.Rng.Chance(0.1))
                {
                    beast.State = MonsterState.Slumbering;
                    ctx.Emit(new EventDraft("MonsterSlumbered", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                        Place: world.Regions[beast.RegionId].Ref, Importance: 25, Tags: new[] { "Magic" }));
                }
            }
            else if (world.Regions[beast.RegionId].Conditions.TryGetValue(Conditions.LeySurge, out var surge) && ctx.Rng.Chance(0.3))
            {
                beast.State = MonsterState.Roaming;
                ctx.Emit(new EventDraft("MonsterWoke", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                    Place: world.Regions[beast.RegionId].Ref, Causes: new[] { new Cause(surge, CauseKind.Trigger, 0.9) },
                    Importance: 45, Tags: new[] { "Magic" }));
            }
        }
        Rise(ctx);
    }

    private const double WorldDateYears = Core.WorldDate.DaysPerYear;

    private void Rise(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Monsters.Values.Count(m => m.Alive) >= Cap(world)) return;
        var haunted = world.Monsters.Values.Where(m => m.Alive).Select(m => m.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.Danger >= RiseDanger && !haunted.Contains(r.Id)).ToList())
        {
            var surge = region.Conditions.TryGetValue(Conditions.LeySurge, out var surgeEvent);
            if (!ctx.Rng.Chance(RisePerYear * (surge ? 3 : 1) * region.Danger / 100)) continue;
            var breeds = Monster.BreedsOf(region.Biome);
            var breed = breeds[ctx.Rng.NextInt(0, breeds.Count)];
            var id = world.Ids.Take(Monster.Kind);
            var invention = _inventor.Invent(new InventionBrief("monster", $"{Monster.Kind}#{id}",
                $"A {breed} that rose in the wild land of {region.Name} ({region.Biome}) in year {ctx.Date.Year}.",
                $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {region.Name}"));
            using var lore = ctx.Annotate("lore", invention.Lore);
            var rose = ctx.Emit(new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
                Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["breed"] = breed },
                Causes: Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.8), (Conditions.Dangerous, CauseKind.Enabler, 0.6)),
                Importance: 40, Tags: new[] { "Magic" }));
            world.Monsters[id] = new Monster
            {
                Id = id, Name = invention.Name, Breed = breed, RegionId = region.Id, Might = Math.Clamp(20 + region.Danger / 2 + ctx.Rng.NextInt(0, 21), 1, 100),
                BornDay = ctx.Date.Day, State = MonsterState.Roaming, RoseEventId = rose.Id,
            };
            return; // at most one a year
        }
    }

    private static void Raid(SimContext ctx, Monster beast, Region lair)
    {
        var world = ctx.World;
        var graph = world.Geography?.Graph;
        var nearby = new[] { lair.Id }.Concat(graph?.Neighbours(lair.Id) ?? Array.Empty<long>()).ToHashSet();
        var target = world.Settlements.Values.Where(s => !s.Abandoned && nearby.Contains(s.RegionId))
            .OrderBy(s => s.RegionId == lair.Id ? 0 : 1).ThenByDescending(s => s.Population).ThenBy(s => s.Id).FirstOrDefault();
        if (target is null) return;
        var share = beast.Might / 1000.0; // up to a tenth of the people
        var dead = (int)(target.Adults * share);
        target.Adults -= dead;
        world.Regions[target.RegionId].Food *= 1 - share;
        target.Wealth *= 1 - share;
        target.Unrest = Math.Min(100, target.Unrest + 15);
        beast.Notoriety = Math.Min(100, beast.Notoriety + 5 + beast.Might / 10);
        var previous = ctx.Log.Involving(beast.Ref).LastOrDefault();
        var raid = ctx.Emit(new EventDraft("MonsterRaid", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
            Place: target.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Causes: previous is null ? null : new[] { new Cause(previous.Id, CauseKind.Enabler, 0.5) },
            Importance: (int)Math.Clamp(25 + beast.Might / 3, 25, 60), Tags: new[] { "Magic" }));
        // Merchants shun a land a beast has raided (the spec's raid that severs a road).
        foreach (var route in world.Routes.Values.Where(r => r.State == RouteState.Open && r.Regions.Contains(target.RegionId)).ToList())
            TradeSystem.Sever(ctx, route, "raid", target.Ref, raid.Id);
        if (!beast.Legendary && beast.Notoriety >= LegendNotoriety)
        {
            beast.Legendary = true;
            ctx.Emit(new EventDraft("MonsterLegendary", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                Place: lair.Ref, Causes: new[] { new Cause(beast.RoseEventId, CauseKind.Enabler, 0.6) }, Importance: 60, Tags: new[] { "Magic" }));
        }
    }
}

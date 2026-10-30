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
        foreach (var beast in world.Monsters.Values.Where(m => m.State is MonsterState.Roaming or MonsterState.Lairing).ToList())
        {
            var lair = world.Regions[beast.RegionId];
            lair.Danger = Math.Max(lair.Danger, beast.Might / 2);
            if (beast.State == MonsterState.Lairing || !ctx.Rng.Chance(RaidPerMonth * (0.5 + beast.Might / 100))) continue;
            Raid(ctx, beast, lair);
        }
        Witness(ctx);
    }

    /// <summary>
    /// A beast is known once someone faces it and lives (the owner, 2026-10-29): of the month's events where a person met an
    /// unseen beast (a hunt, a raid sworn on, a delve to its lair, a fight) the first whose person did not die of it reveals
    /// the beast, told as news; until then it is an unseen beast to the chronicle's audience.
    /// </summary>
    private static void Witness(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Monsters.Values.All(m => m.Revealed)) return;
        var since = ctx.Date.Day - 31;
        var month = new List<WorldEvent>();
        for (var i = ctx.Log.All.Count - 1; i >= 0 && ctx.Log.All[i].Date.Day > since; i--) month.Add(ctx.Log.All[i]);
        month.Reverse();
        foreach (var ev in month.Where(e => e.Type != "BeastSeen"))
            foreach (var beastRef in ev.Actors.Values.Where(r => r.Kind == Monster.Kind).Distinct())
            {
                if (!world.Monsters.TryGetValue(beastRef.Id, out var beast) || beast.Revealed) continue;
                var witness = ev.Actors.Values.Where(r => r.Kind == Character.Kind).Select(r => world.Characters.GetValueOrDefault(r.Id)).OfType<Character>()
                    .FirstOrDefault(c => c.IsActive || !month.Any(d => d.Type == "CharacterDied" && d.Actors.GetValueOrDefault("deceased") == c.Ref
                                                                      && d.Causes.Any(k => k.EventId == ev.Id)));
                if (witness is null) continue;
                var seen = ctx.Emit(new EventDraft("BeastSeen",
                    Actors: new Dictionary<string, EntityRef> { ["witness"] = witness.Ref, ["monster"] = beast.Ref },
                    Place: ev.Place, Payload: new Dictionary<string, string> { ["breed"] = beast.Breed, ["name"] = beast.Name },
                    Causes: new[] { new Cause(ev.Id, CauseKind.Trigger, 1.0) },
                    Importance: Math.Max(45, Fates.Importance(witness, 40)), Tags: new[] { "Characters", "Magic" }));
                beast.RevealedEventId = seen.Id;
                beast.RevealedById = witness.Id;
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
            else if (beast.State == MonsterState.Slumbering && world.Regions[beast.RegionId].Conditions.TryGetValue(Conditions.LeySurge, out var surge)
                     && ctx.Rng.Chance(0.3))
            {
                beast.State = MonsterState.Roaming;
                ctx.Emit(new EventDraft("MonsterWoke", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
                    Place: world.Regions[beast.RegionId].Ref, Causes: new[] { new Cause(surge, CauseKind.Trigger, 0.9) },
                    Importance: 45, Tags: new[] { "Magic" }));
            }
        }
        RiseFromTheSea(ctx);
        Rise(ctx);
    }

    public const double SeaRisePerYear = 0.04;

    /// <summary>
    /// Sea monsters (sub-project 13): in a world of several continents, now and then a beast of the deep rises off a port
    /// where ships cross, at most one for each continent at a time; it preys on the hosts that cross there.
    /// </summary>
    private void RiseFromTheSea(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Continents.Count < 2 || world.Geography is not { } geography) return;
        if (world.Monsters.Values.Count(m => m.Alive && m.AtSea) >= world.Continents.Count) return;
        var haunted = world.Monsters.Values.Where(m => m.Alive).Select(m => m.RegionId).ToHashSet();
        var ports = geography.Graph.Links.Where(l => l.Kind == LinkKind.Sea).SelectMany(l => new[] { l.A, l.B }).Distinct()
            .Where(id => !haunted.Contains(id)).Order().ToList();
        if (ports.Count == 0 || !ctx.Rng.Chance(SeaRisePerYear)) return;
        var region = world.Regions[ports[ctx.Rng.NextInt(0, ports.Count)]];
        var breed = Monster.SeaBreeds[ctx.Rng.NextInt(0, Monster.SeaBreeds.Count)];
        var id = world.Ids.Take(Monster.Kind);
        var invention = _inventor.Invent(new InventionBrief("monster", $"{Monster.Kind}#{id}",
            $"A {breed} that rose from the sea off {region.Name}, where ships cross between the continents, in year {ctx.Date.Year}.",
            $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {region.Name}"));
        using var lore = ctx.Annotate("lore", invention.Lore);
        var rose = ctx.Emit(new EventDraft("MonsterRose", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["breed"] = breed },
            Importance: 45, Tags: new[] { "Magic" }));
        world.Monsters[id] = new Monster
        {
            Id = id,
            Name = invention.Name,
            Breed = breed,
            RegionId = region.Id,
            Might = Math.Clamp(45 + ctx.Rng.NextInt(0, 31), 1, 100),
            BornDay = ctx.Date.Day,
            State = MonsterState.Roaming,
            RoseEventId = rose.Id,
        };
    }

    private const double WorldDateYears = Core.WorldDate.DaysPerYear;

    private void Rise(SimContext ctx)
    {
        var world = ctx.World;
        if (world.Monsters.Values.Count(m => m.State is MonsterState.Roaming or MonsterState.Slumbering) >= Cap(world)) return; // guardians aside
        var haunted = world.Monsters.Values.Where(m => m.Alive).Select(m => m.RegionId).ToHashSet();
        foreach (var region in world.Regions.Values.Where(r => r.Danger >= RiseDanger && !haunted.Contains(r.Id)).ToList())
        {
            var surge = region.Conditions.TryGetValue(Conditions.LeySurge, out var surgeEvent);
            if (!ctx.Rng.Chance(RisePerYear * (surge ? 3 : 1) * region.Danger / 100)) continue;
            // The land's breeds and the continent's own (sub-project 13).
            var breeds = Monster.BreedsOf(region.Biome).Concat(world.Continents.GetValueOrDefault(region.ContinentId)?.Breeds ?? []).ToList();
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
                Id = id,
                Name = invention.Name,
                Breed = breed,
                RegionId = region.Id,
                Might = Math.Clamp(20 + region.Danger / 2 + ctx.Rng.NextInt(0, 21), 1, 100),
                BornDay = ctx.Date.Day,
                State = MonsterState.Roaming,
                RoseEventId = rose.Id,
            };
            return; // at most one a year
        }
    }

    /// <summary>A mighty beast's raid takes a notable's life at most this often (its might in a hundred, times this).</summary>
    public const double KillShare = 0.35;

    /// <summary>How often a bold survivor of a raid swears to slay the beast.</summary>
    public const double SwearChance = 0.35;

    /// <summary>
    /// A raid falls on the story's people too (the owner, 2026-10-29: an age full of beasts shapes the lives and desires of its
    /// people): it may kill one of the town's notables, and the bold who survive it may swear to slay the beast, and so hunt it.
    /// </summary>
    private static void Befall(SimContext ctx, Monster beast, Settlement target, WorldEvent raid)
    {
        var world = ctx.World;
        var people = world.PeopleOf(target).Where(c => c.IsActive && c.Stage(ctx.Date) != LifeStage.Child && !SagaSystem.IsDemonLord(world, c))
            .OrderBy(c => c.Id).ToList();
        if (people.Count == 0) return;
        if (ctx.Rng.Chance(beast.Might / 100.0 * KillShare))
        {
            var victim = people[ctx.Rng.NextInt(0, people.Count)];
            Fates.Die(ctx, victim, beast.Name, new[] { new Cause(raid.Id, CauseKind.Trigger, 1.0) });
            people.Remove(victim);
        }
        foreach (var c in people.Where(c => !c.Has(Traits.Craven) && c.Ambition != Ambitions.Renown && c.Ambition != Ambitions.Vengeance))
        {
            // The bold swear readily; anyone but a coward may, having seen the beast at their gate.
            if (!ctx.Rng.Chance(c.Has(Traits.Brave) || c.Has(Traits.Wrathful) ? SwearChance : SwearChance / 3)) continue;
            c.Ambition = Ambitions.Renown;
            c.AmbitionTarget = 0;
            ctx.Emit(new EventDraft("SwornAgainstBeast", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref, ["monster"] = beast.Ref },
                Place: target.Ref, Causes: new[] { new Cause(raid.Id, CauseKind.Motive, 0.9) }, Importance: Fates.Importance(c, 14), Tags: new[] { "Characters" }));
            break; // one oath a raid
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
        // A town's fortification (its level, the owner, 2026-10-30) keeps a beast out: one well below it is driven off, and
        // what gets in does less harm the stronger the walls.
        var guard = Levels.Of(world, target);
        if (Levels.Of(beast) + Levels.RepelMargin <= guard)
        {
            ctx.Emit(new EventDraft("RaidRepelled", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref }, Place: target.Ref,
                Payload: new Dictionary<string, string> { ["level"] = guard.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                Importance: 20, Tags: new[] { "Magic", "Settlements" }));
            return;
        }
        var share = beast.Might / 1000.0 * Levels.Ward(guard); // up to a tenth of the people, less behind walls
        var dead = (int)(target.Adults * share);
        target.Adults -= dead;
        world.Regions[target.RegionId].Food *= 1 - share;
        target.Wealth *= 1 - share;
        target.Unrest = Math.Min(100, target.Unrest + 15);
        beast.Notoriety = Math.Min(100, beast.Notoriety + 5 + beast.Might / 10);
        // The beast's own last deed leads to this one: its rising or last raid, never what others did about it (a prayer for
        // deliverance from it, a hunt) — those are not why it raids.
        var previous = ctx.Log.Involving(beast.Ref).LastOrDefault(e => e.Type is "MonsterRose" or "LabyrinthOutbreak" or "MonsterRaid" or "MonsterLegendary"
                                                                       or "MonsterWoke" or "MonsterSlumbered" or "MonstersMigrated");
        var raid = ctx.Emit(new EventDraft("MonsterRaid", Actors: new Dictionary<string, EntityRef> { ["monster"] = beast.Ref },
            Place: target.Ref, Payload: new Dictionary<string, string> { ["dead"] = dead.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            // A beast that came of a world event (an elder dragon's waking, a great labyrinth's outbreak) raids because of it.
            Causes: new[] { new Cause(previous?.Id ?? beast.RoseEventId, CauseKind.Enabler, 0.5) },
            Importance: (int)Math.Clamp(25 + beast.Might / 3, 25, 60), Tags: new[] { "Magic" }));
        Befall(ctx, beast, target, raid);
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

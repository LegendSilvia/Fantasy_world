using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The world events besides the demon lord (the owner, 2026-10-30), each needing many together and a long time to end
/// (companies, <see cref="DeedsSystem"/>):
/// <list type="bullet">
/// <item>The Great Labyrinth: once in a world's age a great labyrinth opens, twelve levels deep, asking level 6 of its
/// delvers, a level-9 guardian at its heart; it is delved a few levels at a time by companies, over years.</item>
/// <item>The Great Labyrinth Outbreak: while it stands unconquered, now and then it spills beasts across the lands around it;
/// the outbreak ends when they are all slain, or the labyrinth is cleared.</item>
/// <item>The Elder Dragon: a level-10 dragon wakes in the mountains and preys on the realms; only companies can face it,
/// each bout they win wearing it down, until one slays it.</item>
/// </list>
/// Each rolls from the world and the year, never the world's dice, so a world without them is unchanged.
/// </summary>
public sealed class GreatEventsSystem : ISimSystem
{
    private readonly IInventor _inventor;

    public GreatEventsSystem(IInventor? inventor = null) => _inventor = inventor ?? new RulesInventor();

    public string Name => "great events";

    /// <summary>A world must be this old before a great labyrinth or an elder dragon may come.</summary>
    public const int GreatAge = 40;

    /// <summary>The chance each year, in a hundred: the great labyrinth opens, it spills an outbreak, an elder dragon wakes.</summary>
    public const int GreatLabyrinthChance = 6, OutbreakChance = 20, ElderDragonChance = 4;

    public const int GreatLevels = 12, GreatRequiredLevel = 6, OutbreakBeasts = 3;

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        if (ctx.Date.Year < GreatAge) return;
        var seed = (long)(world.Seed % long.MaxValue);
        bool Roll(int salt, int chance) => Hash.Percent(seed, salt, ctx.Date.Year) < chance;

        var great = world.Structures.Values.FirstOrDefault(s => s.Great);
        if (great is null && Roll(7501, GreatLabyrinthChance)) great = OpenGreatLabyrinth(ctx);
        if (great is { ClearedEventId: 0 })
        {
            var outbreak = OpenOutbreak(ctx, great);
            if (outbreak is null && Roll(7502, OutbreakChance)) Outbreak(ctx, great);
        }
        // An outbreak ends with its beasts, or with the labyrinth that spilled them.
        if (great is not null && OpenOutbreak(ctx, great) is { } open
            && (great.ClearedEventId != 0 || world.Monsters.Values.Where(m => m.RoseEventId == open.Id).All(m => !m.Alive)))
            ctx.Emit(new EventDraft("GreatOutbreakEnded", Actors: new Dictionary<string, EntityRef> { ["structure"] = great.Ref },
                Place: world.Regions[great.RegionId].Ref, Causes: new[] { new Cause(great.ClearedEventId != 0 ? great.ClearedEventId : open.Id, CauseKind.Trigger, 0.8) },
                Importance: 60, Tags: new[] { "Magic", "Structures" }));

        if (!world.Monsters.Values.Any(m => m.Elder && m.Alive) && Roll(7503, ElderDragonChance)) WakeElderDragon(ctx);
        // An elder dragon heals between hunts, a little each year.
        foreach (var dragon in world.Monsters.Values.Where(m => m.Elder && m.Alive)) dragon.Vigour = Math.Min(100, dragon.Vigour + 10);
    }

    /// <summary>The great labyrinth's outbreak still running: its last outbreak, if no end has been told since.</summary>
    public static WorldEvent? OpenOutbreak(SimContext ctx, Structure great)
    {
        var last = ctx.Log.Involving(great.Ref).LastOrDefault(e => e.Type is "GreatOutbreak" or "GreatOutbreakEnded");
        return last?.Type == "GreatOutbreak" ? last : null;
    }

    private Structure? OpenGreatLabyrinth(SimContext ctx)
    {
        var world = ctx.World;
        var region = world.Regions.Values.Where(r => !world.Structures.Values.Any(s => s.RegionId == r.Id && s.Type == StructureKind.Labyrinth))
            .Where(r => r.Biome is not Biome.Ocean && world.Settlements.Values.All(s => s.RegionId != r.Id || s.Abandoned) || r.LeyPower >= 60)
            .OrderByDescending(r => r.LeyPower + r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (region is null) return null;
        var id = world.Ids.Take(Structure.Kind);
        var guardianId = world.Ids.Take(Monster.Kind);
        var invention = _inventor.Invent(new InventionBrief("labyrinth", $"{Structure.Kind}#{id}",
            $"The Great Labyrinth, twelve levels deep, that opened in {region.Name} in year {ctx.Date.Year}: a world event, too deep for any one delver.",
            $"The Great Labyrinth of {region.Name}"));
        var breed = Monster.GuardianBreeds[Hash.Percent(id, 7504) % Monster.GuardianBreeds.Count];
        using var lore = ctx.Annotate("lore", invention.Lore);
        var opened = ctx.Emit(new EventDraft("GreatLabyrinthRose",
            Actors: new Dictionary<string, EntityRef> { ["structure"] = new(Structure.Kind, id), ["guardian"] = new(Monster.Kind, guardianId) },
            Place: region.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["levels"] = GreatLevels.ToString(CultureInfo.InvariantCulture) },
            Importance: 90, Tags: new[] { "Structures", "Magic" }));
        var maze = new Structure
        {
            Id = id,
            Name = invention.Name,
            Type = StructureKind.Labyrinth,
            RegionId = region.Id,
            StartedEventId = opened.Id,
            CompletedEventId = opened.Id,
            Progress = 100,
            State = StructureState.Standing,
            Levels = GreatLevels,
            RequiredLevel = GreatRequiredLevel,
            Great = true,
            GuardianId = guardianId,
        };
        world.Structures[id] = maze;
        var deeps = invention.Name.StartsWith("The ", StringComparison.Ordinal) ? "the " + invention.Name[4..] : invention.Name;
        world.Monsters[guardianId] = new Monster
        {
            Id = guardianId,
            Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {deeps}",
            Breed = breed,
            RegionId = region.Id,
            Might = 100,
            Level = Levels.Max - 1,
            BornDay = ctx.Date.Day,
            State = MonsterState.Lairing,
            RoseEventId = opened.Id,
            LairStructureId = id,
            RevealedEventId = opened.Id,
        };
        region.Danger = Math.Min(100, region.Danger + 25);
        return maze;
    }

    private void Outbreak(SimContext ctx, Structure great)
    {
        var world = ctx.World;
        var home = world.Regions[great.RegionId];
        var lands = new[] { home.Id }.Concat(world.Geography?.Graph.Neighbours(home.Id) ?? Array.Empty<long>())
            .Select(id => world.Regions[id]).Where(r => r.Biome is not Biome.Ocean).OrderBy(r => r.Id).ToList();
        if (lands.Count == 0) return;
        var outbreak = ctx.Emit(new EventDraft("GreatOutbreak", Actors: new Dictionary<string, EntityRef> { ["structure"] = great.Ref },
            Place: home.Ref, Payload: new Dictionary<string, string> { ["beasts"] = OutbreakBeasts.ToString(CultureInfo.InvariantCulture) },
            Causes: new[] { new Cause(great.StartedEventId, CauseKind.Enabler, 0.8) }, Importance: 75, Tags: new[] { "Magic", "Structures" }));
        for (var i = 0; i < OutbreakBeasts; i++)
        {
            var land = lands[Hash.Percent(outbreak.Id, 7510 + i) % lands.Count];
            var breeds = Monster.BreedsOf(land.Biome);
            var breed = breeds[Hash.Percent(outbreak.Id, 7520 + i) % breeds.Count];
            var id = world.Ids.Take(Monster.Kind);
            world.Monsters[id] = new Monster
            {
                Id = id,
                Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of the Outbreak of {land.Name}",
                Breed = breed,
                RegionId = land.Id,
                Might = 60 + Hash.Percent(outbreak.Id, 7530 + i) / 5,
                BornDay = ctx.Date.Day,
                State = MonsterState.Roaming,
                RoseEventId = outbreak.Id,
                // The world saw them pour out: they are known from the first.
                RevealedEventId = outbreak.Id,
            };
            land.Danger = Math.Min(100, land.Danger + 15);
        }
    }

    private void WakeElderDragon(SimContext ctx)
    {
        var world = ctx.World;
        var lair = world.Regions.Values.Where(r => r.Biome is Biome.Mountain or Biome.Hills or Biome.Tundra)
            .OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault() ?? world.Regions.Values.OrderByDescending(r => r.Danger).ThenBy(r => r.Id).FirstOrDefault();
        if (lair is null) return;
        var id = world.Ids.Take(Monster.Kind);
        var invention = _inventor.Invent(new InventionBrief("monster", $"{Monster.Kind}#{id}",
            $"An elder dragon, older than the realms, that woke in {lair.Name} in year {ctx.Date.Year}: a world event, slain only by many together.",
            $"The Elder Dragon of {lair.Name}"));
        using var lore = ctx.Annotate("lore", invention.Lore);
        var woke = ctx.Emit(new EventDraft("ElderDragonWoke", Actors: new Dictionary<string, EntityRef> { ["monster"] = new(Monster.Kind, id) },
            Place: lair.Ref, Payload: new Dictionary<string, string> { ["name"] = invention.Name, ["breed"] = "elder dragon" },
            Importance: 90, Tags: new[] { "Magic" }));
        world.Monsters[id] = new Monster
        {
            Id = id,
            Name = invention.Name,
            Breed = "elder dragon",
            RegionId = lair.Id,
            Might = 100,
            Level = Levels.Max,
            Elder = true,
            Vigour = 100,
            Legendary = true,
            Notoriety = 80,
            BornDay = ctx.Date.Day,
            State = MonsterState.Roaming,
            RoseEventId = woke.Id,
            RevealedEventId = woke.Id,
        };
        lair.Danger = Math.Min(100, lair.Danger + 30);
    }
}

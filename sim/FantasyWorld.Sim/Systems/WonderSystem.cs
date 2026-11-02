using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Natural wonders (the owner, 2026-11-02, after Civilization VI): places the land itself made, a few to a world, one to a
/// land, unknown until someone comes upon them on a deed or a journey there; the one who discovers a wonder is remembered by
/// it, and only then is it on the map. Some keep a treasure made of the place itself, guarded by a beast that wakes when the
/// wonder is found; whoever slays the guardian takes the treasure. Placed once in a world that has none (an old world gains
/// them the year this comes), rolled from the world's seed and the year, never the world's dice.
/// </summary>
public sealed class WonderSystem(IArbiter? arbiter = null, IInventor? inventor = null) : ISimSystem
{
    public string Name => "wonders";

    public const int DiscoverChance = 15, ChallengeChance = 20, GuardedShare = 35;

    private readonly IArbiter _arbiter = arbiter ?? new RulesArbiter();
    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    /// <summary>A kind of wonder: what it is, and the treasure it may keep.</summary>
    public sealed record Kind(string Name, string What, string Treasure);

    private static readonly Dictionary<Biome, Kind[]> Kinds = new()
    {
        [Biome.Mountain] = [new("Crystal Caverns", "caves of living crystal that ring when the wind passes", "a blade of singing crystal"),
            new("Skyspire", "a needle of rock so tall its top is lost in cloud", "a crown of cloudstone")],
        [Biome.Hills] = [new("Sky Arch", "a great stone arch the wind carved over a thousand thousand years", "a bow of windworn stone"),
            new("Singing Stones", "standing stones no hand raised, that hum at dusk", "a shield of humming stone")],
        [Biome.Forest] = [new("World Tree", "a tree older than every realm, its roots under three lands", "a staff of the World Tree's heartwood"),
            new("Glowing Grove", "a grove whose leaves shine silver at night", "a cloak woven of shining leaves")],
        [Biome.Jungle] = [new("Emerald Falls", "falls that pour green light into a deep pool", "a spear of green falling-water"),
            new("Glowing Grove", "a grove whose leaves shine silver at night", "a cloak woven of shining leaves")],
        [Biome.Desert] = [new("Glass Sea", "a plain of sand turned to glass by a fire no one remembers", "a mirror-blade of sea glass"),
            new("Singing Dunes", "dunes that sing when the wind crosses them", "a horn of singing sand")],
        [Biome.Savanna] = [new("Thunder Plain", "a plain where lightning falls every night and never burns", "a hammer of stormstone")],
        [Biome.Swamp] = [new("Burning Marsh", "a marsh whose waters burn with a cold blue fire", "a lantern of the marsh's cold fire")],
        [Biome.Tundra] = [new("Frozen Falls", "a great waterfall frozen in the act of falling", "a blade of the falls' frozen water"),
            new("Aurora Lake", "a lake that holds the northern lights in its depths", "a gem of captured auroras")],
        [Biome.Taiga] = [new("Frozen Falls", "a great waterfall frozen in the act of falling", "a blade of the falls' frozen water")],
        [Biome.Grassland] = [new("Great Falls", "falls so wide their roar is heard a day away", "a horn carved from the falls' rainbow stone"),
            new("Giants' Circle", "a ring of stones each taller than a keep", "a ring cut from a giant's stone")],
    };

    /// <summary>How many wonders a world holds: one for every ten lands, at least two.</summary>
    public static int Count(World world) => Math.Max(2, world.Regions.Count / 10);

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        if (!ctx.World.Structures.Values.Any(s => s.Type == StructureKind.Wonder)) Place(ctx);
        Discover(ctx);
        Challenge(ctx);
    }

    /// <summary>The wonders of a world, placed once: in lands of a fitting kind, one to a land, unknown.</summary>
    private void Place(SimContext ctx)
    {
        var world = ctx.World;
        var seed = unchecked((long)world.Seed);
        var lands = world.Regions.Values.Where(r => Kinds.ContainsKey(r.Biome)).OrderBy(r => Hash.Percent(seed + r.Id, 9301)).ThenBy(r => r.Id)
            .Take(Count(world)).ToList();
        foreach (var region in lands)
        {
            var kinds = Kinds[region.Biome];
            var kind = kinds[Hash.Percent(seed + region.Id, 9302) % kinds.Length];
            var id = world.Ids.Take(Structure.Kind);
            var named = _inventor.Invent(new InventionBrief("natural wonder", $"{Structure.Kind}#{id}",
                $"A natural wonder in the land of {region.Name}: {kind.What}. Name it as the people of the land would.", $"The {kind.Name} of {region.Name}"));
            var guarded = Hash.Percent(seed + region.Id, 9303) < GuardedShare;
            var wonder = new Structure
            {
                Id = id,
                Name = named.Name,
                Type = StructureKind.Wonder,
                Purpose = kind.Name,
                RegionId = region.Id,
                Progress = 100,
                State = StructureState.Standing,
                Treasure = guarded ? kind.Treasure : "",
            };
            world.Structures[id] = wonder;
        }
    }

    private static readonly HashSet<string> Deeds = new(StringComparer.Ordinal)
    {
        "MonsterSlain", "MonsterHuntFailed", "MonstersSlain", "LabyrinthDelved", "LabyrinthDelveFailed", "ArtifactFound", "DelveFailed",
        "ExpeditionBegun", "ArmyMoved", "BattleFought",
    };

    /// <summary>Someone out on a deed in a wonder's land, or a wanderer living there, may come upon it.</summary>
    private static void Discover(SimContext ctx)
    {
        var world = ctx.World;
        var hidden = world.Structures.Values.Where(s => s.Type == StructureKind.Wonder && s.FinderId == 0).ToList();
        if (hidden.Count == 0) return;
        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        var deeds = ctx.Log.All.Where(e => e.Date.Day >= since && Deeds.Contains(e.Type)).ToList();
        foreach (var wonder in hidden.OrderBy(s => s.Id))
        {
            var region = world.Regions[wonder.RegionId];
            bool There(WorldEvent e) => e.Place == region.Ref
                                        || e.Place is { Kind: Settlement.Kind } p && world.Settlements.TryGetValue(p.Id, out var s) && s.RegionId == region.Id;
            var finder = deeds.Where(There).OrderBy(e => e.Id)
                .Select(e => e.Actors.Values.Where(a => a.Kind == Character.Kind).Select(a => world.Characters.GetValueOrDefault(a.Id)).FirstOrDefault(c => c is { IsActive: true }))
                .OfType<Character>().FirstOrDefault(c => Hash.Percent(c.Id, 9306, ctx.Date.Year) < DiscoverChance * 2)
                ?? world.ActiveCharacters.Where(c => c.Role is Role.Adventurer or Role.Knight && world.Settlements.TryGetValue(c.SettlementId, out var home) && home.RegionId == region.Id)
                    .OrderBy(c => c.Id).FirstOrDefault(c => Hash.Percent(c.Id, 9307, ctx.Date.Year) < DiscoverChance / 3);
            if (finder is null) continue;
            var found = ctx.Emit(new EventDraft("WonderDiscovered",
                Actors: new Dictionary<string, EntityRef> { ["discoverer"] = finder.Ref, ["structure"] = wonder.Ref }, Place: region.Ref,
                Payload: new Dictionary<string, string> { ["name"] = wonder.Name, ["kind"] = wonder.Purpose },
                Importance: Math.Max(45, Fates.Importance(finder, 45)), Tags: new[] { "Characters", "Deeds", "Structures" }));
            (wonder.FinderId, wonder.StartedEventId, wonder.CompletedEventId) = (finder.Id, found.Id, found.Id);
            finder.Renown = Math.Min(100, finder.Renown + 6);
            // A guarded wonder's beast is met with it, and the treasure it keeps is known.
            if (wonder.Treasure.Length > 0 && wonder.GuardianId == 0)
            {
                var seed = unchecked((long)world.Seed) + region.Id;
                var breed = Monster.GuardianBreeds[Hash.Percent(seed, 9304) % Monster.GuardianBreeds.Count];
                var guardianId = world.Ids.Take(Monster.Kind);
                wonder.GuardianId = guardianId;
                world.Monsters[guardianId] = new Monster
                {
                    Id = guardianId,
                    Name = $"The {char.ToUpperInvariant(breed[0])}{breed[1..]} of {Narration.English.InProse(wonder.Name)}",
                    Breed = breed,
                    RegionId = region.Id,
                    Might = 45 + Hash.Percent(seed, 9305) % 30,
                    BornDay = ctx.Date.Day,
                    State = MonsterState.Lairing,
                    LairStructureId = wonder.Id,
                    RoseEventId = found.Id,
                    RevealedEventId = found.Id,
                };
            }
        }
    }

    /// <summary>A fighter of the land or near it may go against a found wonder's guardian; slaying it, they take the treasure.</summary>
    private void Challenge(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var wonder in world.Structures.Values.Where(s => s.Type == StructureKind.Wonder && s.FinderId != 0 && s.TreasureArtifactId == 0).OrderBy(s => s.Id).ToList())
        {
            if (!world.Monsters.TryGetValue(wonder.GuardianId, out var guardian) || !guardian.Alive) continue;
            var region = world.Regions[wonder.RegionId];
            var near = new[] { region.Id }.Concat(world.Geography?.Graph.Neighbours(region.Id) ?? Array.Empty<long>()).ToHashSet();
            var hero = world.ActiveCharacters
                .Where(c => c.Role is Role.Adventurer or Role.Knight && c.Stage(ctx.Date) == LifeStage.Adult && Levels.Of(c) >= Levels.Of(guardian) - 1
                            && world.Settlements.TryGetValue(c.SettlementId, out var home) && near.Contains(home.RegionId))
                .OrderByDescending(Levels.Of).ThenBy(c => c.Id).FirstOrDefault();
            if (hero is null || Hash.Percent(hero.Id, 9308, ctx.Date.Year) >= ChallengeChance) continue;
            var outcome = _arbiter.Resolve(Encounters.HuntBeast(world, hero, guardian), ctx.Rng);
            WorldEvent hunt;
            using (ctx.Annotate("account", outcome.Account))
                hunt = ctx.Emit(new EventDraft(outcome.AttackerWins ? "MonsterSlain" : "MonsterHuntFailed",
                    Actors: new Dictionary<string, EntityRef> { ["hunter"] = hero.Ref, ["monster"] = guardian.Ref, ["structure"] = wonder.Ref },
                    Place: region.Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
                    Causes: new[] { new Cause(wonder.CompletedEventId, CauseKind.Enabler, 0.8) },
                    Importance: Fates.Importance(hero, outcome.AttackerWins ? 45 : 20), Tags: new[] { "Characters", "Deeds", "Magic" }));
            if (outcome.AttackerFate == Fate.Dead)
                Fates.Die(ctx, hero, guardian.Name, new[] { new Cause(hunt.Id, CauseKind.Trigger, 1.0) }, lostInRegion: region.Id);
            else Fates.Injure(hero, outcome.AttackerFate, new Rng(unchecked((ulong)hunt.Id)));
            if (!outcome.AttackerWins) continue;
            (guardian.State, guardian.SlainEventId, guardian.SlayerId) = (MonsterState.Slain, hunt.Id, hero.Id);
            hero.Renown = Math.Min(100, hero.Renown + 10);
            // The treasure made of the place itself (the owner, 2026-11-02).
            var treasure = ArtifactForge.Create(new Rng(unchecked((ulong)hunt.Id * 31)), world.Ids, new NameGen(new Rng(unchecked((ulong)hunt.Id))), hunt.Id, fame: 45);
            treasure.Name = $"{char.ToUpperInvariant(wonder.Treasure[0])}{wonder.Treasure[1..]} of {Narration.English.InProse(wonder.Name)}";
            treasure.Lore = $"Made by no hand: {wonder.Treasure}, taken from {Narration.English.InProse(wonder.Name)} when its guardian fell.";
            world.Artifacts[treasure.Id] = treasure;
            wonder.TreasureArtifactId = treasure.Id;
            var taken = ctx.Emit(new EventDraft("WonderTreasureTaken",
                Actors: new Dictionary<string, EntityRef> { ["taker"] = hero.Ref, ["artifact"] = treasure.Ref, ["structure"] = wonder.Ref }, Place: region.Ref,
                Causes: new[] { new Cause(hunt.Id, CauseKind.Trigger, 1.0) }, Importance: Math.Max(50, Fates.Importance(hero, 50)), Tags: new[] { "Characters", "Artifacts" }));
            Fates.MoveToHolder(treasure, hero, taken.Id, "taken");
        }
    }
}

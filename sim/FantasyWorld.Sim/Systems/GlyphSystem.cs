using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Ponegralyphs (the owner, 2026-10-31): carved stones older than every realm, hidden in the wild. Each year one who went out
/// on a deed (a hunt, a delve, an expedition) may come upon one in that land; it tells a secret of the world, and the stone
/// keeps the name of the one who found it. Only once the chronicle airs (secrets are the audience's), a few to a world, one
/// to a land. Rolled from the finder and the year, never the world's dice.
/// </summary>
public sealed class GlyphSystem(IInventor? inventor = null) : ISimSystem
{
    public string Name => "glyphs";

    public const int FindChance = 3;

    private static readonly HashSet<string> Deeds = new(StringComparer.Ordinal)
    {
        "MonsterSlain", "MonsterHuntFailed", "MonstersSlain", "LabyrinthDelved", "LabyrinthDelveFailed", "ArtifactFound", "DelveFailed",
        "ExpeditionBegun",
    };

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    /// <summary>How many Ponegralyphs a world hides: one for every twelve lands, at least two.</summary>
    public static int Cap(World world) => Math.Max(2, world.Regions.Count / 12);

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        if (world.LastBroadcastDate is null) return;
        var found = world.Structures.Values.Where(s => s.Glyph).ToList();
        if (found.Count >= Cap(world)) return;
        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        var marked = found.Select(s => s.RegionId).ToHashSet();
        foreach (var deed in ctx.Log.All.Where(e => e.Date.Day >= since && Deeds.Contains(e.Type) && e.Place is { Kind: Region.Kind }).OrderBy(e => e.Id).ToList())
        {
            var region = world.Regions[deed.Place!.Value.Id];
            if (marked.Contains(region.Id)) continue;
            var finder = deed.Actors.Values.Where(a => a.Kind == Character.Kind).Select(a => world.Characters.GetValueOrDefault(a.Id))
                .FirstOrDefault(c => c is { IsActive: true });
            if (finder is null || Hash.Percent(finder.Id, 8201, deed.Id) >= FindChance) continue;
            var id = world.Ids.Take(Structure.Kind);
            var name = $"The Ponegralyph of {region.Name}";
            var discovered = ctx.Emit(new EventDraft("PonegralyphFound",
                Actors: new Dictionary<string, EntityRef> { ["finder"] = finder.Ref, ["structure"] = new(Structure.Kind, id) }, Place: region.Ref,
                Payload: new Dictionary<string, string> { ["name"] = name },
                Causes: new[] { new Cause(deed.Id, CauseKind.Trigger, 0.8) },
                Importance: Math.Max(55, Fates.Importance(finder, 55)), Tags: new[] { "Characters", "Deeds", "Magic" }));
            world.Structures[id] = new Structure
            {
                Id = id,
                Name = name,
                Type = StructureKind.Landmark,
                Glyph = true,
                FinderId = finder.Id,
                RegionId = region.Id,
                StartedEventId = discovered.Id,
                CompletedEventId = discovered.Id,
                Progress = 100,
                State = StructureState.Standing,
            };
            DeedsSystem.RevealSecret(ctx, _inventor, finder, world.Structures[id], region, discovered.Id, null);
            return; // one a year at most
        }
    }
}

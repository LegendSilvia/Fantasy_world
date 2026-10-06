using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>Tiny is for tests and quick demos; Medium and Large are real worlds (spec: 100–120 regions).</summary>
public enum WorldSize { Tiny, Medium, Large }

public sealed record WorldGenOptions(ulong Seed, WorldSize Size = WorldSize.Medium)
{
    public int MapSize => Size == WorldSize.Tiny ? 64 : 256;

    public int TargetRegions => Size switch
    {
        WorldSize.Tiny => 12,
        WorldSize.Medium => 100,
        _ => 120,
    };

    public TerrainOptions Terrain
    {
        get
        {
            var defaults = new TerrainOptions();
            var land = MapSize * MapSize * defaults.LandFraction;
            return defaults with { Width = MapSize, Height = MapSize, MinIslandTiles = (int)(land / TargetRegions / 2) };
        }
    }
}

/// <summary>Seed → a complete world: terrain, rivers, regions, region graph, settlements, cultures, faiths, notables and ancient artifacts. No history yet (Plan 6).</summary>
public static class WorldGenerator
{
    public static World Generate(WorldGenOptions options, DateTimeOffset nowUtc)
    {
        var seed = options.Seed;
        Rng Stream(string name) => Rng.ForStream(seed, "worldgen/" + name, 0);

        var terrain = TerrainGenerator.Generate(seed, options.Terrain);
        Hydrology.Apply(terrain);
        var layout = RegionPartitioner.Partition(terrain, options.TargetRegions, Stream("regions"));
        var graph = RegionGraphBuilder.Build(terrain, layout);

        var world = WorldFactory.CreateNew(seed, nowUtc);
        var names = new NameGen(Stream("names"));
        var regions = RegionProfiler.Profile(terrain, layout, seed, Stream("profile"), world.Ids, names);
        var settlements = SettlementPlacer.Place(terrain, layout, regions, Stream("settlements"), world.Ids, names);

        var (cultures, faiths) = CultureGenerator.Generate(regions, graph, Stream("cultures"), world.Ids);

        world.Geography = new Geography(terrain, layout.RegionOf, graph);
        foreach (var region in regions) world.Regions[region.Id] = region;
        foreach (var culture in cultures) world.Cultures[culture.Id] = culture;
        foreach (var faith in faiths) world.Faiths[faith.Id] = faith;
        foreach (var settlement in settlements) world.Settlements[settlement.Id] = settlement;
        PeopleGenerator.Populate(world, Stream("people"));
        return world;
    }
}

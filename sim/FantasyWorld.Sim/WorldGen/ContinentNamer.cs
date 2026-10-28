using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.WorldGen;

/// <summary>
/// The world's continents (sub-project 13): every landmass holding at least a tenth of the land is one; smaller ones
/// (islands) belong to the nearest. Each gets a name, and each region the continent its centre lies on.
/// </summary>
public static class ContinentNamer
{
    public const double ContinentShare = 0.1;

    /// <summary>Landmass index (into <see cref="TerrainGenerator.LandComponents"/>) → the major landmass it belongs to, per region.</summary>
    public static int[] LandmassOfRegions(TerrainGrid grid, RegionLayout layout)
    {
        var components = TerrainGenerator.LandComponents(grid);
        var componentOf = new int[grid.Count];
        Array.Fill(componentOf, -1);
        for (var c = 0; c < components.Count; c++)
            foreach (var i in components[c]) componentOf[i] = c;
        var land = components.Sum(c => c.Count);
        var major = Enumerable.Range(0, components.Count).Where(c => components[c].Count >= land * ContinentShare).ToList();
        if (major.Count == 0) major = [Enumerable.Range(0, components.Count).OrderByDescending(c => components[c].Count).First()];
        var centres = components.Select(c => (X: c.Average(i => i % grid.Width), Y: c.Average(i => i / grid.Width))).ToList();
        int Major(int c) => major.Contains(c) ? c
            : major.OrderBy(m => Math.Pow(centres[m].X - centres[c].X, 2) + Math.Pow(centres[m].Y - centres[c].Y, 2)).First();

        var result = new int[layout.Count + 1];
        for (var r = 1; r <= layout.Count; r++)
        {
            var tile = layout.Tiles[r].FirstOrDefault(i => componentOf[i] >= 0, -1);
            result[r] = tile < 0 ? major[0] : Major(componentOf[tile]);
        }
        return result;
    }

    public static void Name(World world, TerrainGrid grid, RegionLayout layout, Rng rng)
    {
        var landmass = LandmassOfRegions(grid, layout);
        var names = new NameGen(rng);
        var ids = new Dictionary<int, long>();
        foreach (var mass in Enumerable.Range(1, layout.Count).Select(r => landmass[r]).Distinct().Order())
        {
            var continent = new Continent { Id = world.Ids.Take(Continent.Kind), Name = names.Continent() };
            world.Continents[continent.Id] = continent;
            ids[mass] = continent.Id;
        }
        foreach (var region in world.Regions.Values) region.ContinentId = ids[landmass[(int)region.Id]];
    }
}

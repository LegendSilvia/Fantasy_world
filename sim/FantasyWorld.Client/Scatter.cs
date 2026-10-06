using System.Numerics;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Client;

public enum TreeKind { Broadleaf, Conifer }

/// <summary>One tree on the block: where, how tall, which shape and its leaf colour.</summary>
public sealed record Tree(Vector3 Position, float Height, TreeKind Kind, Vector3 Colour);

/// <summary>One building in a settlement: footprint size, turn, roof colour; towers are the keeps of towns and cities.</summary>
public sealed record House(Vector3 Position, float Size, float Yaw, Vector3 Roof, bool Tower);

/// <summary>
/// Dresses the diorama like a model maker would (sub-project 4 §3): woods of round broadleaf trees and pointed
/// conifers scattered by biome, kept off shores, rivers, steep rock and settlements; settlements as clusters of houses
/// around a keep. Placement is a pure function of the terrain and the settlement, so the scene is stable across runs.
/// </summary>
public static class Scatter
{
    public const float Spacing = 0.011f;
    public const int MaxTrees = 14000;

    private static readonly Vector3 Leaf = BlockMesh.Hex(0x6d8f55);
    private static readonly Vector3 Jungle = BlockMesh.Hex(0x5d8a4c);
    private static readonly Vector3 Needle = BlockMesh.Hex(0x58795c);
    private static readonly Vector3 Dry = BlockMesh.Hex(0x9aa064);

    public static double Density(Biome biome) => biome switch
    {
        Biome.Forest => 0.75, Biome.Jungle => 0.85, Biome.Taiga => 0.7, Biome.Swamp => 0.3, Biome.Savanna => 0.08,
        Biome.Grassland => 0.04, Biome.Hills => 0.12, Biome.Mountain => 0.02, Biome.Tundra => 0.02, _ => 0,
    };

    /// <summary>Trees on a jittered lattice; each spot is kept with its biome's density. Clearings are kept around the given points.</summary>
    public static IReadOnlyList<Tree> Trees(TerrainGrid grid, IEnumerable<Vector3>? clearings = null, float clearing = 0.03f)
    {
        var field = TerrainField.For(grid);
        var open = (clearings ?? Array.Empty<Vector3>()).ToList();
        var steps = (int)(2 / Spacing);
        var trees = new List<Tree>();
        for (var j = 0; j < steps; j++)
            for (var i = 0; i < steps; i++)
            {
                var u = (i + 0.15 + 0.7 * BlockMesh.Hash(i, j + 7919)) / steps;
                var v = (j + 0.15 + 0.7 * BlockMesh.Hash(i + 104729, j)) / steps;
                var tile = grid.Index((int)Math.Min(grid.Width - 1, u * grid.Width), (int)Math.Min(grid.Height - 1, v * grid.Height));
                var biome = grid.BiomeAt(tile);
                if (BlockMesh.Hash(i * 31 + 5, j * 17 + 3) >= Density(biome)) continue;
                var height = field.Height(u, v);
                if (height < BlockMesh.Shore * 1.6f || field.River(u, v) > 0.2f) continue;
                if (Slope(field, u, v) > 0.55f) continue;
                var at = new Vector3((float)(u * 2 - 1), height, (float)(v * 2 - 1));
                if (open.Any(c => Vector2.Distance(new(c.X, c.Z), new(at.X, at.Z)) < clearing)) continue;
                var kind = biome is Biome.Taiga or Biome.Tundra or Biome.Mountain || field.Cold(u, v) > 0.62f ? TreeKind.Conifer : TreeKind.Broadleaf;
                var colour = kind == TreeKind.Conifer ? Needle : biome == Biome.Jungle ? Jungle : biome is Biome.Savanna or Biome.Grassland ? Dry : Leaf;
                var shade = 0.88f + 0.24f * BlockMesh.Hash(i + 11, j + 13);
                var tall = (kind == TreeKind.Conifer ? 0.012f : 0.0095f) * (0.75f + 0.5f * BlockMesh.Hash(i + 3, j + 5));
                trees.Add(new Tree(at, tall, kind, Vector3.Clamp(colour * shade, Vector3.Zero, Vector3.One)));
            }
        if (trees.Count <= MaxTrees) return trees;
        var keep = (double)MaxTrees / trees.Count;
        return trees.Where((_, k) => BlockMesh.Hash(k, 977) < keep).ToList();
    }

    /// <summary>The houses of a settlement marker: more for bigger places, in a sunflower spiral around a keep.</summary>
    public static IReadOnlyList<House> Houses(Marker settlement, TerrainGrid grid)
    {
        var field = TerrainField.For(grid);
        var count = Math.Max(3, (int)Math.Round(settlement.Size * settlement.Size * 9000));
        var spread = settlement.Size * 0.7f;
        var seed = (int)(settlement.Ref.Id * 7919 % 100_000);
        var houses = new List<House>();
        var walled = settlement.Size >= Markers.SizeOf("Town");
        if (walled) houses.Add(new House(OnSurface(field, settlement.Position.X, settlement.Position.Z), 0.011f, 0, settlement.Colour, Tower: true));
        for (var k = walled ? 1 : 0; k < count; k++)
        {
            var r = spread * MathF.Sqrt((k + 0.5f) / count);
            var a = k * 2.39996f + seed;
            var (x, z) = (settlement.Position.X + r * MathF.Cos(a), settlement.Position.Z + r * MathF.Sin(a));
            var size = 0.0062f + 0.0028f * BlockMesh.Hash(seed + k, 31);
            houses.Add(new House(OnSurface(field, x, z), size, BlockMesh.Hash(seed, k) * MathF.PI, settlement.Colour, Tower: false));
        }
        return houses;
    }

    private static Vector3 OnSurface(TerrainField field, float x, float z) =>
        new(x, Math.Max(0, field.Height((x + 1) / 2.0, (z + 1) / 2.0)), z);

    /// <summary>1 − the surface normal's up component at a point (0 flat, 1 vertical).</summary>
    private static float Slope(TerrainField field, double u, double v)
    {
        const double d = 0.004;
        var dx = (field.Height(u + d, v) - field.Height(u - d, v)) / (4 * d);
        var dz = (field.Height(u, v + d) - field.Height(u, v - d)) / (4 * d);
        return 1 - 1 / MathF.Sqrt(1 + (float)(dx * dx + dz * dz));
    }
}

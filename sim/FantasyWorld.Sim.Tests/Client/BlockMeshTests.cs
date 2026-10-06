using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class BlockMeshTests
{
    private static readonly TerrainGrid Grid =
        WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch).Geography!.Terrain;

    [Fact]
    public void TheTop_IsAHeightfieldColouredByBiome()
    {
        var top = BlockMesh.Top(Grid, 32);
        Assert.Equal(33 * 33, top.Positions.Length);
        Assert.Equal(32 * 32 * 2, top.Triangles);
        Assert.All(top.Positions, p => Assert.True(p.X is >= -1 and <= 1 && p.Z is >= -1 and <= 1));
        Assert.Contains(top.Positions, p => p.Y < 0);
        Assert.Contains(top.Positions, p => p.Y > 0.05f);
        Assert.All(top.Normals, n => Assert.InRange(n.Length(), 0.999f, 1.001f));
        Assert.True(top.Normals.Count(n => n.Y > 0.5f) > top.Normals.Length * 0.9);
        Assert.Contains(BlockMesh.ColourOf(Biome.Mountain), top.Colors);
        Assert.All(top.Indices, i => Assert.InRange(i, 0, top.Positions.Length - 1));
    }

    [Fact]
    public void TheSides_CutThroughThreeStrata_DownToTheBase()
    {
        var sides = BlockMesh.Sides(Grid, 16);
        Assert.Equal(4 * 16 * BlockMesh.Strata.Length * 4, sides.Positions.Length);
        Assert.Equal(-BlockMesh.BaseDepth, sides.Positions.Min(p => p.Y));
        Assert.All(BlockMesh.Strata, s => Assert.Contains(s.Colour, sides.Colors));
        Assert.All(sides.Normals, n => Assert.Equal(0, n.Y));
        Assert.Equal(2, BlockMesh.WaterPlane().Triangles);
    }

    [Fact]
    public void MapPoints_LandOnTheSurface_AndComeBack()
    {
        var point = new MapPoint(0.25, 0.75);
        var onBlock = Projection.ToBlock(Grid, point);
        Assert.Equal(-0.5f, onBlock.X, 5);
        Assert.Equal(0.5f, onBlock.Z, 5);
        Assert.True(onBlock.Y >= Projection.MarkerLift);
        var back = Projection.ToMap(onBlock);
        Assert.Equal(point.X, back.X, 5);
        Assert.Equal(point.Y, back.Y, 5);
    }
}

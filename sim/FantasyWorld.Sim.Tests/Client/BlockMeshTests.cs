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
        Assert.All(top.Colors, c => Assert.True(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1));
        Assert.True(top.Colors.Distinct().Count() > 200, "biomes blend instead of standing in flat blocks");
        // The surface is smooth: neighbouring samples never jump by a whole step of the old tile terraces.
        Assert.True(Enumerable.Range(0, 32).All(i => Math.Abs(top.Positions[i].Y - top.Positions[i + 1].Y) < 0.03f));
        Assert.All(top.Indices, i => Assert.InRange(i, 0, top.Positions.Length - 1));
    }

    [Fact]
    public void TheSides_CutThroughThreeStrata_DownToTheBase()
    {
        var sides = BlockMesh.Sides(Grid, 16);
        Assert.Equal(4 * 16 * BlockMesh.Strata.Length * 4, sides.Positions.Length);
        Assert.Equal(-BlockMesh.BaseDepth, sides.Positions.Min(p => p.Y));
        // The world is ringed by sea, so its walls show sand over clay over bedrock.
        Assert.All(BlockMesh.Strata.Skip(1), s => Assert.Contains(s.Colour, sides.Colors));
        Assert.Contains(BlockMesh.Sand, sides.Colors);
        Assert.All(sides.Normals, n => Assert.Equal(0, n.Y));
    }

    [Fact]
    public void TheSea_DeepensInColourAndOpacity_AndIsClearOverLand()
    {
        var water = BlockMesh.Water(Grid, 24);
        Assert.NotNull(water.Alphas);
        Assert.Equal(water.Positions.Length, water.Alphas!.Length);
        Assert.Contains(0f, water.Alphas);
        Assert.Contains(water.Alphas, a => a > 0.6f);
        Assert.All(water.Positions.Take(25 * 25), p => Assert.Equal(0, p.Y));
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

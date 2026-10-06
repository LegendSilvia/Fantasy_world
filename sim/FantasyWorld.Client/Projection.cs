using System.Numerics;
using FantasyWorld.Sim.Geo;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>Map positions (0–1 across and down) to points on the block's surface, and back.</summary>
public static class Projection
{
    public const float MarkerLift = 0.02f;

    public static Vector3 ToBlock(TerrainGrid grid, MapPoint point, float lift = MarkerLift) =>
        new((float)(point.X * 2 - 1), Math.Max(0, BlockMesh.HeightAt(grid, point.X, point.Y)) + lift, (float)(point.Y * 2 - 1));

    public static MapPoint ToMap(Vector3 onBlock) =>
        new(Math.Clamp((onBlock.X + 1) / 2.0, 0, 1), Math.Clamp((onBlock.Z + 1) / 2.0, 0, 1));
}

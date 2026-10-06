using System.Numerics;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Client;

/// <summary>
/// A small painted top-down map of the world (the character page's location inset): the terrain's colours with
/// soft hill shading, the sea tinted by depth, as RGBA bytes a renderer turns into a texture.
/// </summary>
public static class MiniMap
{
    public static byte[] Pixels(TerrainGrid grid, int size)
    {
        var field = TerrainField.For(grid);
        var pixels = new byte[size * size * 4];
        var light = Vector3.Normalize(new Vector3(-1, 1.6f, -1));
        var step = 1.0 / size;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var (u, v) = ((x + 0.5) / size, (y + 0.5) / size);
                var h = field.Height(u, v);
                Vector3 colour;
                if (h <= 0)
                    colour = Vector3.Lerp(BlockMesh.Shallows, BlockMesh.Deep, Math.Clamp(-h / (BlockMesh.SeaDepth * 0.7f), 0, 1));
                else
                {
                    var dx = (field.Height(u + step, v) - field.Height(u - step, v)) / (float)(4 * step);
                    var dz = (field.Height(u, v + step) - field.Height(u, v - step)) / (float)(4 * step);
                    var shade = Math.Clamp(Vector3.Dot(Vector3.Normalize(new Vector3(-dx, 1, -dz)), light), 0.55f, 1.1f);
                    colour = field.BaseColour(u, v) * (0.35f + 0.65f * shade);
                    colour = Vector3.Lerp(colour, BlockMesh.River, Math.Clamp((field.River(u, v) - 0.25f) * 2, 0, 1));
                }
                var i = (y * size + x) * 4;
                pixels[i] = (byte)(Math.Clamp(colour.X, 0, 1) * 255);
                pixels[i + 1] = (byte)(Math.Clamp(colour.Y, 0, 1) * 255);
                pixels[i + 2] = (byte)(Math.Clamp(colour.Z, 0, 1) * 255);
                pixels[i + 3] = 255;
            }
        return pixels;
    }
}

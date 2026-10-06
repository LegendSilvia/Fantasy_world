using System.Numerics;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

public enum MarkerShape { Settlement, Landmark, Labyrinth, Ruin }

/// <summary>One thing standing on the block: where, what shape, how big and in what colour.</summary>
public sealed record Marker(EntityRef Ref, string Name, MarkerShape Shape, Vector3 Position, float Size, Vector3 Colour, PinMarker Kind);

/// <summary>A numbered digest pin floating above its place.</summary>
public sealed record Pin(int Number, EntityRef Place, string Label, Vector3 Position, long EventId);

/// <summary>A straight piece of a realm border on the block's surface.</summary>
public sealed record Segment(Vector3 From, Vector3 To);

/// <summary>
/// Lays the map screen out on the diorama (sub-project 4 §3): settlement blocks sized by tier and coloured by their
/// holder (orange when occupied), diamonds for landmarks, nested diamonds for labyrinths, numbered pins above digest
/// places, and realm borders along the tile edges where two differently held regions meet.
/// </summary>
public static class Markers
{
    public const float PinLift = 0.14f;

    public static float SizeOf(string tier) => tier switch
    {
        nameof(SettlementTier.City) => 0.045f,
        nameof(SettlementTier.Town) => 0.034f,
        nameof(SettlementTier.Village) => 0.024f,
        _ => 0.016f,
    };

    public static IReadOnlyList<Marker> For(MapScreen map, Geography geography)
    {
        var grid = geography.Terrain;
        var colourOf = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Colour);
        var markers = new List<Marker>();
        foreach (var s in map.Settlements.Where(s => s.Position is not null))
        {
            var region = RegionAt(geography, s.Position!);
            var colour = s.Marker == PinMarker.Conflict ? Palette.Conflict
                : colourOf.TryGetValue(region, out var name) && name != "none" ? Palette.Heraldic(name) : Palette.Muted;
            markers.Add(new Marker(s.Ref, s.Name, MarkerShape.Settlement, Projection.ToBlock(grid, s.Position!, 0), SizeOf(s.Tier), colour, s.Marker));
        }
        foreach (var st in map.Structures.Where(st => st.Position is not null))
        {
            var shape = st.State == nameof(StructureState.Ruined) ? MarkerShape.Ruin
                : st.Kind == nameof(StructureKind.Labyrinth) ? MarkerShape.Labyrinth : MarkerShape.Landmark;
            var colour = shape == MarkerShape.Ruin ? Palette.Muted : shape == MarkerShape.Labyrinth ? Palette.For(Category.FaithAndMagic) : Palette.Ink;
            // Structures stand beside their settlement, not inside it.
            var at = Projection.ToBlock(grid, st.Position!, 0) + new Vector3(0.03f, 0, 0.03f);
            markers.Add(new Marker(st.Ref, st.Name, shape, at with { Y = Math.Max(0, BlockMesh.HeightAt(grid, (at.X + 1) / 2, (at.Z + 1) / 2)) },
                0.022f, colour, PinMarker.Plain));
        }
        return markers;
    }

    /// <summary>The digest's numbered pins, lifted above the block so they read from any angle.</summary>
    public static IReadOnlyList<Pin> Pins(DigestScreen digest, Geography geography) =>
        digest.Pins.Select(p => new Pin(p.Number, p.Place, p.Label, Projection.ToBlock(geography.Terrain, p.Position, PinLift), p.EventId)).ToList();

    /// <summary>Tile edges between land regions whose holders differ, slightly above the surface.</summary>
    public static IReadOnlyList<Segment> Borders(MapScreen map, Geography geography, float lift = 0.006f)
    {
        var grid = geography.Terrain;
        var owner = map.Regions.ToDictionary(r => r.Ref.Id, r => r.Owner?.Id ?? 0);
        long Owner(int region) => owner.GetValueOrDefault(region);
        var (w, h) = (grid.Width, grid.Height);
        Vector3 At(double u, double v) => new((float)(u * 2 - 1), Math.Max(0, BlockMesh.HeightAt(grid, u, v)) + lift, (float)(v * 2 - 1));
        var segments = new List<Segment>();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var here = geography.RegionOf[grid.Index(x, y)];
                if (here == 0) continue;
                if (x + 1 < w && geography.RegionOf[grid.Index(x + 1, y)] is var right and not 0 && right != here && Owner(right) != Owner(here))
                    segments.Add(new Segment(At((x + 1.0) / w, y / (double)h), At((x + 1.0) / w, (y + 1.0) / h)));
                if (y + 1 < h && geography.RegionOf[grid.Index(x, y + 1)] is var down and not 0 && down != here && Owner(down) != Owner(here))
                    segments.Add(new Segment(At(x / (double)w, (y + 1.0) / h), At((x + 1.0) / w, (y + 1.0) / h)));
            }
        return segments;
    }

    private static long RegionAt(Geography geography, MapPoint point)
    {
        var grid = geography.Terrain;
        var x = (int)Math.Clamp(point.X * grid.Width, 0, grid.Width - 1);
        var y = (int)Math.Clamp(point.Y * grid.Height, 0, grid.Height - 1);
        return geography.RegionOf[grid.Index(x, y)];
    }
}

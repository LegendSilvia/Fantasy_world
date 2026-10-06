using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Views;

/// <summary>A position on the world map, 0–1 across and down (the client maps it onto its diorama).</summary>
public sealed record MapPoint(double X, double Y)
{
    public static MapPoint? Of(World world, EntityRef entity)
    {
        if (world.Geography is not { } geography) return null;
        var (w, h) = ((double)geography.Terrain.Width, (double)geography.Terrain.Height);
        return entity.Kind switch
        {
            Settlement.Kind when world.Settlements.TryGetValue(entity.Id, out var s) => new MapPoint((s.Tile.X + 0.5) / w, (s.Tile.Y + 0.5) / h),
            Region.Kind when world.Regions.TryGetValue(entity.Id, out var r) => new MapPoint((r.Center.X + 0.5) / w, (r.Center.Y + 0.5) / h),
            Structure.Kind when world.Structures.TryGetValue(entity.Id, out var st) =>
                Of(world, st.SettlementId != 0 ? new EntityRef(Settlement.Kind, st.SettlementId) : new EntityRef(Region.Kind, st.RegionId)),
            Character.Kind when world.Characters.TryGetValue(entity.Id, out var c) => Of(world, new EntityRef(Settlement.Kind, c.SettlementId)),
            _ => null,
        };
    }
}

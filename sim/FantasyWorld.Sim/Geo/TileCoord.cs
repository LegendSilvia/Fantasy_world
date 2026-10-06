namespace FantasyWorld.Sim.Geo;

public readonly record struct TileCoord(int X, int Y)
{
    public int DistanceSquared(TileCoord other) =>
        (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);

    public override string ToString() => $"({X},{Y})";
}

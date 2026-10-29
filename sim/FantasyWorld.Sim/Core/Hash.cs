namespace FantasyWorld.Sim.Core;

/// <summary>
/// A fixed roll from numbers alone (a character, a day, a salt), for choices that must not draw on the world's dice: a new
/// rule added this way leaves every other roll of the world as it was.
/// </summary>
public static class Hash
{
    /// <summary>A number from 0 to 99, the same for the same inputs.</summary>
    public static int Percent(long a, long salt, long b = 0)
    {
        var x = unchecked((ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)salt * 0xC2B2AE3D27D4EB4FUL ^ (ulong)b * 0x165667B19E3779F9UL);
        x ^= x >> 33;
        x = unchecked(x * 0xFF51AFD7ED558CCDUL);
        x ^= x >> 33;
        return (int)(x % 100);
    }
}

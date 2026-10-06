namespace FantasyWorld.Sim.Persist;

/// <summary>
/// Rolling backups of the last three sessions (spec §9): before a command changes the world, the file is copied to
/// <c>.bak1</c>, after shifting older copies to <c>.bak2</c> and <c>.bak3</c>.
/// </summary>
public static class Backups
{
    public const int Keep = 3;

    public static string PathOf(string world, int n) => $"{world}.bak{n}";

    public static void Rotate(WorldStore store, string world)
    {
        File.Delete(PathOf(world, Keep));
        for (var n = Keep - 1; n >= 1; n--)
            if (File.Exists(PathOf(world, n))) File.Move(PathOf(world, n), PathOf(world, n + 1));
        store.BackupTo(PathOf(world, 1));
    }
}

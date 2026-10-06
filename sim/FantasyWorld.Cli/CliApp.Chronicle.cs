using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    private static int Follow(string path, string[] rest, TextWriter output, bool follow)
    {
        if (rest.Length < 1) return UsageError(output);
        var entity = Importance.ParseRef(rest[0]);
        using var store = OpenExisting(path);
        var world = store.Load().World;
        var name = Names.Of(world)(entity) ?? throw new KeyNotFoundException($"No {entity} in this world.");
        store.Follow(entity, follow);
        output.WriteLine($"{(follow ? "Following" : "No longer following")} {name} ({entity}).");
        var followed = store.LoadFollowed();
        if (followed.Count > 0) output.WriteLine("Followed: " + string.Join(", ", followed.Select(f => $"{Names.Of(world)(f) ?? "?"} ({f})")));
        return 0;
    }
}

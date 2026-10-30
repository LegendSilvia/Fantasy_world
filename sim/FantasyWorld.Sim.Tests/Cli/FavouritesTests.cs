using FantasyWorld.Cli;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Cli;

/// <summary>The owner, 2026-10-30: the videos follow the characters viewers like (their follows, page views and blessings).</summary>
public class FavouritesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 23, 0, 0, TimeSpan.Zero);

    private static (int Code, string Output) Run(params string[] args)
    {
        var output = new StringWriter();
        return (CliApp.Run(args, output, () => T0), output.ToString());
    }

    [Fact]
    public void TheTally_ReadsFollowsAndViews_AndOldTalliesStillRead()
    {
        var tally = Tally.Parse("""{"prayers":{},"blessings":{},"follows":{"12":3},"views":{"12":9,"40":2}}""");
        Assert.Equal(3, tally.FollowsOf[12]);
        Assert.Equal((9, 2), (tally.ViewsOf[12], tally.ViewsOf[40]));
        Assert.Empty(Tally.Parse("""{"prayers":{},"blessings":{}}""").FollowsOf);
    }

    [Fact]
    public void TheViewersFavourites_AreFollowed_AndToldOfInTheNextEpisode()
    {
        using var file = new TempFile();
        using var dir = new TempDir();
        Run("new", file.Path, "--seed", "3", "--size", "tiny");
        Run("advance", file.Path, "2y");
        long fan, seen;
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, _, _) = store.Load();
            var living = world.ActiveCharacters.OrderBy(c => c.Id).ToList();
            (fan, seen) = (living[0].Id, living[1].Id);
        }
        // Two followers (6) outweigh five page views (5).
        var tallyPath = Path.Combine(dir.Path, "tally.json");
        File.WriteAllText(tallyPath, $$$"""{"prayers":{},"blessings":{},"follows":{"{{{fan}}}":2},"views":{"{{{seen}}}":5,"999999":40}}""");
        var (code, output) = Run("answer", file.Path, tallyPath);
        Assert.Equal(0, code);
        using (var store = WorldStore.Open(file.Path))
        {
            var (world, log, _) = store.Load();
            Assert.Equal([fan, seen], Favourites.Of(world, log, Tally.Parse(File.ReadAllText(tallyPath))));
            Assert.Equal([fan, seen], store.LoadFollowed().Select(r => r.Id).Order());
            Assert.Contains($"The viewers' favourites: {world.Characters[fan].Name}, {world.Characters[seen].Name}.", output);
        }

        Run("advance", file.Path, "1y");
        Assert.Equal(0, Run("episode", file.Path, "--out", dir.Path, "--date", "2026-10-27").Code);
        var episode = Episode.FromJson(File.ReadAllText(Path.Combine(dir.Path, "episode.json")));
        // Each favourite is in the episode: among its stories, or where they stand now.
        using var after = WorldStore.Open(file.Path);
        var (w, l, _) = after.Load();
        foreach (var id in new[] { fan, seen })
        {
            var name = w.Characters[id].Name;
            Assert.Contains(episode.Parts, p => p.Text.Contains(name));
        }
    }
}

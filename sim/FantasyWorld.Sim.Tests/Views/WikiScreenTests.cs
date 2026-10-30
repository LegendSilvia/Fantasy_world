using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class WikiScreenTests
{
    private static Simulator History(int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * years);
        return sim;
    }

    [Fact]
    public void TheWiki_ExplainsEveryPartOfTheWorld_ByCategory_WithThisWorldsOwn()
    {
        var sim = History(20);
        var world = sim.World;
        var wiki = WikiScreen.Build(world, sim.Log);
        var keys = wiki.Categories.Select(c => c.Key).ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);
        Assert.Superset(new HashSet<string> { "races", "classes", "resources", "faiths", "realms", "war", "fights", "beginnings", "changes", "beasts", "landmarks", "shadow", "trade" },
            keys.ToHashSet());
        Assert.All(wiki.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Intro)));

        var Of = (string key) => wiki.Categories.Single(c => c.Key == key).Entries;
        Assert.All(world.Races.Values, r => Assert.Contains(Of("races"), e => e.Name == r.Name));
        Assert.All(Classes.All, k => Assert.Contains(Of("classes"), e => e.Name == Classes.Title(k)));
        Assert.All(world.Faiths.Values, f => Assert.Contains(Of("faiths"), e => e.Name == f.Name));
        // Every reason for war is told in words, never as its bare key.
        Assert.All(Of("war").Where(e => e.Name.Contains(' ')), e => Assert.DoesNotContain($"for {e.Name.ToLowerInvariant()}", e.Text));
        // A beast no one has survived stays out; an unfound treasure is only a rumour.
        Assert.All(world.Monsters.Values.Where(m => !m.Revealed), m => Assert.DoesNotContain(Of("beasts"), e => e.Links.Any(l => l.Ref == m.Ref)));
        Assert.All(world.Wonders.Where(w => w.Hidden), w => Assert.DoesNotContain(Of("resources"), e => e.Name == w.Name));
    }

    [Fact]
    public void AnEpisode_TellsWhoWonEveryFightThatEnded()
    {
        var sim = History(12);
        var since = sim.Log.All.Last(e => e.Date.Year == 9).Id;
        var episode = Episode.Build(sim.World, sim.Log, since, "2026-10-30");
        var ended = sim.Log.Since(since).Where(e => e.Type == "DuelFought").ToList();
        Assert.NotEmpty(ended);
        // Each fought duel's sentence names its winner.
        var narrator = new Sim.Narration.TemplateNarrator(sim.World);
        Assert.All(ended, e => Assert.True(narrator.Sentence(e).Contains(" won") || narrator.Sentence(e).Contains("Neither won")));
        var results = episode.Parts.SingleOrDefault(p => p.Kind == EpisodeSection.Results);
        if (results is not null)
        {
            Assert.Contains(" won", results.Text);
            Assert.True(results.Text.Split(". ").Length <= EpisodeSection.MostResults * 3);
        }
    }
}

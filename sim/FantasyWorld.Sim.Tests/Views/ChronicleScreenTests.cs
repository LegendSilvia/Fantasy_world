using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Views;

public class ChronicleScreenTests
{
    private static Simulator History(int years)
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(7, WorldSize.Medium), DateTimeOffset.UnixEpoch);
        var sim = new Simulator(world, new EventLog(), new EventQueue(), DefaultSystems.Create());
        sim.AdvanceDays(360 * years);
        return sim;
    }

    [Fact]
    public void TheChronicle_IsNewestFirst_FilteredAndPaged()
    {
        var sim = History(12);
        var all = ChronicleScreen.Build(sim.World, sim.Log);
        Assert.Equal((4, 13), (all.FromYear, all.ToYear));
        Assert.Equal(sim.Log.All.Count(e => e.Date.Year >= 4), all.Total);
        var ids = all.Seasons.SelectMany(s => s.Rows).Select(r => r.EventId).ToList();
        Assert.Equal(ChronicleScreen.PageSize, ids.Count);
        Assert.Equal(ids.OrderByDescending(id => id), ids);
        Assert.Equal(all.Pages, ChronicleScreen.Build(sim.World, sim.Log, page: 999).Page);

        var wars = ChronicleScreen.Build(sim.World, sim.Log, 1, 13, Category.War, minImportance: 40);
        Assert.All(wars.Seasons.SelectMany(s => s.Rows), r => Assert.True(r.Category == Category.War && r.Importance >= 40));
    }

    [Fact]
    public void EveryRealmAndArtifact_HasAPage_WhoseNamesResolve()
    {
        var sim = History(30);
        var world = sim.World;
        var nameOf = Names.Of(world);
        foreach (var realm in world.Realms.Values.Where(r => !r.Destroyed))
        {
            var page = RealmScreen.Build(world, sim.Log, realm.Id);
            Assert.Contains(realm.Name, page.Title);
            Assert.Equal(world.Faiths[realm.FaithId].Name, page.Faith!.Name);
            Assert.All(page.Vassals.Concat(page.Council.Select(c => c.Member)).Concat(page.Treaties.Select(t => t.With)),
                m => Assert.Equal(nameOf(m.Ref), m.Name));
            Assert.All(page.Eras, e => Assert.True(sim.Log.TryGet(e.EventId, out _)));
        }
        Assert.Contains(world.Realms.Values, r => RealmScreen.Build(world, sim.Log, r.Id).Wars.Count > 0);
        foreach (var artifact in world.Artifacts.Values)
        {
            var page = ArtifactScreen.Build(world, sim.Log, artifact.Id);
            Assert.NotEmpty(page.Powers);
            Assert.Equal(artifact.Provenance.Count, page.Provenance.Count);
            Assert.All(page.Made.Links, l => Assert.Equal(nameOf(l.Entity), page.Made.Text.Substring(l.Start, l.Length)));
        }
    }
}

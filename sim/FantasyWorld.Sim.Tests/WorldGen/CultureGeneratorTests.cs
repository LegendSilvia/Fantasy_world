using System.Text.Json;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class CultureGeneratorTests
{
    private static World Medium(ulong seed = 42) =>
        WorldGenerator.Generate(new WorldGenOptions(seed), DateTimeOffset.UnixEpoch);

    [Fact]
    public void MediumWorld_HasSeveralCulturesAndFaiths()
    {
        var world = Medium();
        Assert.InRange(world.Cultures.Count, 6, 9);
        Assert.InRange(world.Faiths.Count, 2, 5);
        Assert.Empty(Invariants.Check(world, new EventLog()));
    }

    [Fact]
    public void EveryRegion_HasACultureAndItsFaith()
    {
        var world = Medium();
        Assert.All(world.Regions.Values, r =>
        {
            Assert.True(world.Cultures.ContainsKey(r.CultureId));
            Assert.Equal(world.Cultures[r.CultureId].FaithId, r.FaithId);
        });
        Assert.All(world.Cultures.Values, c => Assert.Equal(c.Id, world.Regions[c.HomeRegionId].CultureId));
    }

    [Fact]
    public void Cultures_FormConnectedTerritories()
    {
        var world = Medium();
        var graph = world.Geography!.Graph;
        foreach (var culture in world.Cultures.Values)
        {
            var mine = world.Regions.Values.Where(r => r.CultureId == culture.Id).Select(r => r.Id).ToHashSet();
            var seen = new HashSet<long> { culture.HomeRegionId };
            var queue = new Queue<long>(seen);
            while (queue.Count > 0)
                foreach (var n in graph.Neighbours(queue.Dequeue()))
                    if (mine.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(mine.Count, seen.Count);
        }
    }

    [Fact]
    public void Cultures_HaveDistinctNameStyles()
    {
        var world = Medium();
        var styles = world.Cultures.Values.Select(c => string.Join("|", c.Style.Onsets) + "/" + string.Join("|", c.Style.Vowels)).ToList();
        Assert.True(styles.Distinct().Count() >= styles.Count - 1);
    }

    [Fact]
    public void NameStyle_ShapesNames()
    {
        var style = new NameStyle(new[] { "k" }, new[] { "a" }, new[] { "" });
        var names = new NameGen(new Rng(1), style);
        Assert.All(Enumerable.Range(0, 20).Select(_ => names.Given()), n => Assert.Matches("^Ka(ka)*$", n));
    }

    [Fact]
    public void Culture_JsonRoundTrip()
    {
        var culture = Medium().Cultures[1];
        var copy = JsonSerializer.Deserialize<Culture>(JsonSerializer.Serialize(culture))!;
        Assert.Equal(culture.Style.Onsets, copy.Style.Onsets);
        Assert.Equal(culture.Lineage, copy.Lineage);
    }

    [Fact]
    public void Invariants_CatchUnknownCulture()
    {
        var world = Medium();
        world.Regions[1].CultureId = 99;
        Assert.Contains(Invariants.Check(world, new EventLog()), e => e.Contains("unknown culture #99"));
    }
}

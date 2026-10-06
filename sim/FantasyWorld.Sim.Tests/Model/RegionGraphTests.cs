using System.Text.Json;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Tests.Model;

public class RegionGraphTests
{
    // 1 -2- 2 -2- 3, and a slow direct road 1 -10- 3; 4 is isolated.
    private static RegionGraph Sample() => new(new[]
    {
        new RegionLink(1, 2, LinkKind.Land, 2, 5, false, false),
        new RegionLink(2, 3, LinkKind.Land, 2, 5, true, false),
        new RegionLink(1, 3, LinkKind.Sea, 10, 0, false, false),
    });

    [Fact]
    public void Neighbours_AreSymmetric()
    {
        var g = Sample();
        Assert.Equal(new long[] { 2, 3 }, g.Neighbours(1).OrderBy(x => x));
        Assert.Equal(new long[] { 1, 2 }, g.Neighbours(3).OrderBy(x => x));
        Assert.Empty(g.Neighbours(4));
        Assert.True(g.Between(3, 2)!.RiverCrossing);
    }

    [Fact]
    public void ShortestPath_PrefersCheaperRoute()
    {
        var (path, days) = Sample().ShortestPath(1, 3)!.Value;
        Assert.Equal(new long[] { 1, 2, 3 }, path);
        Assert.Equal(4, days);
    }

    [Fact]
    public void ShortestPath_ToSelf_IsEmptyJourney()
    {
        var (path, days) = Sample().ShortestPath(2, 2)!.Value;
        Assert.Equal(new long[] { 2 }, path);
        Assert.Equal(0, days);
    }

    [Fact]
    public void ShortestPath_Unreachable_IsNull() => Assert.Null(Sample().ShortestPath(1, 4));

    [Fact]
    public void Links_MustBeOrdered() =>
        Assert.Throws<ArgumentException>(() => new RegionGraph(new[] { new RegionLink(2, 1, LinkKind.Land, 1, 1, false, false) }));

    [Fact]
    public void JsonRoundTrip_RebuildsAdjacency()
    {
        var copy = JsonSerializer.Deserialize<RegionGraph>(JsonSerializer.Serialize(Sample()))!;
        Assert.Equal(3, copy.Links.Count);
        Assert.Equal(4, copy.ShortestPath(1, 3)!.Value.Days);
    }
}

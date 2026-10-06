using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class NameGenTests
{
    [Fact]
    public void Names_AreDeterministic()
    {
        var a = new NameGen(new Rng(3));
        var b = new NameGen(new Rng(3));
        for (var i = 0; i < 20; i++) Assert.Equal(a.Region(), b.Region());
    }

    [Fact]
    public void Names_AreUniqueAndCapitalised()
    {
        var names = new NameGen(new Rng(8));
        var all = Enumerable.Range(0, 300).Select(i => i % 2 == 0 ? names.Region() : names.Settlement()).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, n => Assert.True(char.IsUpper(n[0]) && n.Length >= 3, n));
    }
}

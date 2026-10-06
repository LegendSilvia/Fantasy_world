using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.WorldGen;

public class ArtifactForgeTests
{
    private static List<Artifact> Forge(ulong seed, int count, IReadOnlyList<string>? rare = null, ArtifactType? type = null)
    {
        var rng = new Rng(seed);
        var ids = new IdGen();
        var names = new NameGen(new Rng(seed + 1));
        return Enumerable.Range(0, count).Select(_ => ArtifactForge.Create(rng, ids, names, 0, type, rare)).ToList();
    }

    [Fact]
    public void Forge_IsDeterministic() =>
        Assert.Equal(Forge(3, 20).Select(a => a.Name), Forge(3, 20).Select(a => a.Name));

    [Fact]
    public void Artifacts_HaveOneToThreeFittingPowers()
    {
        foreach (var a in Forge(4, 200))
        {
            Assert.InRange(a.Powers.Count, 1, 3);
            Assert.Contains(a.Powers, p => p.Kind == "attribute");
            Assert.All(a.Powers.Where(p => p.Kind == "attribute"), p => Assert.InRange(p.Amount, 1, 5));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
        }
    }

    [Fact]
    public void Tomes_TeachLore()
    {
        Assert.All(Forge(5, 30, type: ArtifactType.Tome), a => Assert.True(a.Bonus("lore") > 0));
    }

    [Fact]
    public void RareMaterials_AppearWhenAvailable()
    {
        var made = Forge(6, 100, rare: new[] { ResourceKinds.Mithril });
        Assert.InRange(made.Count(a => a.Material == ResourceKinds.Mithril), 40, 80);
    }

    [Fact]
    public void SomeArtifacts_AreCursed()
    {
        var cursed = Forge(7, 300).Count(a => a.Curse is not null);
        Assert.InRange(cursed, 35, 90);
    }

    [Fact]
    public void Ids_AreSequential() =>
        Assert.Equal(new long[] { 1, 2, 3 }, Forge(8, 3).Select(a => a.Id));
}

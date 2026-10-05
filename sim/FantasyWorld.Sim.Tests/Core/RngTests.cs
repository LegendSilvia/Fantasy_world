using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class RngTests
{
    [Fact]
    public void Fnv1a64_MatchesReferenceValues()
    {
        Assert.Equal(14695981039346656037UL, StableHash.Fnv1a64(""));
        Assert.Equal(0xAF63DC4C8601EC8CUL, StableHash.Fnv1a64("a"));
    }

    [Fact]
    public void SplitMix64_MatchesReferenceValue()
    {
        Assert.Equal(0xE220A8397B1DCDAFUL, new Rng(0).NextULong());
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void SameStreamAndTick_AreEqual() =>
        Assert.Equal(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "economy", 30).NextULong());

    [Fact]
    public void DifferentStreams_Differ() =>
        Assert.NotEqual(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "politics", 30).NextULong());

    [Fact]
    public void DifferentTicks_Differ() =>
        Assert.NotEqual(Rng.ForStream(42, "economy", 30).NextULong(), Rng.ForStream(42, "economy", 60).NextULong());

    [Fact]
    public void DifferentSeeds_Differ() =>
        Assert.NotEqual(Rng.ForStream(1, "economy", 30).NextULong(), Rng.ForStream(2, "economy", 30).NextULong());

    [Fact]
    public void NextDouble_IsInUnitInterval()
    {
        var rng = new Rng(7);
        for (var i = 0; i < 10_000; i++)
        {
            var d = rng.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void NextInt_StaysInRangeAndHitsBothEnds()
    {
        var rng = new Rng(7);
        var seen = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.NextInt(-2, 3);
            Assert.InRange(v, -2, 2);
            seen.Add(v);
        }
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void NextInt_RejectsEmptyRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rng(1).NextInt(3, 3));

    [Fact]
    public void Chance_RespectsExtremes()
    {
        var rng = new Rng(3);
        for (var i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0.0));
            Assert.True(rng.Chance(1.0));
        }
    }
}

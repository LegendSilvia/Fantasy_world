using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Geo;

namespace FantasyWorld.Sim.Tests.Geo;

public class NoiseTests
{
    [Fact]
    public void SameSeed_SameValues()
    {
        var a = new Noise2D(5);
        var b = new Noise2D(5);
        for (var i = 0; i < 100; i++)
            Assert.Equal(a.Fractal(i * 0.37, i * 0.11), b.Fractal(i * 0.37, i * 0.11));
    }

    [Fact]
    public void DifferentSeeds_Differ() =>
        Assert.NotEqual(new Noise2D(1).Fractal(3.3, 4.4), new Noise2D(2).Fractal(3.3, 4.4));

    [Fact]
    public void Values_AreInUnitInterval_AndVaried()
    {
        var noise = new Noise2D(9);
        var values = new List<double>();
        for (var y = 0; y < 50; y++)
            for (var x = 0; x < 50; x++)
                values.Add(noise.Fractal(x / 7.0, y / 7.0));
        Assert.All(values, v => Assert.InRange(v, 0.0, 0.9999999999999999));
        Assert.True(values.Max() - values.Min() > 0.4, "noise should vary across the field");
    }

    [Fact]
    public void Noise_IsContinuous()
    {
        var noise = new Noise2D(9);
        Assert.InRange(Math.Abs(noise.Value(10.5, 3.5) - noise.Value(10.501, 3.5)), 0, 0.01);
    }

    [Fact]
    public void Derive_GivesDistinctStableSeeds()
    {
        Assert.Equal(StableHash.Derive(7, "a"), StableHash.Derive(7, "a"));
        Assert.NotEqual(StableHash.Derive(7, "a"), StableHash.Derive(7, "b"));
    }
}

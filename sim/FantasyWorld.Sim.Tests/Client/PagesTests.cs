using FantasyWorld.Client;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Client;

public class PagesTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 40);

    [Fact]
    public void AWhyChain_IsLaidOutTopDown_WithoutOverlaps_AndEveryEdgeConnected()
    {
        var war = Aged.Log.All.Last(e => e.Causes.Count > 0 && Aged.Log.TryGet(e.Causes[0].EventId, out var c) && c.Causes.Count > 0);
        var why = WhyScreen.Build(Aged.World, Aged.Log, war.Id);
        var graph = WhyLayout.Layout(why, 680);
        Assert.Equal(1 + why.Layers.Sum(l => l.Count), graph.Cards.Count);
        Assert.Equal(0, graph.Cards[0].Y);
        Assert.Equal(war.Id, graph.Cards[0].Card.EventId);
        Assert.All(graph.Cards, c => Assert.True(c.X >= 0 && c.X + c.Width <= 680.5f && c.Width >= WhyLayout.MinCard - 0.5f));
        foreach (var a in graph.Cards)
            foreach (var b in graph.Cards.Where(b => b != a))
                Assert.False(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + WhyLayout.CardHeight && b.Y < a.Y + WhyLayout.CardHeight);
        Assert.Equal(why.Edges.Count, graph.Connectors.Count);
        Assert.All(graph.Connectors, c => Assert.Equal(4, c.Points.Count));
        Assert.True(graph.Height >= WhyLayout.CardHeight * (1 + why.Layers.Count) - 1);
    }

    [Fact]
    public void TheMiniMap_PaintsLandAndSea()
    {
        var pixels = MiniMap.Pixels(Aged.World.Geography!.Terrain, 32);
        Assert.Equal(32 * 32 * 4, pixels.Length);
        Assert.True(Enumerable.Range(0, 32 * 32).Select(i => (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2])).Distinct().Count() > 50);
        Assert.All(Enumerable.Range(0, 32 * 32), i => Assert.Equal(255, pixels[i * 4 + 3]));
    }
}

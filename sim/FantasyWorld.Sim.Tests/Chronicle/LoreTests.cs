using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Chronicle;

/// <summary>The world's legend and its secrets (the owner, 2026-10-29).</summary>
public class LoreTests
{
    private static Simulator Aged(int years, WorldSize size = WorldSize.Tiny, ulong seed = 3) =>
        Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(seed, size), DateTimeOffset.UnixEpoch), years);

    [Fact]
    public void TheLegend_IsToldInFull_InTheFirstEpisode_AndInBrief_AfterIt()
    {
        var sim = Aged(20);
        var legend = Legends.Rules(sim.World, 0);
        Assert.Contains(sim.World.Name, legend.Summary);
        Assert.True(legend.Full.Length > legend.Summary.Length);
        Assert.InRange(legend.Summary.Length, 1, Legends.MaxSummary);
        Assert.InRange(legend.Full.Length, 1, Legends.MaxFull);
        sim.World.Legend = legend;

        var first = Episode.Build(sim.World, sim.Log, 0, "2026-10-29");
        Assert.Equal(EpisodeSection.Legend, first.Parts[1].Kind);
        Assert.Equal(legend.Full, first.Parts[1].Text);
        Assert.DoesNotContain(legend.Summary, first.Parts[0].Text);

        // A world given its legend after its first episode tells it in full in the next one, even with nothing new since.
        Assert.Contains(Episode.Build(sim.World, sim.Log, sim.Log.All[^1].Id, "2026-10-30").Parts, p => p.Kind == EpisodeSection.Legend);

        // Once an episode that told it in full has aired, the next ones tell it in brief, with no primer.
        sim.World.Legend = legend with { Told = true };
        var later = Episode.Build(sim.World, sim.Log, sim.Log.All[^1].Id - 5, "2026-10-30");
        Assert.DoesNotContain(later.Parts, p => p.Kind == EpisodeSection.Legend);
        Assert.DoesNotContain(later.Parts, p => p.Kind == EpisodeSection.Primer);
        Assert.EndsWith(legend.Summary, later.Parts[0].Text);
    }

    [Fact]
    public void ALabyrinthCleared_RevealsASecretOfTheWorld_ToldAsNews_AndKept()
    {
        var sim = Aged(120, WorldSize.Medium, 11);
        var cleared = sim.Log.All.Where(e => e.Type == "LabyrinthCleared").ToList();
        Assert.NotEmpty(cleared);
        var revealed = sim.Log.All.Where(e => e.Type == "SecretRevealed").ToList();
        Assert.Equal(cleared.Count, revealed.Count);
        Assert.All(revealed, r => Assert.Contains(r.Causes, c => cleared.Any(e => e.Id == c.EventId)));
        Assert.Equal(revealed.Select(r => r.Id), sim.World.Secrets.Select(s => s.EventId));
        Assert.Equal(sim.World.Secrets.Count, sim.World.Secrets.Select(s => s.Title).Distinct().Count());
        var told = new FantasyWorld.Sim.Narration.TemplateNarrator(sim.World).Sentence(revealed[0]);
        Assert.Contains(sim.World.Secrets[0].Title, told, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sim.World.Secrets[0].Text, told);

        // Kept with the world.
        var back = WorldSnapshot.Capture(sim.World, sim.Queue).ToWorld();
        Assert.Equal(sim.World.Secrets, back.Secrets);
    }
}

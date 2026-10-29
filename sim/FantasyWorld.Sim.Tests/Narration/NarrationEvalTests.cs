using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Views;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Narration;

/// <summary>
/// Structural evals of the template voice (sub-project 5 §5), run on every test run over a real history, and a
/// golden narration file for a fixed seed (regenerate deliberately with UPDATE_GOLDEN=1 and read the diff).
/// </summary>
public class NarrationEvalTests
{
    private static readonly Simulator Aged = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(4, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 60);

    [Fact]
    public void EverySentence_IsFilled_Capitalised_Bounded_AndNeverStutters()
    {
        var narrator = new TemplateNarrator(Aged.World);
        var nameOf = Names.Of(Aged.World);
        foreach (var ev in Aged.Log.All)
        {
            var s = narrator.Sentence(ev);
            Assert.False(s.Contains('{') || s.Contains('}'), $"unfilled placeholder: {s}");
            Assert.True(char.IsUpper(s[0]) && s.EndsWith('.'), $"not a sentence: {s}");
            // A secret of the world is told whole (the owner, 2026-10-29): only the words around it are bounded.
            var words = (ev.Type == "SecretRevealed" && ev.Payload.TryGetValue("text", out var truth) ? s.Replace(truth, "") : s).Split(' ').Length;
            Assert.True(words is >= 2 and <= 45, $"{words} words: {s}");
            Assert.False(Regex.IsMatch(s, @"\b(\w+) \1\b", RegexOptions.IgnoreCase), $"doubled word: {s}");
            Assert.DoesNotContain("(s)", s);
            var linked = LinkedText.Link(s, LinkedText.Mentions(ev), nameOf);
            Assert.All(linked.Links, l => Assert.True(LinkedText.Reads(s.Substring(l.Start, l.Length), nameOf(l.Entity))));
        }
    }

    [Fact]
    public void Headlines_NeverRepeatTheirTitle_AndTheDigestKeepsItsSize()
    {
        var narrator = new TemplateNarrator(Aged.World);
        foreach (var ev in Aged.Log.All.Where(e => e.Importance >= 40))
            Assert.DoesNotContain(narrator.Title(ev), narrator.Body(ev, Aged.Log));
        var screen = DigestScreen.Build(Aged.World, Aged.Log, Digest.Build(Aged.Log, Aged.World, 0, new HashSet<EntityRef>()), new FantasyWorld.Sim.Core.WorldDate(0));
        Assert.InRange(1 + screen.Items.Count, 3, Digest.DefaultTake);
    }

    [Fact]
    public void TheStoryOfWhy_TellsEveryRoot()
    {
        var narrator = new TemplateNarrator(Aged.World);
        foreach (var war in Aged.Log.All.Where(e => e.Type is "WarDeclared" or "RevoltBegan" or "CharacterDied").Take(40))
        {
            var why = WhyScreen.Build(Aged.World, Aged.Log, war.Id);
            if (why.Story is null) continue;
            var roots = why.Layers.SelectMany(l => l).Where(c => c.IsRoot).Select(c => Aged.Log.Get(c.EventId));
            Assert.All(roots, r => Assert.Contains(narrator.Clause(r), why.Story.Text));
            Assert.DoesNotMatch(@"[,;] (In|The) |[a-z] The [A-Z]", why.Story.Text);
            Assert.All(why.Story.Links, l => Assert.True(LinkedText.Reads(why.Story.Text.Substring(l.Start, l.Length), Names.Of(Aged.World)(l.Entity))));
            var chain = narrator.NarrateChain(new HistoryQuery(Aged.Log).WhyChain(war.Id, 4));
            Assert.DoesNotMatch(@"[,;] (In|The) ", chain);
            var body = narrator.Body(war, Aged.Log);
            Assert.All(LinkedText.Link(body, LinkedText.Mentions(war).Concat(war.Causes.SelectMany(c => LinkedText.Mentions(Aged.Log.Get(c.EventId)))), Names.Of(Aged.World)).Links,
                l => Assert.True(LinkedText.Reads(body.Substring(l.Start, l.Length), Names.Of(Aged.World)(l.Entity))));
        }
    }

    [Fact]
    public void TheNarrationOfAFixedWorld_MatchesItsGolden()
    {
        var narrator = new TemplateNarrator(Aged.World);
        var text = new StringBuilder();
        foreach (var ev in Aged.Log.All.Where(e => e.Importance >= 30).Take(80)) text.AppendLine(narrator.Narrate(ev));
        var war = Aged.Log.All.LastOrDefault(e => e.Type == "WarDeclared") ?? Aged.Log.All.Last(e => e.Causes.Count > 0);
        text.AppendLine().AppendLine(narrator.NarrateChain(new HistoryQuery(Aged.Log).WhyChain(war.Id, 4)));
        text.AppendLine().AppendLine(WhyScreen.Build(Aged.World, Aged.Log, war.Id).Story?.Text);
        Golden("narration.txt", text.ToString());
    }

    private static void Golden(string name, string text, [CallerFilePath] string here = "")
    {
        var file = Path.Combine(Path.GetDirectoryName(here)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1") File.WriteAllText(file, text);
        Assert.Equal(File.ReadAllText(file), text);
    }
}

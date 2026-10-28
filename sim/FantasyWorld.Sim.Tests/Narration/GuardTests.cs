using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Narration;

public class GuardTests
{
    [Fact]
    public void ANarration_MayUseOnlyTheNamesAndNumbersOfItsBrief()
    {
        const string brief = "In Year 12, Maren of Holt fell in battle at Esk, aged 41.";
        Assert.Empty(NarrationGuard.Problems("Maren of Holt died at Esk in Year 12, at 41. The town grieved.", brief));
        // A sentence may not open with a word the brief lacks: a false alarm costs only the template prose.
        Assert.Equal(new[] { "name Grief" }, NarrationGuard.Problems("Grief lay on Esk.", brief));
        Assert.Equal(new[] { "name Tarvo" }, NarrationGuard.Problems("Maren of Holt died at Esk beside her friend Tarvo.", brief));
        Assert.Equal(new[] { "number 300" }, NarrationGuard.Problems("Maren of Holt died at Esk with 300 others.", brief));
    }

    [Fact]
    public void ASentence_MayOpenWithAPlainWord_TheScriptOrTheTaskUses_ButNotWithAnInventedName()
    {
        // The live episode of 2026-10-28 lost the chronicler's script to these two openings.
        const string brief = "Faern of Ostherow was humiliated at Haencellvale. What to make of it all?";
        const string task = "your own view of the year: what it meant, who rose and who fell, what to watch for";
        var script = "Faern of Ostherow was humiliated at Haencellvale. Humiliation, in that house, is long remembered. Watch the house of Ostherow.";
        Assert.Empty(NarrationGuard.Problems(script, brief, task));
        // A plain word the script uses in lowercase elsewhere may open a sentence too.
        Assert.Empty(NarrationGuard.Problems("Ruin came to Haencellvale, and the ruin was long.", brief));
        // Names are still names, at an opening or anywhere else, whatever the task says.
        Assert.Equal(["name Orrin"], NarrationGuard.Problems(script + " Orrin wept.", brief, task));
        Assert.Equal(["name Grief"], NarrationGuard.Problems("Grief lay on Haencellvale.", brief, task));
    }

    [Fact]
    public void TheGuard_ReadsPossessivesHyphensTitlesAndNumbersInWords()
    {
        const string brief = "In Year 76, the Duchy of Wosmeim ceded two regions; Nellsi of Ard died, aged 63.";
        Assert.Empty(NarrationGuard.Problems("Wosmeim's duke gave up 2 regions. At 63, Nellsi of Ard, Wosmeim-born, died before the Duke.", brief));
        Assert.Equal(new[] { "number 7" }, NarrationGuard.Problems("Nellsi of Ard died with 7 others.", brief));
        Assert.Equal(new[] { "name Orrin" }, NarrationGuard.Problems("Orrin the smith wept for Nellsi of Ard.", brief));
    }

    [Fact]
    public void AnInventingNarration_IsRejected_Journaled_AndTheTemplatesSpeak()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var ev = log.Append(world.Date, new EventDraft("RevoltBegan", Place: world.Settlements[1].Ref));
        var fake = new FakeLlmProvider(_ => "{\"text\":\"Led by the smith Orrin, the town rose.\"}");
        var gateway = new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal());
        var narrator = new LlmNarrator(new StoryContext(world, log, gateway));

        Assert.Equal(new TemplateNarrator(world).Narrate(ev), narrator.Narrate(ev));
        Assert.Contains(gateway.Journal.All, entry => entry.Note.StartsWith("rejected: ", StringComparison.Ordinal) && entry.Note.Contains("name Orrin"));
    }

    [Fact]
    public void AScriptForTheEar_MayGreetLinkAndTakeItsLeave_ButNotInventAName()
    {
        const string brief = "The chronicle of Moumgrillfell begins in Year 101.\n\nDendbreil of Veloundard is dead.\n\n"
                             + "So it was set down by Grurcun of the Ound. May the gods keep Moumgrillfell until we meet again.";
        const string script = "Welcome back to the chronicle of Moumgrillfell, in Year 101. Our gravest news: Dendbreil of Veloundard is dead. "
                              + "So it was set down by Grurcun of the Ound. May the gods keep Moumgrillfell until we meet again. Farewell.";
        Assert.Empty(NarrationGuard.Problems(script, brief));
        Assert.Equal(["name Orrin"], NarrationGuard.Problems(script + " Orrin wept.", brief));
        Assert.Equal(["number Nineteen"], NarrationGuard.Problems(script + " Nineteen years passed.", brief));
    }

    [Fact]
    public void NumbersInWords_MustComeFromTheBrief()
    {
        const string brief = "Three years have passed in Aerenmoor. 10,000 soldiers marched; two hundred and six died at Fiar.";
        Assert.True(NarrationGuard.Faithful("After three years, ten thousand marched, and two hundred and six fell at Fiar.", brief));
        Assert.Contains("number four", NarrationGuard.Problems("After four years of war, the host marched.", brief));
        Assert.Contains("number Twenty-one", NarrationGuard.Problems("Twenty-one died at Fiar.", brief));
        Assert.Contains("number Nine thousand", NarrationGuard.Problems("Nine thousand fell.", brief));
        Assert.True(NarrationGuard.Faithful("No one at Fiar forgot it, and one of them wept.", brief)); // "one" is not a count
        Assert.Equal(new[] { (206L, "two hundred and six"), (3L, "three") },
            NarrationGuard.NumberWords("two hundred and six men and three horses"));
    }
}

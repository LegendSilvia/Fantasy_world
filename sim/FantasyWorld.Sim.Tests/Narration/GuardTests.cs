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
        Assert.Contains(gateway.Journal.All, entry => entry.Note == "rejected");
    }
}

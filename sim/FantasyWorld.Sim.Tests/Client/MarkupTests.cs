using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Client;

public class MarkupTests
{
    [Fact]
    public void LinkedNames_BecomeUrls_AndBracketsAreEscaped()
    {
        var text = new LinkedText("Maren [the Bold] rode to Esk.", new[]
        {
            new TextLink(25, 3, new EntityRef("settlement", 4)),
            new TextLink(0, 16, new EntityRef("char", 12)),
        });
        Assert.Equal("[url=char#12]Maren [lb]the Bold][/url] rode to [url=settlement#4]Esk[/url].", Markup.Bbcode(text));
        Assert.Equal("No links [lb]here].", Markup.Bbcode(LinkedText.Plain("No links [here].")));
    }

    [Fact]
    public void TimeAway_ReadsInItsLargestUnit()
    {
        Assert.Equal("now", Markup.Elapsed(0));
        Assert.Equal("+1 day", Markup.Elapsed(1));
        Assert.Equal("+4 months", Markup.Elapsed(4 * WorldDate.DaysPerMonth + 7));
        Assert.Equal("+2 years", Markup.Elapsed(2 * WorldDate.DaysPerYear + 40));
    }

    [Fact]
    public void TheStatusLine_SaysCaughtUpOrHowFarBehind()
    {
        Assert.Equal("Caught up · Year 312, day 196", DigestScreen.StatusLine(WorldDate.FromYmd(312, 7, 16), 0));
        Assert.Equal("Catching up · 1,200 days behind", DigestScreen.StatusLine(new WorldDate(0), 1200));
    }
}

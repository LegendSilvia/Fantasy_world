using System.Text;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>Text for the screens: linked names as rich-text markup, and short spans of world time.</summary>
public static class Markup
{
    /// <summary>
    /// BBCode for a rich-text label: each linked span becomes [url=kind#id]…[/url] (the label raises the ref when
    /// clicked), and brackets in the prose are escaped so names like "[the Grey]" cannot inject markup.
    /// </summary>
    public static string Bbcode(LinkedText text)
    {
        var builder = new StringBuilder();
        var at = 0;
        foreach (var link in text.Links.OrderBy(l => l.Start))
        {
            if (link.Start < at || link.Start + link.Length > text.Text.Length) continue;
            builder.Append(Escape(text.Text[at..link.Start]));
            builder.Append("[url=").Append(link.Entity).Append(']').Append(Escape(text.Text.Substring(link.Start, link.Length))).Append("[/url]");
            at = link.Start + link.Length;
        }
        return builder.Append(Escape(text.Text[at..])).ToString();
    }

    public static string Escape(string text) => text.Replace("[", "[lb]");

    /// <summary>The control card's "+4 months": world time away in its largest whole unit.</summary>
    public static string Elapsed(long days) => days switch
    {
        <= 0 => "now",
        < WorldDate.DaysPerMonth => $"+{days} day{(days == 1 ? "" : "s")}",
        < WorldDate.DaysPerYear => $"+{days / WorldDate.DaysPerMonth} month{(days / WorldDate.DaysPerMonth == 1 ? "" : "s")}",
        _ => $"+{days / WorldDate.DaysPerYear} year{(days / WorldDate.DaysPerYear == 1 ? "" : "s")}",
    };
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace FantasyWorld.Broadcast.Publishing;

/// <summary>One narrated episode as the feed and the archive show it.</summary>
public sealed record FeedItem(string Date, int ToYear, string? Headline, long AudioBytes, int AudioSeconds, DateTimeOffset Published);

/// <summary>
/// The podcast feed and the archive page (M3), rebuilt each morning from the manifest: RSS 2.0 with the iTunes tags
/// podcast apps read (cover, duration, explicit, category), each item an MP3 enclosure with a stable GUID (world and
/// date), newest first.
/// </summary>
public static class Feed
{
    public const string FeedKey = "feed.xml", ArchiveKey = "index.html", CoverKey = "cover.png";
    public const int MaxItems = 100;

    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>Text as XML can hold it: characters XML forbids (control characters) are dropped.</summary>
    public static string Clean(string text) => new(text.Where(System.Xml.XmlConvert.IsXmlChar).ToArray());

    public static string Title(string worldName) => $"The Chronicle of {worldName}";

    /// <summary>The items of a manifest: every day with narration, newest first, dated at its morning in the broadcast's zone.</summary>
    public static IReadOnlyList<FeedItem> Items(Manifest manifest, BroadcastConfig config) =>
        manifest.Episodes.Where(d => d.Value.Done(Manifest.AudioStep)).OrderByDescending(d => d.Key, StringComparer.Ordinal).Take(MaxItems)
            .Select(d => new FeedItem(d.Key, d.Value.ToYear, d.Value.Headline, d.Value.AudioBytes, d.Value.AudioSeconds, MorningOf(d.Key, config)))
            .ToList();

    /// <summary>05:30 in the broadcast's zone on the date (the schedule's hour).</summary>
    public static DateTimeOffset MorningOf(string date, BroadcastConfig config)
    {
        var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(5.5);
        return new DateTimeOffset(day, config.Zone.GetUtcOffset(day));
    }

    /// <param name="ownerEmail">The podcast directories' contact for proving ownership; optional.</param>
    public static string Rss(string worldName, string baseUrl, IReadOnlyList<FeedItem> items, string? ownerEmail = null)
    {
        string Url(string key) => $"{baseUrl.TrimEnd('/')}/{key}";
        worldName = Clean(worldName);
        var channel = new XElement("channel",
            new XElement("title", Title(worldName)),
            new XElement("link", Url(ArchiveKey)),
            new XElement(Atom + "link", new XAttribute("href", Url(FeedKey)), new XAttribute("rel", "self"), new XAttribute("type", "application/rss+xml")),
            new XElement("description", $"Every morning the world of {worldName} lives one more year, and its chronicler tells what happened and why."),
            new XElement("language", "en"),
            new XElement(Itunes + "author", "The Chronicle"),
            new XElement(Itunes + "owner", new XElement(Itunes + "name", "The Chronicle"), ownerEmail is null ? null : new XElement(Itunes + "email", ownerEmail)),
            new XElement(Itunes + "image", new XAttribute("href", Url(CoverKey))),
            new XElement(Itunes + "category", new XAttribute("text", "Fiction")),
            new XElement(Itunes + "explicit", "false"),
            new XElement(Itunes + "type", "episodic"),
            items.Select(item => new XElement("item",
                new XElement("title", Clean(ItemTitle(worldName, item))),
                new XElement("description", item.Headline is null ? "A quiet year in the chronicle." : $"{Clean(item.Headline).TrimEnd('.')}."),
                new XElement("guid", new XAttribute("isPermaLink", "false"), $"{worldName}-{item.Date}"),
                new XElement("pubDate", item.Published.ToString("r", CultureInfo.InvariantCulture)),
                new XElement("enclosure", new XAttribute("url", Url(DailyRun.NarrationKey(item.Date))),
                    new XAttribute("length", item.AudioBytes), new XAttribute("type", "audio/mpeg")),
                new XElement(Itunes + "duration", item.AudioSeconds),
                new XElement(Itunes + "episodeType", "full"))));
        var rss = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
                new XAttribute(XNamespace.Xmlns + "atom", Atom), channel));
        using var writer = new Utf8StringWriter();
        rss.Save(writer);
        return writer.ToString();
    }

    public static string ItemTitle(string worldName, FeedItem item) =>
        item.Headline is null ? $"{worldName}, Year {item.ToYear}" : $"Year {item.ToYear}: {item.Headline}";

    /// <summary>The archive page: every episode with its map, its narration and its script.</summary>
    public static string Archive(string worldName, IReadOnlyList<FeedItem> items)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(Title(worldName))}</title>
            <link rel="alternate" type="application/rss+xml" title="{E(Title(worldName))}" href="{FeedKey}">
            <style>
            body {"{"} margin: 0 auto; max-width: 46rem; padding: 2rem 1rem; background: #ecebe7; color: #1a1a18; font: 16px/1.5 Georgia, serif; {"}"}
            h1 {"{"} font-style: italic; font-weight: normal; font-size: 2.4rem; margin: 0 0 .25rem; {"}"}
            article {"{"} background: #f7f6f3; border: 1px solid #dcdad4; border-radius: 12px; padding: 1rem 1.25rem; margin: 1rem 0; {"}"}
            .when {"{"} color: #66645d; font: 13px system-ui, sans-serif; {"}"} img {"{"} width: 100%; border-radius: 8px; {"}"} audio {"{"} width: 100%; {"}"}
            </style></head><body>
            <h1>{E(Title(worldName))}</h1>
            <p class="when">A new episode every morning · <a href="{FeedKey}">podcast feed</a></p>

            """);
        foreach (var item in items)
            html.Append($"""
                <article>
                <p class="when">{E(item.Date)} · Year {item.ToYear} · {item.AudioSeconds / 60}:{item.AudioSeconds % 60:00}</p>
                <h2>{E(item.Headline ?? "A quiet year")}</h2>
                <audio controls preload="none" src="{DailyRun.NarrationKey(item.Date)}"></audio>
                <p><a href="{DailyRun.ScriptKey(item.Date)}">Read the script</a></p>
                <img loading="lazy" alt="The map that morning" src="{DailyRun.MapKey(item.Date)}" onerror="this.remove()">
                </article>

                """);
        html.Append("</body></html>\n");
        return html.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}

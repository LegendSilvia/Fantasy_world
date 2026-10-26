using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>One story of an episode: the event, its year, its category and how the chronicle tells it.</summary>
public sealed record EpisodeStory(long EventId, int Year, string Category, string Text);

/// <summary>
/// One episode of the daily broadcast (sub-project 6 §3.2): what happened since the audience last listened, as data
/// (the cold open, the headline, the other stories, the headline's chain of causes, the sign-off) and as a script to
/// read aloud. Built from the digest's ranking, so the broadcast and the morning digest agree on what mattered. Writer says
/// who wrote the script: the template voice, or the chronicler (the LLM, held to the template's facts).
/// </summary>
public sealed record Episode(
    string Date, string WorldName, string Chronicler, int FromYear, int ToYear, int EventCount, long FirstEventId, long LastEventId,
    string ColdOpen, EpisodeStory? Headline, string HeadlineBody, IReadOnlyList<EpisodeStory> Stories, string? Why, string SignOff, string Script,
    string Writer = Episode.TemplateWriter)
{
    public const string TemplateWriter = "template", ChroniclerWriter = "chronicler";

    public const int DefaultStories = 5;

    /// <summary>The template script stays under this many characters (about four minutes read aloud).</summary>
    public const int DefaultMaxCharacters = 3600;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [JsonIgnore] public int YearsPassed => Math.Max(0, ToYear - FromYear);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Episode FromJson(string json) => JsonSerializer.Deserialize<Episode>(json, Json)
                                                   ?? throw new JsonException("An episode file was empty.");

    /// <summary>
    /// The episode for <paramref name="date"/> (the broadcast date, e.g. "2026-10-27") from the events after
    /// <paramref name="sinceEventId"/>. It never moves the broadcast marker; the caller does, when it saves the world.
    /// </summary>
    public static Episode Build(World world, EventLog log, long sinceEventId, string date, IReadOnlySet<EntityRef>? followed = null,
        int stories = DefaultStories, int maxCharacters = DefaultMaxCharacters)
    {
        followed ??= new HashSet<EntityRef>();
        var narrator = new TemplateNarrator(world);
        var name = DigestScreen.DisplayName(world);
        var chronicler = Narration.Chronicler.Of(world);
        // The first episode tells the latest year; the history before it is summed up in the cold open.
        var first = sinceEventId == 0;
        var events = log.Since(sinceEventId).Where(e => !first || e.Date.Day > world.Date.Day - WorldDate.DaysPerYear).ToList();
        var fromYear = log.TryGet(sinceEventId, out var last) ? last.Date.Year : events.Count > 0 ? events[0].Date.Year : world.Date.Year;
        var toYear = world.Date.Year;
        var ranked = events.Select(e => (Event: e, Score: Importance.Score(e, world, followed)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Event.Id).Select(x => x.Event).ToList();
        // News that shapes the world leads (a war, a fall, a legend), however famous the people in smaller stories are.
        // The engine's own weight decides among them, so a realm's fall outranks a famous lord's death.
        var top = ranked.Where(e => TemplateNarrator.IsHeadline(e.Type)).OrderByDescending(e => e.Importance).FirstOrDefault() ?? ranked.FirstOrDefault();
        // One story per person, so a busy hero does not fill the episode; and one of a kind per party, so a fallen
        // realm's many cessions are told once.
        var chosen = new List<WorldEvent>();
        var people = new HashSet<EntityRef>(top?.Actors.Values.Where(a => a.Kind == Character.Kind) ?? Array.Empty<EntityRef>());
        foreach (var ev in ranked.Where(e => e != top))
        {
            if (chosen.Count == stories) break;
            var who = ev.Actors.Values.Where(a => a.Kind == Character.Kind).ToList();
            if (who.Any(people.Contains)) continue;
            if (chosen.Prepend(top).Any(c => c is not null && c.Type == ev.Type && c.Actors.Values.Intersect(ev.Actors.Values).Any())) continue;
            people.UnionWith(who);
            chosen.Add(ev);
        }

        EpisodeStory Story(WorldEvent ev) => new(ev.Id, ev.Date.Year, Categories.Label(Categories.Of(ev)), narrator.Sentence(ev));
        var headline = top is null ? null : new EpisodeStory(top.Id, top.Date.Year, Categories.Label(Categories.Of(top)), narrator.Title(top));
        var others = chosen.OrderBy(e => e.Id).Select(Story).ToList();
        // A why needs at least one cause still in the log (old minor events are condensed away).
        IReadOnlyList<WorldEvent> thread = top is null ? [] : Thread(log, top);
        var why = thread.Count > 1 ? narrator.Story(thread) : null;
        // The body leaves the cause to the story of why when there is one, and says nothing if all it has is when.
        var body = top is null ? "" : narrator.Body(top, log, withCause: why is null);
        if (top is not null && body == $"{Digest.SeasonLabel(top.Date)}.") body = "";
        var coldOpen = OpeningFor(name, fromYear, toYear, events.Count, sinceEventId == 0);
        var signOff = SignOffFor(chronicler, name);

        // The template script: drop the least important stories, then the why, until it fits the budget.
        // Within a year the stories need no dates; across years each says when.
        var dated = toYear - fromYear > 1 || chosen.Select(e => e.Date.Year).Distinct().Count() > 1;
        var lines = chosen.ToDictionary(e => e.Id, e => dated ? $"In Year {e.Date.Year}, {narrator.Clause(e)}" : narrator.Sentence(e));
        string Write(IReadOnlyList<EpisodeStory> told, string? because) => Compose(coldOpen, headline, body, told, lines, because, signOff);
        var keep = others.ToList();
        var script = Write(keep, why);
        while (script.Length > maxCharacters && keep.Count > 0)
        {
            var weakest = keep.OrderBy(s => chosen.FindIndex(e => e.Id == s.EventId)).Last();
            keep.Remove(weakest);
            script = Write(keep, why);
        }
        if (script.Length > maxCharacters && why is not null) script = Write(keep, why = null);

        return new Episode(date, name, chronicler.Name, fromYear, toYear, events.Count, events.Count > 0 ? events[0].Id : 0,
            events.Count > 0 ? events[^1].Id : sinceEventId, coldOpen, headline, body, keep, why, signOff, script);
    }

    public const int WhySteps = 5;

    /// <summary>
    /// The headline's strongest thread of causes: the outcome, then up to <paramref name="steps"/> causes behind it, each
    /// step back following the weightiest cause still in the log.
    /// </summary>
    public static IReadOnlyList<WorldEvent> Thread(EventLog log, WorldEvent outcome, int steps = WhySteps)
    {
        var thread = new List<WorldEvent> { outcome };
        var current = outcome;
        while (thread.Count <= steps && current.Causes.OrderByDescending(c => c.Weight).ThenByDescending(c => c.EventId)
                   .Select(c => log.TryGet(c.EventId, out var cause) ? cause : null).FirstOrDefault(c => c is not null && thread.All(t => t.Id != c.Id)) is { } next)
        {
            thread.Add(next);
            current = next;
        }
        return thread;
    }

    private static string OpeningFor(string world, int fromYear, int toYear, int events, bool first)
    {
        if (first) return $"The chronicle of {world} begins in Year {toYear}, with {English.Count(toYear - 1, "year")} of history behind it.";
        if (events == 0) return $"Year {toYear} in {world}, and a quiet one: nothing of note was recorded.";
        var years = toYear - fromYear;
        return years <= 1
            ? $"This is the chronicle of {world}, Year {toYear}."
            : $"{English.Capitalise(English.Count(years, "year"))} have passed in {world} since the chronicle last spoke. It is now Year {toYear}.";
    }

    private static string SignOffFor(Chronicler chronicler, string world) => chronicler.Temperament switch
    {
        "devout" => $"So it was set down by {chronicler.Name}. May the gods keep {world} until we meet again.",
        "wry" => $"{chronicler.Name}, keeper of the chronicle, will be here tomorrow, whether or not {world} behaves.",
        "melancholy" => $"Remember them. This was {chronicler.Name}, for the chronicle of {world}.",
        "proud" => $"Thus records {chronicler.Name}, chronicler of {world}. Until the next dawn.",
        _ => $"That is the year. This was {chronicler.Name}, for the chronicle of {world}.",
    };

    private static string Compose(string coldOpen, EpisodeStory? headline, string body, IReadOnlyList<EpisodeStory> stories,
        IReadOnlyDictionary<long, string> lines, string? why, string signOff)
    {
        var text = new StringBuilder(coldOpen);
        if (headline is not null) text.Append("\n\n").Append(headline.Text).Append('.');
        if (body.Length > 0) text.Append("\n\n").Append(body);
        if (stories.Count > 0)
        {
            text.Append("\n\nElsewhere in the chronicle:");
            foreach (var story in stories) text.Append("\n\n").Append(lines[story.EventId]);
        }
        if (why is not null) text.Append("\n\nHow did it come to this? ").Append(why);
        return text.Append("\n\n").Append(signOff).ToString();
    }
}

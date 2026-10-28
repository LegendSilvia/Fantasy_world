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
/// One section of an episode (sub-project 7 §3), read aloud on its own and shown as a chapter: its kind (opening,
/// headline, face-off, story, comment, closing), the chapter's title, its text and the event it tells (0 for the opening and
/// closing); a face-off carries both sides' figures, and the first section telling a war carries the war's moves in the
/// episode (sub-project 10 C3); a fight's section, its days as fought (sub-project 8 B3). A section with a picture carries the description of its still image (never read aloud) and,
/// once the broadcast has it, the image's key in its store. The sections' texts, joined by blank lines, are the script.
/// </summary>
public sealed record EpisodeSection(string Kind, string Title, string Text, long EventId = 0, FaceOff? FaceOff = null,
    IReadOnlyList<CampaignMove>? Moves = null, string? Scene = null, string? Still = null, IReadOnlyList<FightBeat>? Beats = null)
{
    /// <summary>Why: "how it came to this", in episodes made before 2026-10-27; Comment: the chronicler's view of the year that replaced it.</summary>
    public const string Opening = "opening", Headline = "headline", Challenge = "faceoff", Story = "story", Why = "why", Comment = "comment", Closing = "closing";

    /// <summary>The prayers of the year and the gods' answers (sub-project 13 N5): the players answer them on the website.</summary>
    public const string Prayers = "prayers";

    /// <summary>The events the prayers' section tells (never among the stories).</summary>
    public static readonly IReadOnlySet<string> PrayerEvents = new HashSet<string>(StringComparer.Ordinal) { "PrayerOffered", "PrayerAnswered", "PrayerUnheeded", "GodsBlessed" };

    /// <summary>At most this many new prayers are read aloud.</summary>
    public const int PrayersTold = 3;
}

/// <summary>
/// One episode of the daily broadcast (sub-project 6 §3.2): what happened since the audience last listened, as data
/// (the cold open, the headline, the other stories, the headline's chain of causes, the sign-off) and as a script to
/// read aloud. Built from the digest's ranking, so the broadcast and the morning digest agree on what mattered. Writer says
/// who wrote the script: the template voice, or the chronicler (the LLM, held to the template's facts).
/// </summary>
public sealed record Episode(
    string Date, string WorldName, string Chronicler, int FromYear, int ToYear, int EventCount, long FirstEventId, long LastEventId,
    string ColdOpen, EpisodeStory? Headline, string HeadlineBody, IReadOnlyList<EpisodeStory> Stories, string? Why, string SignOff, string Script,
    string Writer = Episode.TemplateWriter, IReadOnlyList<EpisodeSection>? Sections = null)
{
    /// <summary>The script as sections; an episode stored before sections existed is one section.</summary>
    [JsonIgnore] public IReadOnlyList<EpisodeSection> Parts => Sections is { Count: > 0 } s ? s : [new EpisodeSection(EpisodeSection.Opening, WorldName, Script)];

    /// <summary>The script made from these sections (the sections' texts joined by blank lines).</summary>
    public static string ScriptOf(IEnumerable<EpisodeSection> sections) => string.Join("\n\n", sections.Select(s => s.Text));

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
        var ranked = events.Where(e => !EpisodeSection.PrayerEvents.Contains(e.Type)).Select(e => (Event: e, Score: Importance.Score(e, world, followed)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Event.Id).Select(x => x.Event).ToList();
        // News that shapes the world leads (a war, a fall, a legend), however famous the people in smaller stories are.
        // The engine's own weight decides among them, so a realm's fall outranks a famous lord's death.
        var top = ranked.Where(e => TemplateNarrator.IsHeadline(e.Type)).OrderByDescending(e => e.Importance).FirstOrDefault() ?? ranked.FirstOrDefault();
        // One story per person, so a busy hero does not fill the episode; and one of a kind per party, so a fallen
        // realm's many cessions are told once.
        // The challenges of the episode (duels and wars declared) are face-offs of their own, the weightiest first, and are
        // not told again among the stories.
        var faceOffs = ranked.Where(e => FaceOffs.Declarations.Contains(e.Type)).OrderByDescending(e => e.Importance).ThenBy(e => e.Id)
            .Select(e => FaceOffs.For(world, e)).OfType<FaceOff>().Take(FaceOffs.PerEpisode).ToList();
        var chosen = new List<WorldEvent>();
        var people = new HashSet<EntityRef>(top?.Actors.Values.Where(a => a.Kind == Character.Kind) ?? Array.Empty<EntityRef>());
        // A fight that lasted days is one story, however many of its days made the news.
        var fights = new HashSet<long>();
        if (top is not null && Fights.RootOf(log, top) is var topFight and not 0) fights.Add(topFight);
        foreach (var ev in ranked.Where(e => e != top && faceOffs.All(f => f.EventId != e.Id)))
        {
            if (chosen.Count == stories) break;
            if (Fights.RootOf(log, ev) is var fight and not 0 && !fights.Add(fight)) continue;
            var who = ev.Actors.Values.Where(a => a.Kind == Character.Kind).ToList();
            if (who.Any(people.Contains)) continue;
            if (chosen.Prepend(top).Any(c => c is not null && c.Type == ev.Type && c.Actors.Values.Intersect(ev.Actors.Values).Any())) continue;
            people.UnionWith(who);
            chosen.Add(ev);
        }

        EpisodeStory Story(WorldEvent ev) => new(ev.Id, ev.Date.Year, Categories.Label(Categories.Of(ev)), narrator.Sentence(ev));
        var headline = top is null ? null : new EpisodeStory(top.Id, top.Date.Year, Categories.Label(Categories.Of(top)), narrator.Title(top));
        var others = chosen.OrderBy(e => e.Id).Select(Story).ToList();
        // In place of "how it came to this" (the owner, 2026-10-27), the chronicler's own view closes the news: the chronicler
        // (Claude) writes its comment and conclusion on the year; this line stands in for it when no chronicler answers.
        // The headline's body gives its cause in a line, and says nothing if all it has is when.
        string? why = null;
        var comment = headline is null ? null : $"What to make of it all? Of everything in this chronicle, this will be remembered longest: {headline.Text}.";
        var body = top is null ? "" : narrator.Body(top, log, withCause: true);
        if (top is not null && body == $"{Digest.SeasonLabel(top.Date)}.") body = "";
        var coldOpen = OpeningFor(name, fromYear, toYear, events.Count, sinceEventId == 0);
        var signOff = SignOffFor(chronicler, name);

        // The template script: drop the least important stories, then the chronicler's view, until it fits the budget.
        // Within a year the stories need no dates; across years each says when.
        var dated = toYear - fromYear > 1 || chosen.Select(e => e.Date.Year).Distinct().Count() > 1;
        var lines = chosen.ToDictionary(e => e.Id, e => dated ? $"In Year {e.Date.Year}, {narrator.Clause(e)}" : narrator.Sentence(e));
        var titles = chosen.Append(top).Where(e => e is not null).ToDictionary(e => e!.Id, e => Title(narrator, e!));
        var challenges = faceOffs.Select(f => new EpisodeSection(EpisodeSection.Challenge, $"Face-off: {f.A.Name} v {f.B.Name}",
            FaceOffs.Tell(f, log.TryGet(f.EventId, out var declared) ? narrator.PlaceOf(declared) ?? "the appointed place" : "the appointed place"),
            f.EventId, f)).ToList();
        // Each war's moves in the episode go with the first section that tells the war: a few are read, all are shown.
        var campaigns = new Dictionary<long, IReadOnlyList<CampaignMove>>();
        IReadOnlyList<CampaignMove> MovesOf(long war) =>
            campaigns.TryGetValue(war, out var known) ? known : campaigns[war] = Campaign.Moves(world, events, war, narrator);
        IReadOnlyList<EpisodeSection> WithMoves(IReadOnlyList<EpisodeSection> sections)
        {
            var told = new HashSet<long>();
            return sections.Select(s => s.EventId != 0 && log.TryGet(s.EventId, out var ev) && Campaign.WarOf(ev) is var war and not 0
                                        && told.Add(war) && MovesOf(war) is { Count: > 0 } moves
                ? s with { Text = Campaign.Tell(moves, s.EventId) is { Length: > 0 } march ? $"{s.Text}\n\n{march}" : s.Text, Moves = moves }
                : s).Select(WithBeats).Select(WithFight).ToList();
        }
        // A fight's section shows its face-off as it stands (the owner, 2026-10-28): the result if it ended within the
        // episode, else its course so far.
        EpisodeSection WithFight(EpisodeSection s)
        {
            if (s.FaceOff is not null || s.Kind is not (EpisodeSection.Headline or EpisodeSection.Story) || s.EventId == 0
                || !log.TryGet(s.EventId, out var ev) || !FaceOffs.Fights.Contains(ev.Type)) return s;
            var root = Fights.RootOf(log, ev);
            var latest = events.LastOrDefault(e => FaceOffs.Fights.Contains(e.Type) && Fights.RootOf(log, e) == root) ?? ev;
            return FaceOffs.OfFight(world, log, latest) is { } f ? s with { FaceOff = f } : s;
        }
        // A fight's section carries its days; a few are read aloud (never one its text already gives).
        EpisodeSection WithBeats(EpisodeSection s)
        {
            if (s.Kind is not (EpisodeSection.Headline or EpisodeSection.Story) || s.EventId == 0 || !log.TryGet(s.EventId, out var ev)
                || Fights.Beats(log, ev) is not { Count: > 1 } beats) return s;
            var fresh = beats.Where(b => !s.Text.Contains(b.Text, StringComparison.Ordinal)).ToList();
            return s with { Text = Fights.Tell(fresh) is { Length: > 0 } told ? $"{s.Text}\n\n{told}" : s.Text, Beats = beats };
        }
        var prayers = PrayersOf(events, narrator);
        IReadOnlyList<EpisodeSection> Write(IReadOnlyList<EpisodeStory> told, string? view) =>
            WithMoves(Compose(name, coldOpen, headline, body, challenges, told, lines, titles, view, signOff, prayers));
        var keep = others.ToList();
        var sections = Write(keep, comment);
        while (ScriptOf(sections).Length > maxCharacters && keep.Count > 0)
        {
            var weakest = keep.OrderBy(s => chosen.FindIndex(e => e.Id == s.EventId)).Last();
            keep.Remove(weakest);
            sections = Write(keep, comment);
        }
        if (ScriptOf(sections).Length > maxCharacters && comment is not null) sections = Write(keep, null);

        sections = Stills.Mark(world, log, sections);
        return new Episode(date, name, chronicler.Name, fromYear, toYear, events.Count, events.Count > 0 ? events[0].Id : 0,
            events.Count > 0 ? events[^1].Id : sinceEventId, coldOpen, headline, body, keep, why, signOff, ScriptOf(sections),
            Sections: sections);
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

    /// <summary>A story's chapter title: its kicker and where it happened ("Duel at Kesburg").</summary>
    private static string Title(TemplateNarrator narrator, WorldEvent ev) =>
        narrator.PlaceOf(ev) is { } place ? $"{Views.Categories.Kicker(ev)} at {English.Capitalise(place)}" : Views.Categories.Kicker(ev);

    private static IReadOnlyList<EpisodeSection> Compose(string world, string coldOpen, EpisodeStory? headline, string body,
        IReadOnlyList<EpisodeSection> challenges, IReadOnlyList<EpisodeStory> stories, IReadOnlyDictionary<long, string> lines, IReadOnlyDictionary<long, string> titles, string? comment, string signOff,
        EpisodeSection? prayers = null)
    {
        var sections = new List<EpisodeSection> { new(EpisodeSection.Opening, world, coldOpen) };
        if (headline is not null)
            sections.Add(new(EpisodeSection.Headline, headline.Text, body.Length > 0 ? $"{headline.Text}.\n\n{body}" : $"{headline.Text}.", headline.EventId));
        // The challenges follow the headline: what is coming, before the news of what was.
        sections.AddRange(challenges);
        for (var i = 0; i < stories.Count; i++)
        {
            var line = lines[stories[i].EventId];
            sections.Add(new(EpisodeSection.Story, titles[stories[i].EventId], i == 0 ? $"Elsewhere in the chronicle:\n\n{line}" : line, stories[i].EventId));
        }
        if (prayers is not null) sections.Add(prayers);
        if (comment is not null) sections.Add(new(EpisodeSection.Comment, "The chronicler's view", comment));
        sections.Add(new(EpisodeSection.Closing, "Until tomorrow", signOff));
        return sections;
    }

    /// <summary>
    /// The prayers' section (sub-project 13 N5): how the gods answered since the last episode, then the new prayers, and
    /// the call to the players to answer them. Null when no one prayed and nothing was answered.
    /// </summary>
    private static EpisodeSection? PrayersOf(IReadOnlyList<WorldEvent> events, TemplateNarrator narrator)
    {
        var answers = events.Where(e => e.Type is "PrayerAnswered" or "PrayerUnheeded" or "GodsBlessed").Select(narrator.Sentence).ToList();
        var offered = events.Where(e => e.Type == "PrayerOffered").OrderByDescending(e => e.Importance).ThenBy(e => e.Id)
            .Take(EpisodeSection.PrayersTold).Select(narrator.Sentence).ToList();
        if (answers.Count == 0 && offered.Count == 0) return null;
        var parts = new List<string>();
        if (answers.Count > 0) parts.Add("The gods have answered. " + string.Join(" ", answers));
        if (offered.Count > 0)
            parts.Add("New prayers rise to the gods. " + string.Join(" ", offered)
                      + " You are their gods: grant or deny them, and bless a side in the fights to come, on the chronicle's map before the next dawn.");
        return new EpisodeSection(EpisodeSection.Prayers, "Prayers to the gods", string.Join("\n\n", parts));
    }
}

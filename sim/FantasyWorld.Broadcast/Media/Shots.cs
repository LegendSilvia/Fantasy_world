using System.Text.Json;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Broadcast.Media;

/// <summary>Where the camera looks during a section: a point on the map (0–1 across and down) and how close (1 = the whole map).</summary>
public sealed record Shot(double X, double Y, double Zoom)
{
    public static readonly Shot Whole = new(0.5, 0.5, 1);

    /// <summary>Close enough to see a region and its neighbours on a medium world.</summary>
    public const double StoryZoom = 2.5;
}

/// <summary>A shot from a moment of the narration on.</summary>
public sealed record TimedShot(double Start, Shot Shot);

/// <summary>
/// The episode's shot list (sub-project 7 §6): one shot per section, made from the world when the episode is built (the
/// opening, the chain of causes and the closing see the whole map; a story looks at its event's place), kept beside the
/// episode (<c>episodes/&lt;date&gt;/shots.json</c>) and timed by the chapters when the video is made.
/// </summary>
public static class Shots
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string Key(string date) => $"episodes/{date}/shots.json";

    public static IReadOnlyList<Shot> For(World world, EventLog log, Episode episode) =>
        episode.Parts.Select(section => section.Kind is EpisodeSection.Headline or EpisodeSection.Story && log.TryGet(section.EventId, out var ev)
                                        && PlaceOf(world, ev) is { } at
            ? new Shot(at.X, at.Y, Shot.StoryZoom)
            : Shot.Whole).ToList();

    /// <summary>The event's place, else where its first actor with a place is.</summary>
    private static MapPoint? PlaceOf(World world, WorldEvent ev) =>
        (ev.Place is { } place ? MapPoint.Of(world, place) : null) ?? ev.Actors.Values.Select(a => MapPoint.Of(world, a)).FirstOrDefault(p => p is not null);

    public static Task SaveAsync(IObjectStore store, string date, IReadOnlyList<Shot> shots, CancellationToken ct = default) =>
        store.PutAsync(Key(date), JsonSerializer.SerializeToUtf8Bytes(shots, Json), "application/json", ct);

    public static async Task<IReadOnlyList<Shot>> LoadAsync(IObjectStore store, string date, CancellationToken ct = default)
    {
        if (await store.GetAsync(Key(date), ct).ConfigureAwait(false) is not { } bytes) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Shot>>(bytes, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Each section's shot from its chapter's start; none unless there is one shot per chapter.</summary>
    public static IReadOnlyList<TimedShot> Timed(IReadOnlyList<Shot> shots, Timeline timeline) =>
        shots.Count > 0 && shots.Count == timeline.Chapters.Count
            ? shots.Select((shot, i) => new TimedShot(timeline.Chapters[i].Start, shot)).ToList()
            : [];
}

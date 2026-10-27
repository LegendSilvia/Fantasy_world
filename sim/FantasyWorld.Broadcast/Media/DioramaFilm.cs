using System.Text.Json;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast.Media;

/// <summary>One stretch of the film, filmed on its own machine: seconds from and to.</summary>
public sealed record FilmChunk(int Index, double From, double To);

/// <summary>What the film jobs need: the day, the store's prefix (live or staging) and the stretches to film.</summary>
public sealed record FilmPlan(string Date, string Prefix, IReadOnlyList<FilmChunk> Chunks);

/// <summary>A shot as the diorama film reads it: from a moment on, a point on the map (0–1) and how close.</summary>
public sealed record FilmShot(double Start, double X, double Y, double Zoom);

/// <summary>
/// The episode film on the diorama (sub-project 7 F2). The daily run plans it once the narration's chapters exist: the
/// timed shots (<c>episodes/&lt;date&gt;/film/shots.json</c>) and the stretches, each a section or a part of one, at most
/// <see cref="MaxChunkSeconds"/> long, so each can be filmed on its own machine at the same time (the game's
/// <c>film/Film.tscn</c>) and kept as <c>film/chunk-NN.mp4</c> (video only, all made alike). The YouTube channel joins
/// the stretches with the narration when every one is there; otherwise the pan film over the map is made instead.
/// </summary>
public static class DioramaFilm
{
    /// <summary>About eight minutes of filming on a GitHub runner at ~1 s a frame and 24 frames a second.</summary>
    public const double MaxChunkSeconds = 20;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string ShotsKey(string date) => $"episodes/{date}/film/shots.json";

    public static string ChunkKey(string date, int index) => $"episodes/{date}/film/chunk-{index:00}.mp4";

    /// <summary>The stretches: each chapter, cut into equal parts of at most <see cref="MaxChunkSeconds"/>, ending with the narration.</summary>
    public static IReadOnlyList<FilmChunk> Chunks(Timeline timeline)
    {
        var chunks = new List<FilmChunk>();
        for (var i = 0; i < timeline.Chapters.Count; i++)
        {
            double from = timeline.Chapters[i].Start, to = i + 1 < timeline.Chapters.Count ? timeline.Chapters[i + 1].Start : timeline.Length;
            if (to <= from) continue;
            var parts = (int)Math.Ceiling((to - from) / MaxChunkSeconds);
            for (var p = 0; p < parts; p++)
                chunks.Add(new FilmChunk(chunks.Count, from + (to - from) * p / parts, from + (to - from) * (p + 1) / parts));
        }
        return chunks;
    }

    /// <summary>The shots as the film reads them, each from its chapter's start.</summary>
    public static IReadOnlyList<FilmShot> Timed(IReadOnlyList<Shot> shots, Timeline timeline) =>
        Shots.Timed(shots, timeline).Select(s => new FilmShot(s.Start, s.Shot.X, s.Shot.Y, s.Shot.Zoom)).ToList();

    public static byte[] ToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    public static T? FromBytes<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json);

    /// <summary>The day's stretches, if every one of the plan was filmed and stored; otherwise null.</summary>
    public static async Task<IReadOnlyList<byte[]>?> ChunksAsync(IObjectStore store, string date, Timeline timeline, CancellationToken ct = default)
    {
        var plan = Chunks(timeline);
        if (plan.Count == 0) return null;
        var films = new List<byte[]>();
        foreach (var chunk in plan)
        {
            if (await store.GetAsync(ChunkKey(date, chunk.Index), ct).ConfigureAwait(false) is not { } bytes) return null;
            films.Add(bytes);
        }
        return films;
    }
}

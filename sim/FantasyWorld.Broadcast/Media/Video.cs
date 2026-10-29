using System.Diagnostics;
using System.Globalization;

namespace FantasyWorld.Broadcast.Media;

/// <summary>Makes the episode's video from its map and narration. Throws <see cref="VideoException"/> when it cannot.</summary>
public interface IVideoMaker
{
    /// <summary>The video; with <paramref name="shots"/>, the camera moves over the map from shot to shot (sub-project 7 F1).</summary>
    Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, IReadOnlyList<TimedShot>? shots = null, CancellationToken ct = default);

    /// <summary>The diorama film (sub-project 7 F2): its filmed stretches, in order, joined with the narration.</summary>
    Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, CancellationToken ct = default);

    /// <summary>The film joined and cut at <paramref name="seconds"/> (a short; null for the whole). A maker that cannot cut
    /// joins the whole.</summary>
    Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, double? seconds, CancellationToken ct = default) =>
        JoinAsync(chunks, narrationMp3, ct);

    /// <summary>The film joined with <paramref name="subtitles"/> (SRT, timed to the narration) burned in; a maker that cannot, joins without.</summary>
    Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, double? seconds, string? subtitles, bool tall, CancellationToken ct = default) =>
        JoinAsync(chunks, narrationMp3, seconds, ct);

    /// <summary>
    /// The video's thumbnail (the owner, 2026-10-29): its frame at <paramref name="seconds"/> as a 1280×720 JPEG, with
    /// <paramref name="caption"/> across its foot; null from a maker that cannot.
    /// </summary>
    Task<byte[]?> ThumbnailAsync(byte[] mp4, double seconds, string caption, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
}

public sealed class VideoException(string message) : Exception(message);

/// <summary>
/// The video (M4): the map held still for the length of the narration, as an MP4 that YouTube accepts (H.264 at
/// 1080×1080, a few frames a second since nothing moves; AAC audio; the index at the front for streaming), made by ffmpeg.
/// </summary>
public sealed class FfmpegVideo : IVideoMaker
{
    public const int DefaultSize = 1080;

    private readonly string _ffmpeg;
    private readonly int _size;

    /// <param name="size">The video's side in pixels; even, as H.264 in 4:2:0 needs.</param>
    public FfmpegVideo(string ffmpeg = "ffmpeg", int size = DefaultSize)
    {
        if (size < 16 || size % 2 != 0) throw new ArgumentOutOfRangeException(nameof(size), "The video's side must be even and at least 16 pixels.");
        (_ffmpeg, _size) = (ffmpeg, size);
    }

    /// <summary>
    /// The arguments for <c>ffmpeg</c>: a looped still image (fitted into the square, never stretched) and the audio, cut
    /// to the audio's length.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string image, string audio, string output, int size) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-loop", "1", "-framerate", "2", "-i", image,
        "-i", audio,
        "-vf", $"scale={size}:{size}:force_original_aspect_ratio=decrease:flags=lanczos,pad={size}:{size}:(ow-iw)/2:(oh-ih)/2:color=0xecebe7,format=yuv420p",
        "-c:v", "libx264", "-tune", "stillimage", "-preset", "veryfast", "-r", "6",
        "-c:a", "aac", "-b:a", "160k",
        "-shortest", "-movflags", "+faststart", output,
    ];

    /// <summary>The pan film's frame (16:9, as the diorama film will be) and rate.</summary>
    public const int FilmWidth = 1920, FilmHeight = 1080, FilmFps = 24;

    /// <summary>A move from one shot to the next takes this long, eased in and out.</summary>
    public const double MoveSeconds = 2.5;

    /// <summary>
    /// The arguments for the pan film (sub-project 7 F1): the map fitted into a 16:9 canvas at twice the film's size (so a
    /// close shot stays sharp), and ffmpeg's zoompan following the shots, each move eased over <see cref="MoveSeconds"/>
    /// from its chapter's start, at <see cref="FilmFps"/> frames a second.
    /// </summary>
    public static IReadOnlyList<string> FilmArguments(string image, string audio, string output, (int Width, int Height) map, IReadOnlyList<TimedShot> shots)
    {
        const int canvasW = FilmWidth * 2, canvasH = FilmHeight * 2;
        var fit = Math.Min(canvasW / (double)map.Width, canvasH / (double)map.Height);
        var (fw, fh) = (map.Width * fit, map.Height * fit);
        var (ox, oy) = ((canvasW - fw) / 2, (canvasH - fh) / 2);
        string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        var time = $"(on/{FilmFps})";
        // A value that holds each shot's, easing from the previous shot's over MoveSeconds from the shot's start.
        string Follow(Func<Shot, double> of)
        {
            var expression = N(of(shots[0].Shot));
            for (var i = 1; i < shots.Count; i++)
            {
                var (from, to, start) = (of(shots[i - 1].Shot), of(shots[i].Shot), shots[i].Start);
                var u = $"clip(({time}-{N(start)})/{N(MoveSeconds)},0,1)";
                expression = $"if(lt({time},{N(start)}),{expression},{N(from)}+({N(to - from)})*{u}*{u}*(3-2*{u}))";
            }
            return expression;
        }
        // The view's corner along one axis: a view wider than the map is centred on it; a narrower one follows the shot but
        // stays on the map, never showing the canvas around it.
        string Frame(string centre, string size, double offset, double extent)
        {
            var view = $"{size}/zoom";
            return $"if(gte({view},{N(extent)}),clip({N(offset + extent / 2)}-{view}/2,0,{size}-{view}),"
                   + $"clip({centre}-{view}/2,{N(offset)},{N(offset + extent)}-{view}))";
        }
        var zoom = Follow(s => s.Zoom);
        var cx = Follow(s => ox + s.X * fw);
        var cy = Follow(s => oy + s.Y * fh);
        var filter = $"scale={N(fw)}:{N(fh)}:flags=lanczos,pad={canvasW}:{canvasH}:{N(ox)}:{N(oy)}:color=0xecebe7,"
                     + $"zoompan=z='{zoom}':x='{Frame(cx, "iw", ox, fw)}':y='{Frame(cy, "ih", oy, fh)}'"
                     + $":d=1:s={FilmWidth}x{FilmHeight}:fps={FilmFps},format=yuv420p";
        return
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-loop", "1", "-framerate", FilmFps.ToString(CultureInfo.InvariantCulture), "-i", image,
            "-i", audio,
            "-vf", filter,
            "-c:v", "libx264", "-preset", "veryfast", "-r", FilmFps.ToString(CultureInfo.InvariantCulture),
            "-c:a", "aac", "-b:a", "160k",
            "-shortest", "-movflags", "+faststart", output,
        ];
    }

    public async Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, IReadOnlyList<TimedShot>? shots = null, CancellationToken ct = default) =>
        await RunAsync(async dir =>
        {
            var (image, audio, output) = (Path.Combine(dir, "map.png"), Path.Combine(dir, "narration.mp3"), Path.Combine(dir, "episode.mp4"));
            await File.WriteAllBytesAsync(image, mapPng, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(audio, narrationMp3, ct).ConfigureAwait(false);
            return (shots is { Count: > 0 } ? FilmArguments(image, audio, output, Png.Size(mapPng), shots) : Arguments(image, audio, output, _size), output);
        }, ct).ConfigureAwait(false);

    /// <summary>
    /// The arguments to join the stretches (listed in <paramref name="list"/> for ffmpeg's concat demuxer; all encoded
    /// alike, so the video is copied, not encoded again) with the narration, cut to the narration's length.
    /// </summary>
    public static IReadOnlyList<string> JoinArguments(string list, string audio, string output, double? seconds = null, string? subtitles = null,
        bool tall = false) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-f", "concat", "-safe", "0", "-i", list,
        "-i", audio,
        "-map", "0:v:0", "-map", "1:a:0",
        // With subtitles the picture is drawn on, so encoded again; without, copied as filmed.
        .. subtitles is null ? new[] { "-c:v", "copy" }
            : new[] { "-vf", $"subtitles={subtitles}:force_style='{SubtitleStyle(tall)}'", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p" },
        "-c:a", "aac", "-b:a", "160k",
        .. seconds is { } cut ? new[] { "-t", cut.ToString("0.###", CultureInfo.InvariantCulture) } : Array.Empty<string>(),
        "-shortest", "-movflags", "+faststart", output,
    ];

    /// <summary>
    /// How the subtitles look: white bold text with a dark outline, low in the frame (higher on the tall film, above the
    /// Shorts player's own buttons). Sizes are in libass's units of a 288-high frame, scaled to the video.
    /// </summary>
    public static string SubtitleStyle(bool tall) => tall
        ? "FontName=DejaVu Sans,Bold=1,FontSize=9,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=1,Shadow=0,Alignment=2,MarginV=48"
        : "FontName=DejaVu Sans,Bold=1,FontSize=13,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=1.2,Shadow=0,Alignment=2,MarginV=14";

    public Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, CancellationToken ct = default) => JoinAsync(chunks, narrationMp3, null, ct);

    public Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, double? seconds, CancellationToken ct = default) =>
        JoinAsync(chunks, narrationMp3, seconds, null, false, ct);

    public async Task<byte[]> JoinAsync(IReadOnlyList<byte[]> chunks, byte[] narrationMp3, double? seconds, string? subtitles, bool tall,
        CancellationToken ct = default) =>
        await RunAsync(async dir =>
        {
            var lines = new List<string>();
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = Path.Combine(dir, $"chunk-{i:00}.mp4");
                await File.WriteAllBytesAsync(chunk, chunks[i], ct).ConfigureAwait(false);
                lines.Add($"file '{chunk}'");
            }
            var (list, audio, output) = (Path.Combine(dir, "chunks.txt"), Path.Combine(dir, "narration.mp3"), Path.Combine(dir, "episode.mp4"));
            await File.WriteAllLinesAsync(list, lines, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(audio, narrationMp3, ct).ConfigureAwait(false);
            string? srt = null;
            if (subtitles is { Length: > 0 })
            {
                // A plain name in the scratch folder: the filter's own syntax would trip on a path's colons or quotes.
                srt = "subtitles.srt";
                await File.WriteAllTextAsync(Path.Combine(dir, srt), subtitles, ct).ConfigureAwait(false);
            }
            return (JoinArguments(list, audio, output, seconds, srt, tall), output);
        }, ct).ConfigureAwait(false);

    public const int ThumbnailWidth = 1280, ThumbnailHeight = 720;

    /// <summary>The bold font the caption is drawn in, where the runner has it; without it, the frame alone.</summary>
    public static readonly string[] CaptionFonts =
        ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf"];

    /// <summary>
    /// The arguments for the thumbnail: one frame at <paramref name="seconds"/>, fitted into 1280×720, and the caption (read
    /// from <paramref name="captionFile"/>, so nothing in it needs escaping) on a dark band at its foot when a font is given.
    /// </summary>
    public static IReadOnlyList<string> ThumbnailArguments(string video, double seconds, string output, string? captionFile, string? font)
    {
        var fit = $"scale={ThumbnailWidth}:{ThumbnailHeight}:force_original_aspect_ratio=decrease,"
                  + $"pad={ThumbnailWidth}:{ThumbnailHeight}:(ow-iw)/2:(oh-ih)/2";
        var caption = captionFile is null || font is null ? ""
            : $",drawbox=x=0:y=ih-170:w=iw:h=170:color=black@0.6:t=fill,drawtext=fontfile={font}:textfile={captionFile}"
              + ":fontcolor=white:fontsize=52:line_spacing=8:x=(w-text_w)/2:y=h-140";
        return ["-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", video, "-frames:v", "1",
            "-vf", fit + caption, "-q:v", "3", output];
    }

    public async Task<byte[]?> ThumbnailAsync(byte[] mp4, double seconds, string caption, CancellationToken ct = default)
    {
        async Task<byte[]> Make(bool withCaption) => await RunAsync(async dir =>
        {
            var (video, text, output) = (Path.Combine(dir, "video.mp4"), Path.Combine(dir, "caption.txt"), Path.Combine(dir, "thumbnail.jpg"));
            await File.WriteAllBytesAsync(video, mp4, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(text, caption, ct).ConfigureAwait(false);
            var font = withCaption ? CaptionFonts.FirstOrDefault(File.Exists) : null;
            return (ThumbnailArguments(video, seconds, output, withCaption ? text : null, font), output);
        }, ct).ConfigureAwait(false);
        try
        {
            return await Make(withCaption: true).ConfigureAwait(false);
        }
        catch (VideoException)
        {
            return await Make(withCaption: false).ConfigureAwait(false); // an ffmpeg without drawtext: the frame alone
        }
    }

    /// <summary>Runs ffmpeg in a scratch folder the <paramref name="prepare"/> step fills, and returns the file it made.</summary>
    private async Task<byte[]> RunAsync(Func<string, Task<(IReadOnlyList<string> Arguments, string Output)>> prepare, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("fw-video-");
        try
        {
            var (arguments, output) = await prepare(work.FullName).ConfigureAwait(false);
            // Run in the scratch folder, so files named plainly there (the subtitles) are found.
            var start = new ProcessStartInfo(_ffmpeg) { RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = work.FullName };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new VideoException("ffmpeg could not be started.");
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new VideoException($"ffmpeg could not be started; is it installed? ({e.Message})");
            }
            using (process)
            {
                var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
                try
                {
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                if (process.ExitCode != 0 || !File.Exists(output))
                    throw new VideoException($"ffmpeg failed (exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}): {(await errors.ConfigureAwait(false)).Trim()}");
            }
            return await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}

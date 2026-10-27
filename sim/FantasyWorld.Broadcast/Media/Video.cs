using System.Diagnostics;
using System.Globalization;

namespace FantasyWorld.Broadcast.Media;

/// <summary>Makes the episode's video from its map and narration. Throws <see cref="VideoException"/> when it cannot.</summary>
public interface IVideoMaker
{
    /// <summary>The video; with <paramref name="shots"/>, the camera moves over the map from shot to shot (sub-project 7 F1).</summary>
    Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, IReadOnlyList<TimedShot>? shots = null, CancellationToken ct = default);
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
    public const int FilmWidth = 1280, FilmHeight = 720, FilmFps = 24;

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

    public async Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, IReadOnlyList<TimedShot>? shots = null, CancellationToken ct = default)
    {
        var work = Directory.CreateTempSubdirectory("fw-video-");
        try
        {
            var (image, audio, output) = (Path.Combine(work.FullName, "map.png"), Path.Combine(work.FullName, "narration.mp3"),
                Path.Combine(work.FullName, "episode.mp4"));
            await File.WriteAllBytesAsync(image, mapPng, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(audio, narrationMp3, ct).ConfigureAwait(false);
            var start = new ProcessStartInfo(_ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
            var arguments = shots is { Count: > 0 } ? FilmArguments(image, audio, output, Png.Size(mapPng), shots) : Arguments(image, audio, output, _size);
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

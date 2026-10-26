using System.Diagnostics;
using System.Globalization;

namespace FantasyWorld.Broadcast.Media;

/// <summary>Makes the episode's video from its map and narration. Throws <see cref="VideoException"/> when it cannot.</summary>
public interface IVideoMaker
{
    Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, CancellationToken ct = default);
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

    public async Task<byte[]> MakeAsync(byte[] mapPng, byte[] narrationMp3, CancellationToken ct = default)
    {
        var work = Directory.CreateTempSubdirectory("fw-video-");
        try
        {
            var (image, audio, output) = (Path.Combine(work.FullName, "map.png"), Path.Combine(work.FullName, "narration.mp3"),
                Path.Combine(work.FullName, "episode.mp4"));
            await File.WriteAllBytesAsync(image, mapPng, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(audio, narrationMp3, ct).ConfigureAwait(false);
            var start = new ProcessStartInfo(_ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in Arguments(image, audio, output, _size)) start.ArgumentList.Add(argument);
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

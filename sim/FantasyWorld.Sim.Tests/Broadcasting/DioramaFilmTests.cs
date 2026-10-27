using System.Diagnostics;
using FantasyWorld.Broadcast;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class DioramaFilmTests
{
    [Fact]
    public void TheFilm_IsCutIntoStretchesOfAtMostTwentySeconds_AlongTheSections()
    {
        var timeline = new Timeline(75, [new("Opening", 0), new("Headline", 6), new("Until tomorrow", 70)]);
        var chunks = DioramaFilm.Chunks(timeline);
        // The 64-second headline is cut in four equal parts; the opening and the closing are one each.
        Assert.Equal(new[] { (0d, 6d), (6d, 22d), (22d, 38d), (38d, 54d), (54d, 70d), (70d, 75d) }, chunks.Select(c => (c.From, c.To)));
        Assert.Equal(Enumerable.Range(0, 6), chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.True(c.To - c.From <= DioramaFilm.MaxChunkSeconds));
        Assert.Empty(DioramaFilm.Chunks(Timeline.None));
        Assert.Equal("episodes/2026-10-27/film/chunk-04.mp4", DioramaFilm.ChunkKey("2026-10-27", 4));

        var shots = DioramaFilm.Timed([Shot.Whole, new Shot(0.2, 0.7, Shot.StoryZoom), Shot.Whole], timeline);
        Assert.Equal(new FilmShot(6, 0.2, 0.7, Shot.StoryZoom), shots[1]);
        // As the game's film reads them.
        Assert.Contains("\"start\": 6", System.Text.Encoding.UTF8.GetString(DioramaFilm.ToBytes(shots)));
    }

    [Fact]
    public async Task FilmedStretches_AreJoinedWithTheNarration()
    {
        if (!VideoTests.Has("ffmpeg") || !VideoTests.Has("ffprobe")) return;
        using var dir = new TempDir();
        string Make(string args, string file)
        {
            var path = Path.Combine(dir.Path, file);
            using var make = Process.Start("ffmpeg", $"-hide_banner -loglevel error -y {args} {path}")!;
            make.WaitForExit();
            return path;
        }
        // Stretches made as the film jobs make them (H.264, 24 fps, 4:2:0), and two seconds of narration.
        var chunks = new[] { "red", "blue" }.Select(c => File.ReadAllBytes(Make(
            $"-f lavfi -i color={c}:s=320x180:r=24 -t 1 -c:v libx264 -preset veryfast -pix_fmt yuv420p -r 24", $"{c}.mp4"))).ToList();
        var narration = File.ReadAllBytes(Make("-f lavfi -i anullsrc=r=44100:cl=mono -t 2 -b:a 128k", "n.mp3"));
        var film = await new FfmpegVideo().JoinAsync(chunks, narration);
        var output = Path.Combine(dir.Path, "film.mp4");
        await File.WriteAllBytesAsync(output, film);
        using var probe = Process.Start(new ProcessStartInfo("ffprobe", $"-v error -show_entries stream=codec_type -of csv=p=0 {output}") { RedirectStandardOutput = true })!;
        Assert.Equal("video\naudio", probe.StandardOutput.ReadToEnd().Trim().Replace("\r", ""));
    }

    [Fact]
    public async Task TheFilmJobs_CopyFilesInAndOutOfTheBucket()
    {
        using var dir = new TempDir();
        var bucket = Path.Combine(dir.Path, "bucket");
        var file = Path.Combine(dir.Path, "chunk.mp4");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        var output = new StringWriter();
        Assert.Equal(DailyRun.Ok, await BroadcastApp.RunAsync(["put", file, "staging/episodes/2026-10-27/film/chunk-00.mp4", "--local", bucket],
            output, () => DateTimeOffset.UnixEpoch, _ => null));
        var back = Path.Combine(dir.Path, "back.mp4");
        Assert.Equal(DailyRun.Ok, await BroadcastApp.RunAsync(["get", "staging/episodes/2026-10-27/film/chunk-00.mp4", back, "--local", bucket],
            output, () => DateTimeOffset.UnixEpoch, _ => null));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(back));
        Assert.Equal(DailyRun.Failed, await BroadcastApp.RunAsync(["get", "nothing/here", back, "--local", bucket],
            output, () => DateTimeOffset.UnixEpoch, _ => null));
        Assert.Contains("nothing/here is not in the bucket", output.ToString());
    }
}

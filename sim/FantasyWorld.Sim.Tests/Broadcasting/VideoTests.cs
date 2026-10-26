using System.Diagnostics;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class VideoTests
{
    private static bool Has(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public void TheVideo_IsAStillMapUnderTheNarration_ThatYouTubeAccepts()
    {
        var args = FfmpegVideo.Arguments("map.png", "n.mp3", "out.mp4", 1080);
        Assert.Equal(new[] { "-loop", "1" }, args.SkipWhile(a => a != "-loop").Take(2));
        Assert.Contains("-shortest", args);
        Assert.Contains("libx264", args);
        Assert.Contains("aac", args);
        Assert.Contains("-nostdin", args);
        Assert.StartsWith("scale=1080:1080:force_original_aspect_ratio=decrease", args.Single(a => a.StartsWith("scale=", StringComparison.Ordinal)));
        Assert.Equal("out.mp4", args[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfmpegVideo(size: 1081)); // 4:2:0 needs even sides
    }

    [Fact]
    public async Task Ffmpeg_MakesAnMp4_OrSaysWhyNot()
    {
        await Assert.ThrowsAsync<VideoException>(() => new FfmpegVideo("no-such-ffmpeg-here").MakeAsync([1], [2]));
        if (!Has("ffmpeg")) return; // the workflow installs it; a machine without it checks only the failure above

        // Two seconds of silence as the narration, and a real map.
        var mp3 = Path.GetTempFileName() + ".mp3";
        using (var make = Process.Start("ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i anullsrc=r=44100:cl=mono -t 2 -b:a 128k {mp3}")) make.WaitForExit();
        var sim = Prehistory.Run(WorldGenerator.Generate(new WorldGenOptions(3, WorldSize.Tiny), DateTimeOffset.UnixEpoch), 5);
        var video = await new FfmpegVideo(size: 240).MakeAsync(MapPainter.Png(sim.World, sim.Log, 256), await File.ReadAllBytesAsync(mp3));
        File.Delete(mp3);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(video, 4, 4));
        await Assert.ThrowsAsync<VideoException>(() => new FfmpegVideo().MakeAsync([1, 2, 3], [4, 5, 6])); // not a picture
    }
}

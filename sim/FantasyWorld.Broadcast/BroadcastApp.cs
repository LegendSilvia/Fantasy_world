using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;

namespace FantasyWorld.Broadcast;

/// <summary>
/// <c>fw-broadcast run [--dry-run] [--create] [--config broadcast.json] [--local &lt;dir&gt;]</c>. Secrets come only
/// from the environment: R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, DISCORD_WEBHOOK_URL and, for
/// dry runs, DISCORD_TEST_WEBHOOK_URL. <c>--local</c> uses a directory instead of R2.
/// </summary>
public static class BroadcastApp
{
    public const string Usage = """
        usage: fw-broadcast run [--dry-run [--upload-private]] [--create] [--prepare <plan.json>] [--config <broadcast.json>] [--local <dir>]
               fw-broadcast map <world-file> <out.png>   the political map as the broadcast posts it
               fw-broadcast get <key> <file> [--local <dir>]   copy an object of the bucket to a file (the film jobs)
               fw-broadcast put <file> <key> [--local <dir>]   store a file in the bucket (the film jobs)
          --dry-run   every step, under the staging prefix, posting to DISCORD_TEST_WEBHOOK_URL (if set)
          --upload-private   with --dry-run: upload the staging video to YouTube as private, to try the upload
          --create    begin a new world when none is stored (never done by a scheduled run)
          --local     keep the world in a local directory instead of R2
          --prepare   build and narrate the episode and plan its diorama film (written to the file), posting nothing
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, Func<DateTimeOffset> clock,
        Func<string, string?> env, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (args is ["map", var worldPath, var pngPath])
            return Map(worldPath, pngPath, output);
        if (args is ["get" or "put", var first, var second, ..] && args.Skip(3).All(a => a is "--local" || a == Option(args, "--local")))
            return await CopyAsync(args[0] == "get", first, second, Option(args, "--local"), env, handler, output, ct).ConfigureAwait(false);
        if (args is not ["run", ..] || args.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                                             && a is not ("--dry-run" or "--upload-private" or "--create" or "--config" or "--local" or "--prepare"))
                                     || args.Contains("--upload-private") && !args.Contains("--dry-run"))
        {
            output.WriteLine(Usage);
            return DailyRun.Failed;
        }
        var options = new RunOptions(args.Contains("--dry-run"), args.Contains("--create"), args.Contains("--upload-private"), Option(args, "--prepare"));

        BroadcastConfig config;
        try
        {
            config = BroadcastConfig.Load(Option(args, "--config") ?? "broadcast.json");
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or TimeZoneNotFoundException)
        {
            output.WriteLine($"error: broadcast.json: {e.Message}");
            return DailyRun.Failed;
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);

        IObjectStore? store = Option(args, "--local") is { } dir ? new LocalObjectStore(dir) : R2ObjectStore.FromEnvironment(http, env);
        if (store is null)
        {
            output.WriteLine("error: R2 is not configured. Set R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY.");
            return DailyRun.Failed;
        }

        var webhookName = options.DryRun ? "DISCORD_TEST_WEBHOOK_URL" : "DISCORD_WEBHOOK_URL";
        var youtube = YouTubeChannel(config, options, env, http, output);
        // YouTube goes first, so the Discord post can carry the day's video link.
        var channels = new List<IEpisodeChannel>();
        if (youtube is not null) channels.Add(youtube);
        if (env(webhookName) is { Length: > 0 } webhook)
            channels.Add(new DiscordWebhook(http, webhook, config.DiscordName, episode => config.Site.LinkFor(episode, options.DryRun)));
        else if (!options.DryRun)
        {
            output.WriteLine($"error: {webhookName} is not set, so the episode could not be posted.");
            return DailyRun.Failed;
        }

        // The LLM key, like every secret, comes from the environment given (never the process's own, so tests cannot spend).
        return await new DailyRun(config, store, channels, clock, output, options,
            settings => Llm.LlmProviders.Create(settings, env, ScriptTimeout), Speech.ElevenLabs.FromEnvironment(http, env)).RunAsync(ct).ConfigureAwait(false);
    }

    private static int Map(string worldPath, string pngPath, TextWriter output)
    {
        if (!File.Exists(worldPath))
        {
            output.WriteLine($"error: {worldPath} does not exist.");
            return DailyRun.Failed;
        }
        using var store = Sim.Persist.WorldStore.Open(worldPath);
        var (world, log, _) = store.Load();
        File.WriteAllBytes(pngPath, Media.MapPainter.Png(world, log));
        output.WriteLine($"Map written to {pngPath}.");
        return DailyRun.Ok;
    }

    /// <summary>
    /// The YouTube channel: the live broadcast uploads with the three YouTube secrets; a dry run (YouTube has no test
    /// channel) only makes and stores the video, so the first real video is not made in production, unless it is asked
    /// to upload it as private (<c>--upload-private</c>), which tries the whole upload where only the owner sees it.
    /// </summary>
    public static Channels.YouTube? YouTubeChannel(BroadcastConfig config, RunOptions options, Func<string, string?> env, HttpClient http, TextWriter output)
    {
        if (!config.YouTube.Upload) return null;
        var uploads = !options.DryRun || options.UploadPrivate;
        var account = uploads ? Channels.YouTubeAccount.FromEnvironment(env) : null;
        if (uploads && account is null)
        {
            output.WriteLine("YouTube: the YouTube secrets are not set, so no video is uploaded.");
            return null;
        }
        if (!config.Voice.Speak) output.WriteLine("warning: YouTube is set up but voice.speak is off, so there is no narration to make a video of.");
        var settings = options.DryRun ? config.YouTube with { Privacy = "private" } : config.YouTube;
        return new Channels.YouTube(http, account, new Media.FfmpegVideo(), settings, episode => config.Site.LinkFor(episode, options.DryRun));
    }

    /// <summary>A whole script with adaptive thinking can take minutes; the run itself has 45.</summary>
    public static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The film jobs' copies (sub-project 7 F2): <c>get &lt;key&gt; &lt;file&gt;</c> or <c>put &lt;file&gt; &lt;key&gt;</c>,
    /// the key in full (with its live/ or staging/ prefix, as the film plan gives it).
    /// </summary>
    private static async Task<int> CopyAsync(bool get, string first, string second, string? local, Func<string, string?> env,
        HttpMessageHandler? handler, TextWriter output, CancellationToken ct)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);
        IObjectStore? store = local is not null ? new LocalObjectStore(local) : R2ObjectStore.FromEnvironment(http, env);
        if (store is null)
        {
            output.WriteLine("error: R2 is not configured. Set R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY.");
            return DailyRun.Failed;
        }
        try
        {
            if (get)
            {
                if (await store.GetAsync(first, ct).ConfigureAwait(false) is not { } bytes)
                {
                    output.WriteLine($"error: {first} is not in the bucket.");
                    return DailyRun.Failed;
                }
                await File.WriteAllBytesAsync(second, bytes, ct).ConfigureAwait(false);
            }
            else
            {
                var type = Path.GetExtension(first) switch { ".mp4" => "video/mp4", ".json" => "application/json", ".png" => "image/png", _ => "application/octet-stream" };
                await store.PutAsync(second, await File.ReadAllBytesAsync(first, ct).ConfigureAwait(false), type, ct).ConfigureAwait(false);
            }
            return DailyRun.Ok;
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            output.WriteLine($"error: {e.Message}");
            return DailyRun.Failed;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

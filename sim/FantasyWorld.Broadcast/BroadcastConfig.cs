using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The broadcast's LLM (spec §3.2, M2): the chronicler writes the script on the narrate tier. Story beats (the sim's
/// own LLM decisions while it catches up: the characters' choices and the fights, sub-project 8) stay off unless given
/// a budget, so the daily cost is one script; of them, <see cref="FightBeatsPerDay"/> are kept for fights.
/// </summary>
public sealed record BroadcastLlm
{
    /// <summary>When off, the template script ships and nothing is asked.</summary>
    public bool Narrate { get; init; } = true;

    public string NarrateModel { get; init; } = "claude-sonnet-5-5";

    public int StoryBeatsPerDay { get; init; }

    public int FightBeatsPerDay { get; init; }

    public int NarrationsPerDay { get; init; } = 10;

    public decimal MonthlyCapUsd { get; init; } = 5m;
}

/// <summary>
/// The narrator's voice (M3): ElevenLabs speaks each script. The per-request limit and the monthly character budget
/// are settings, not assumptions about the model; past the budget the episode goes out as text.
/// </summary>
public sealed record BroadcastVoice
{
    public bool Speak { get; init; } = true;

    /// <summary>"Michael C. Vincent – Confident, Expressive", from the ElevenLabs library: the owner's choice.</summary>
    public string VoiceId { get; init; } = "uju3wxzG5OhpWcoi3SMy";

    public string Model { get; init; } = "eleven_v4_turbo";

    public string OutputFormat { get; init; } = "mp3_44100_128";

    public int MaxCharactersPerRequest { get; init; } = 5000;

    public int MonthlyCharacterBudget { get; init; } = 150_000;

    /// <summary>Ask the chronicler's model, once per invented name, how to say it; the answers are kept for good.</summary>
    public bool Respell { get; init; } = true;
}

/// <summary>
/// The YouTube upload (M4): on when the three YouTube secrets are set. YouTube keeps videos uploaded through a Google
/// app that has not passed its audit private, whatever is asked here.
/// </summary>
public sealed record BroadcastYouTube
{
    public bool Upload { get; init; } = true;

    /// <summary>public, unlisted or private.</summary>
    public string Privacy { get; init; } = "public";

    /// <summary>24 is Entertainment.</summary>
    public string CategoryId { get; init; } = "24";

    /// <summary>
    /// The video is filmed on the game's diorama (sub-project 7 F2) by the workflow's film jobs; when they have not filmed
    /// every stretch, or this is off, the camera pans over the political map instead.
    /// </summary>
    public bool Diorama { get; init; } = true;

    /// <summary>
    /// A vertical short of each episode (the owner, 2026-10-28: a version for YouTube Shorts and TikTok): the film jobs
    /// also film it at 1080×1920, and it goes up as a Short of its own, cut to three minutes at most. The same file is kept
    /// in the bucket (<c>episodes/&lt;date&gt;/short.mp4</c>) for TikTok.
    /// </summary>
    public bool Shorts { get; init; } = true;
}

/// <summary>The bucket's limit (the owner's word, 2026-10-27: 10 GB in all), kept by <see cref="Storage.StorageBudget"/>.</summary>
public sealed record BroadcastStorage
{
    public double MaxGigabytes { get; init; } = 10;
}

/// <summary>
/// The face-offs' images (the owner, 2026-10-27): a duellist's portrait or a realm's banner from the owner's FLUX image
/// API, made once and kept in the bucket for reuse. The access key comes from the environment (<c>FLUX_ACCESS_KEY</c>);
/// without it, no image is made.
/// </summary>
public sealed record BroadcastImages
{
    public bool Make { get; init; } = true;

    public string BaseUrl { get; init; } = "https://flux-playground.tanwa-tharathep.workers.dev";

    public string Style { get; init; } = "hand-painted";
}

/// <summary>
/// The broadcast's checked-in settings (<c>broadcast.json</c>). Nothing secret lives here: keys and webhooks come from
/// the environment (GitHub Actions secrets).
/// </summary>
public sealed record BroadcastConfig
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The world's object key, under the live or staging prefix.</summary>
    public string WorldKey { get; init; } = "world.fw.db";

    /// <summary>The seed and size a <c>create</c> run starts the world with.</summary>
    public ulong Seed { get; init; } = 20261027;

    public string Size { get; init; } = "medium";

    /// <summary>
    /// The years a newly made world lives before its first episode (lived offline, after Claude names its peoples);
    /// unset, as `fw new` would (a century, none for a tiny world).
    /// </summary>
    public int? PrehistoryYears { get; init; }

    /// <summary>Episodes are keyed by the broadcast date in this time zone.</summary>
    public string TimeZone { get; init; } = "Asia/Bangkok";

    public string Language { get; init; } = "en";

    public string LivePrefix { get; init; } = "live/";

    public string StagingPrefix { get; init; } = "staging/";

    /// <summary>Daily backups kept; the first backup of each month is kept as well, for good.</summary>
    public int BackupDays { get; init; } = 30;

    public int Stories { get; init; } = Episode.DefaultStories;

    public int MaxCharacters { get; init; } = Episode.DefaultMaxCharacters;

    /// <summary>The name the Discord post appears under.</summary>
    public string DiscordName { get; init; } = "The Chronicle";

    public BroadcastLlm Llm { get; init; } = new();

    public BroadcastVoice Voice { get; init; } = new();

    public BroadcastYouTube YouTube { get; init; } = new();

    public BroadcastSite Site { get; init; } = new();

    public BroadcastStorage Storage { get; init; } = new();

    public BroadcastImages Images { get; init; } = new();

    /// <summary>
    /// Where the bucket is public (an r2.dev address or a custom domain), for the podcast feed and the archive page; unset,
    /// they are not published.
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>The podcast's contact address (<c>itunes:email</c>), which directories use to confirm who owns the feed.</summary>
    public string? PodcastEmail { get; init; }

    /// <summary>When on, the workflow commits a heartbeat monthly so GitHub keeps the schedule of a public repository alive.</summary>
    public bool KeepAlive { get; init; } = true;

    public static BroadcastConfig Load(string path) => Parse(File.ReadAllText(path));

    public static BroadcastConfig Parse(string json) =>
        (JsonSerializer.Deserialize<BroadcastConfig>(json, Json) ?? throw new FormatException("broadcast.json is empty.")).Validated();

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>The broadcast date (yyyy-MM-dd) at <paramref name="now"/>, in the broadcast's time zone.</summary>
    public string DateAt(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private BroadcastConfig Validated()
    {
        if (Language != "en") throw new FormatException($"Only English broadcasts exist so far (language \"{Language}\").");
        if (Size is not ("tiny" or "medium" or "large" or "huge")) throw new FormatException($"Unknown size \"{Size}\".");
        if (BackupDays < 1) throw new FormatException("backupDays must be at least 1.");
        if (Stories is < 0 or > 10) throw new FormatException("stories must be between 0 and 10.");
        if (MaxCharacters is < 300 or > 4000) throw new FormatException("maxCharacters must be between 300 and 4000 (a Discord post holds 4096).");
        if (LivePrefix == StagingPrefix || !LivePrefix.EndsWith('/') || !StagingPrefix.EndsWith('/'))
            throw new FormatException("livePrefix and stagingPrefix must differ and end with '/'.");
        if (Llm.StoryBeatsPerDay < 0 || Llm.NarrationsPerDay < 0 || Llm.MonthlyCapUsd < 0 || Llm.NarrateModel.Length == 0)
            throw new FormatException("llm budgets and the cap cannot be negative, and narrateModel must be named.");
        if (Llm.FightBeatsPerDay < 0 || Llm.FightBeatsPerDay > Llm.StoryBeatsPerDay)
            throw new FormatException("llm fightBeatsPerDay must be between 0 and storyBeatsPerDay.");
        if (Voice.MaxCharactersPerRequest < 100 || Voice.MonthlyCharacterBudget < 0 || Voice.VoiceId.Length == 0 || Voice.Model.Length == 0)
            throw new FormatException("voice needs a voiceId and a model, at least 100 characters a request and a budget that is not negative.");
        _ = Speech.Voiceover.Kbps(Voice.OutputFormat);
        if (Storage.MaxGigabytes <= 0) throw new FormatException("storage.maxGigabytes must be more than 0.");
        if (Images.Make && !Uri.TryCreate(Images.BaseUrl, UriKind.Absolute, out _)) throw new FormatException("images.baseUrl must be an address.");
        if (YouTube.Privacy is not ("public" or "unlisted" or "private"))
            throw new FormatException($"youTube.privacy must be public, unlisted or private (got \"{YouTube.Privacy}\").");
        if (PublicBaseUrl is { } url && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            throw new FormatException($"publicBaseUrl must be an https address (got \"{url}\").");
        if (Site.Url is { } site && !(Uri.TryCreate(site, UriKind.Absolute, out var siteUri) && siteUri.Scheme == Uri.UriSchemeHttps))
            throw new FormatException($"site.url must be an https address (got \"{site}\").");
        if (Site.Project.Length == 0 || !Site.Project.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new FormatException($"site.project must be lower-case letters, digits and hyphens (got \"{Site.Project}\").");
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The broadcast's LLM (spec §3.2, M2): the chronicler writes the script on the narrate tier. Story beats (the sim's
/// own LLM decisions while it catches up) stay off unless given a budget, so the daily cost is one script.
/// </summary>
public sealed record BroadcastLlm
{
    /// <summary>When off, the template script ships and nothing is asked.</summary>
    public bool Narrate { get; init; } = true;

    public string NarrateModel { get; init; } = "claude-sonnet-5-5";

    public int StoryBeatsPerDay { get; init; }

    public int NarrationsPerDay { get; init; } = 10;

    public decimal MonthlyCapUsd { get; init; } = 5m;
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
        if (Size is not ("tiny" or "medium" or "large")) throw new FormatException($"Unknown size \"{Size}\".");
        if (BackupDays < 1) throw new FormatException("backupDays must be at least 1.");
        if (Stories is < 0 or > 10) throw new FormatException("stories must be between 0 and 10.");
        if (MaxCharacters is < 300 or > 4000) throw new FormatException("maxCharacters must be between 300 and 4000 (a Discord post holds 4096).");
        if (LivePrefix == StagingPrefix || !LivePrefix.EndsWith('/') || !StagingPrefix.EndsWith('/'))
            throw new FormatException("livePrefix and stagingPrefix must differ and end with '/'.");
        if (Llm.StoryBeatsPerDay < 0 || Llm.NarrationsPerDay < 0 || Llm.MonthlyCapUsd < 0 || Llm.NarrateModel.Length == 0)
            throw new FormatException("llm budgets and the cap cannot be negative, and narrateModel must be named.");
        _ = Zone; // throws TimeZoneNotFoundException for an unknown zone
        return this;
    }
}

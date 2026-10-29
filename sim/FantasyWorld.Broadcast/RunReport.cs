using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// What a day's broadcast came to (the owner, 2026-10-29: "a method to guarantee everything"), read back from the bucket
/// after the run: the script and who wrote it, the stills, the film's stretches, the video and the Short with their
/// privacy, the Discord post, and every shortfall named. Written as Markdown for the workflow's summary; each shortfall is
/// also a warning on the run's page.
/// </summary>
public static class RunReport
{
    public sealed record Result(string Markdown, IReadOnlyList<string> Problems);

    public static async Task<Result> BuildAsync(BroadcastConfig config, IObjectStore root, bool dryRun, string date, CancellationToken ct = default)
    {
        var store = new PrefixedStore(root, dryRun ? config.StagingPrefix : config.LivePrefix);
        var problems = new List<string>();
        var md = new StringBuilder();
        md.AppendLine($"## Broadcast of {date}{(dryRun ? " (rehearsal, under staging)" : "")}");
        var manifest = Manifest.FromBytes(await store.GetAsync(Manifest.Key, ct).ConfigureAwait(false));
        var day = manifest.Day(date);
        if (await store.GetAsync(DailyRun.EpisodeKey(date), ct).ConfigureAwait(false) is not { } bytes)
        {
            problems.Add($"no episode was stored for {date}");
            return Finish(md, problems);
        }
        var episode = Episode.FromJson(Encoding.UTF8.GetString(bytes));
        md.AppendLine();
        md.AppendLine("| | |");
        md.AppendLine("|---|---|");
        string Row(string what, string how) => $"| {what} | {how} |";
        var chronicler = episode.Writer == Episode.ChroniclerWriter;
        md.AppendLine(Row("Script", $"{(chronicler ? "written by Claude" : "**the template's** (Claude's was not used)")}, {episode.Script.Length:N0} characters, {episode.Parts.Count} sections"));
        if (!chronicler) problems.Add("the script is the template's, not Claude's");
        var narrated = day.Done(Manifest.AudioStep);
        md.AppendLine(Row("Narration", narrated ? $"spoken, {day.AudioSeconds:0} s" : "**not spoken**"));
        if (!narrated) problems.Add("the episode was not narrated");
        var wantStills = episode.Parts.Count(p => p.Scene is not null);
        var stills = episode.Parts.Count(p => p.Still is not null);
        md.AppendLine(Row("Stills", $"{stills} of {wantStills}"));
        if (config.Images.Make && stills < wantStills) problems.Add($"{wantStills - stills} of the episode's stills were not painted");
        var faceOffs = episode.Parts.Where(p => p.FaceOff is not null).SelectMany(p => new[] { p.FaceOff!.A, p.FaceOff.B }).ToList();
        md.AppendLine(Row("Face-off images", $"{faceOffs.Count(s => s.Image is not null)} of {faceOffs.Count}"));

        var film = await store.ListAsync($"episodes/{date}/film/", ct).ConfigureAwait(false);
        var wide = film.Count(k => Path.GetFileName(k).StartsWith("chunk-", StringComparison.Ordinal));
        var tall = film.Count(k => Path.GetFileName(k).StartsWith("tall-chunk-", StringComparison.Ordinal));
        var planned = film.Any(k => k.EndsWith("/shots.json", StringComparison.Ordinal));
        md.AppendLine(Row("Film", planned ? $"{wide} wide and {tall} tall stretches filmed" : "not planned (the video pans over the map)"));
        if (planned && config.YouTube.Shorts && tall < wide) problems.Add($"the vertical film has {tall} stretches against {wide} wide ones");

        foreach (var (name, key, step) in new[] { ("Video", YouTube.StateKey(date), "youtube"), ("Short", YouTube.ShortStateKey(date), "youtube-short") })
        {
            if (name == "Short" && !config.YouTube.Shorts) continue;
            var state = await store.GetAsync(key, ct).ConfigureAwait(false) is { } s ? JsonSerializer.Deserialize<UploadState>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
            if (state is { VideoId: { Length: > 0 } id })
            {
                md.AppendLine(Row(name, $"https://youtu.be/{id} ({state.Privacy ?? "privacy unknown"})"));
                if (state.Privacy is { } privacy && privacy != config.YouTube.Privacy) problems.Add($"the {name.ToLowerInvariant()} went up {privacy}, not {config.YouTube.Privacy}");
                if (state.Privacy == "public" && !config.YouTube.GoPublic) problems.Add($"the {name.ToLowerInvariant()} is public without the owner's word");
            }
            else
            {
                var made = await store.GetAsync(name == "Video" ? YouTube.VideoKey(date) : YouTube.ShortVideoKey(date), ct).ConfigureAwait(false) is not null;
                md.AppendLine(Row(name, made ? "made and stored, not uploaded" : $"**not made**{(day.Done(step) ? "" : " (waiting)")}"));
                if (!made) problems.Add($"the {name.ToLowerInvariant()} was not made");
            }
        }
        var posted = day.Done(Manifest.DiscordStep);
        md.AppendLine(Row("Discord", posted ? "posted" : "**not posted**"));
        if (!posted) problems.Add("the Discord post was not made");
        return Finish(md, problems);
    }

    private static Result Finish(StringBuilder md, List<string> problems)
    {
        md.AppendLine();
        if (problems.Count == 0) md.AppendLine("Everything was made, uploaded and posted as it should be.");
        else
        {
            md.AppendLine("### What fell short");
            foreach (var p in problems) md.AppendLine($"- {p}");
        }
        return new Result(md.ToString(), problems);
    }
}

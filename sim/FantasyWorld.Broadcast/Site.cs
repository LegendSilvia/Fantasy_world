using FantasyWorld.Sim.Chronicle;

namespace FantasyWorld.Broadcast;

/// <summary>
/// The world viewer's website (sub-project 11 W4): the Cloudflare Pages project the workflow deploys the browser app and the
/// day's world to, and its public address once known. Dry runs deploy to the project's <c>staging</c> branch, so the public
/// site changes only with a live broadcast.
/// </summary>
public sealed record BroadcastSite
{
    public string Project { get; init; } = "fantasy-world-chronicle";

    /// <summary>The site's address (e.g. https://fantasy-world-chronicle.pages.dev); unset, posts carry no link to it.</summary>
    public string? Url { get; init; }

    /// <summary>The branch a run deploys to: <c>main</c> (the public site) or <c>staging</c> (a dry run's preview).</summary>
    public static string BranchFor(bool dryRun) => dryRun ? "staging" : "main";

    /// <summary>
    /// The link a post gives to the episode's world: the site opened on the headline's story, on the staging preview for a
    /// dry run (a pages.dev address's branch alias). Null without an address.
    /// </summary>
    public string? LinkFor(Episode episode, bool dryRun)
    {
        if (Url is null || !Uri.TryCreate(Url, UriKind.Absolute, out var uri)) return null;
        var host = dryRun && uri.Host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase) ? $"{BranchFor(true)}.{uri.Host}" : uri.Host;
        var root = new UriBuilder(uri) { Host = host }.Uri.ToString().TrimEnd('/');
        return episode.Headline is { } headline ? $"{root}/#why/{headline.EventId}" : root;
    }
}

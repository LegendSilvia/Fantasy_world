using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Broadcast.Media;

/// <summary>What an image is asked for: the subject only (the scenario frames it), the scenario, the style, the seed and, if not the scenario's, the size.</summary>
public sealed record ImageRequest(string Prompt, string Scenario, string Style, bool Transparent, long Seed, int? Width = null, int? Height = null);

/// <summary>An image as made: its bytes and type, and whether its background really was taken away.</summary>
public sealed record MadeImage(byte[] Bytes, string Mime, bool Transparent, string? Warning);

/// <summary>The image API's day is spent (a 429): no image is asked for again until its budget resets at 00:00 UTC.</summary>
public sealed class ImageBudgetSpentException(string message) : Exception(message);

public interface IImageMaker
{
    Task<MadeImage> MakeAsync(ImageRequest request, CancellationToken ct = default);
}

/// <summary>
/// The owner's FLUX image API (a Cloudflare Worker): <c>POST {base}/generate</c> with the key in <c>x-access-key</c>. A 429
/// means the day's budget is spent: it is never retried. Errors carry the status code only, never the key.
/// </summary>
public sealed class FluxImages(HttpClient http, string baseUrl, string accessKey) : IImageMaker
{
    public static FluxImages? FromEnvironment(HttpClient http, string baseUrl, Func<string, string?> env) =>
        env("FLUX_ACCESS_KEY") is { Length: > 0 } key ? new FluxImages(http, baseUrl, key) : null;

    /// <summary>
    /// Whether the API takes the key, without making an image: a request for a scenario that does not exist is refused
    /// after the key is checked, so 400 means the key was accepted and 401 that it was not.
    /// </summary>
    public async Task<(bool Accepted, int Status)> CheckAsync(CancellationToken ct = default)
    {
        using var message = Request(new JsonObject { ["prompt"] = "a test", ["scenario"] = "no-such-scenario" });
        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        return (response.StatusCode == HttpStatusCode.BadRequest, (int)response.StatusCode);
    }

    /// <summary>A request to make an image; the key is sent trimmed (a secret pasted with a trailing newline or space is refused as is).</summary>
    private HttpRequestMessage Request(JsonObject body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/generate")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-access-key", accessKey.Trim());
        return message;
    }

    public async Task<MadeImage> MakeAsync(ImageRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = request.Prompt, ["scenario"] = request.Scenario, ["style"] = request.Style,
            ["transparent"] = request.Transparent, ["seed"] = request.Seed,
        };
        if (request.Width is { } width) body["width"] = width;
        if (request.Height is { } height) body["height"] = height;
        using var message = Request(body);
        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ImageBudgetSpentException("the image API's daily budget is spent until 00:00 UTC");
        if (!response.IsSuccessStatusCode)
            throw new IOException($"the image API answered {(int)response.StatusCode} {response.ReasonPhrase}");
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                     ?? throw new IOException("the image API's answer was empty");
        var image = (string?)answer["image"] ?? throw new IOException("the image API's answer had no image");
        return new MadeImage(Convert.FromBase64String(image), (string?)answer["mime"] ?? "image/png",
            (bool?)answer["transparent"] ?? false, (string?)answer["warning"]);
    }
}

/// <summary>One kept image: its size, the broadcast date it was last shown and what it was asked for.</summary>
public sealed record Likeness(long Bytes, string LastUsed, string Prompt);

/// <summary>
/// The episode's images (the owner, 2026-10-27): a face-off side's portrait or war banner, made once from the world by the
/// image API and kept in the bucket (<c>images/char-12.png</c>, <c>images/realm-3.png</c>), then reused every time that
/// side is shown again; and a still of each fight told (<c>images/scene-812.png</c>, 16:9, from the chronicler's picture of
/// it, else the template's). An index (<c>images/index.json</c>) records each image's last use, so the
/// bucket's limit lets the longest unused go first. No image is made when the bucket has no room for it, when the API's
/// day is spent (a 429 stops the run's asking, never retried), or without the API's key; the card then shows none.
/// </summary>
public static class Likenesses
{
    public const string Prefix = "images/", IndexKey = "images/index.json";

    /// <summary>The largest image expected (a transparent 832×1216 PNG), counted against the bucket's room before asking.</summary>
    public const long ExpectedBytes = 2_500_000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>
    /// Where a side's image is kept: by who it is and a fingerprint of what the picture shows (<c>char-12-3f9a1c0b.png</c>).
    /// The picture names a person's decade of age, role, colours, scars and bearing, and a realm's rank, colours and
    /// government; so a person entering a new decade or changing in a major way, or a realm changing its standing, is
    /// drawn anew, while the same person in the same decade keeps one image. Superseded images stop being used and are the
    /// first the bucket's limit lets go.
    /// </summary>
    public static string? KeyFor(World world, string reference) =>
        reference.Split('#') is [var kind, var id] && long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                                                   && kind is Character.Kind or Realm.Kind && Ask(world, reference, "") is { } request
            ? $"{Prefix}{kind}-{id}-{Fingerprint(World(world) + request.Prompt)}.png"
            : null;

    /// <summary>Which world an image belongs to (its seed), so a new age's realm 3 or event 812 is never drawn as the old one's.</summary>
    private static string World(World world) => world.Seed.ToString(CultureInfo.InvariantCulture) + ":";

    /// <summary>A stable short fingerprint of a picture's description (FNV-1a, 32 bits, as hex).</summary>
    public static string Fingerprint(string text)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text)) hash = (hash ^ b) * 16777619u;
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>A person's age as a painter needs it: the decade, not the year.</summary>
    public static string Decade(int age) => age switch
    {
        < 13 => "a child",
        < 20 => "in their teens",
        < 80 => $"in their {(age / 10) switch { 2 => "twenties", 3 => "thirties", 4 => "forties", 5 => "fifties", 6 => "sixties", _ => "seventies" }}",
        _ => "in great old age",
    };

    public static async Task<Dictionary<string, Likeness>> IndexAsync(IObjectStore store, CancellationToken ct = default)
    {
        if (await store.GetAsync(IndexKey, ct).ConfigureAwait(false) is not { } bytes) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Likeness>>(bytes, Json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>Drops deleted images from the index.</summary>
    public static async Task ForgetAsync(IObjectStore store, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var index = await IndexAsync(store, ct).ConfigureAwait(false);
        if (keys.Count(index.Remove) > 0) await SaveAsync(store, index, ct).ConfigureAwait(false);
    }

    private static Task SaveAsync(IObjectStore store, Dictionary<string, Likeness> index, CancellationToken ct) =>
        store.PutAsync(IndexKey, JsonSerializer.SerializeToUtf8Bytes(index, Json), "application/json", ct);

    /// <summary>
    /// The episode with each face-off side's image: the kept one, else one made now if it can be. <paramref name="room"/>
    /// says whether the bucket has room for so many more bytes. Never fails the episode: an image that cannot be had is
    /// left out, and the reason logged.
    /// </summary>
    public static async Task<Episode> AttachAsync(Episode episode, World world, IObjectStore store, IImageMaker? maker, string style,
        string date, Func<long, Task<bool>> room, TextWriter log, CancellationToken ct = default)
    {
        var sides = episode.Parts.Where(p => p.FaceOff is not null).SelectMany(p => new[] { p.FaceOff!.A, p.FaceOff.B }).DistinctBy(s => s.Ref).ToList();
        var fights = episode.Parts.Where(p => p.Scene is { Length: > 0 } && p.EventId != 0).ToList();
        if (sides.Count == 0 && fights.Count == 0) return episode;
        // Every image wanted: each face-off side's (kept for good, reused), then each fight's still (made for its day).
        var keys = sides.ToDictionary(side => side.Ref, side => KeyFor(world, side.Ref));
        var wanted = sides.Select(side => (Key: keys[side.Ref], Label: side.Name, Ask: (Func<ImageRequest?>)(() => Ask(world, side.Ref, style))))
            .Concat(fights.Select(f => (Key: (string?)StillKey(world, f.EventId), Label: f.Title,
                Ask: (Func<ImageRequest?>)(() => new ImageRequest(f.Scene!, "environment", style, false, f.EventId, StillWidth, StillHeight)))))
            .Where(w => w.Key is not null).ToList();
        var had = await MakeAsync(wanted.Select(w => (w.Key!, w.Label, w.Ask)).ToList(), store, maker, date, room, log, ct).ConfigureAwait(false);

        FaceOffSide With(FaceOffSide side) => keys.GetValueOrDefault(side.Ref) is { } key && had.Contains(key) ? side with { Image = key } : side;
        var sections = episode.Parts.Select(p =>
        {
            if (p.FaceOff is { } f) p = p with { FaceOff = f with { A = With(f.A), B = With(f.B) } };
            if (p.Scene is not null && had.Contains(StillKey(world, p.EventId))) p = p with { Still = StillKey(world, p.EventId) };
            return p;
        }).ToList();
        return episode with { Sections = sections };
    }

    /// <summary>
    /// Every standing realm's banner, made once and reused like a face-off's (the owner, 2026-10-29: the website's flags show
    /// them): the realm's reference ("realm#3") and the banner's key, for those that could be had.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> BannersAsync(World world, IObjectStore store, IImageMaker? maker, string style,
        string date, Func<long, Task<bool>> room, TextWriter log, CancellationToken ct = default)
    {
        var realms = world.Realms.Values.Where(r => !r.Destroyed).OrderBy(r => r.Id)
            .Select(r => (Ref: r.Ref.ToString(), r.Name, Key: KeyFor(world, r.Ref.ToString()))).Where(r => r.Key is not null).ToList();
        var had = await MakeAsync(realms.Select(r => (r.Key!, r.Name, (Func<ImageRequest?>)(() => Ask(world, r.Ref, style)))).ToList(),
            store, maker, date, room, log, ct).ConfigureAwait(false);
        return realms.Where(r => had.Contains(r.Key!)).ToDictionary(r => r.Ref, r => r.Key!, StringComparer.Ordinal);
    }

    /// <summary>
    /// The images wanted, each made unless kept already (under the image API's rules: a timeout tried once more, a 429 stops
    /// the day's asking, nothing made near the bucket's limit) and marked used today; the keys of those that could be had.
    /// </summary>
    private static async Task<HashSet<string>> MakeAsync(IReadOnlyList<(string Key, string Label, Func<ImageRequest?> Ask)> wanted, IObjectStore store,
        IImageMaker? maker, string date, Func<long, Task<bool>> room, TextWriter log, CancellationToken ct)
    {
        var index = await IndexAsync(store, ct).ConfigureAwait(false);
        var kept = (await store.ListAsync(Prefix, ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var spent = maker is null;
        if (maker is null) log.WriteLine("Images: no FLUX_ACCESS_KEY, so none is made; kept ones are still shown.");
        var had = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, label, ask) in wanted)
        {
            if (!kept.Contains(key))
            {
                if (spent || ask() is not { } request) continue;
                if (!await room(ExpectedBytes).ConfigureAwait(false))
                {
                    log.WriteLine($"Images: the bucket is near its limit, so no image of {label} is made.");
                    spent = true;
                    continue;
                }
                try
                {
                    // The image API sometimes times out: one more try, never after a 429.
                    MadeImage made;
                    try { made = await maker!.MakeAsync(request, ct).ConfigureAwait(false); }
                    catch (Exception first) when (first is IOException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        log.WriteLine($"Images: {label}: {first.Message}; trying once more.");
                        made = await maker!.MakeAsync(request, ct).ConfigureAwait(false);
                    }
                    if (made.Warning is { Length: > 0 } warning) log.WriteLine($"Images: {label}: {warning}");
                    await store.PutAsync(key, made.Bytes, made.Mime, ct).ConfigureAwait(false);
                    index[key] = new Likeness(made.Bytes.LongLength, date, request.Prompt);
                    kept.Add(key);
                    log.WriteLine($"Images: made {key} ({made.Bytes.LongLength / 1000} kB{(request.Transparent && !made.Transparent ? ", not transparent" : "")}).");
                }
                catch (ImageBudgetSpentException e)
                {
                    log.WriteLine($"Images: {e.Message}; no more are asked for today.");
                    spent = true;
                    continue;
                }
                catch (Exception e) when (e is IOException or HttpRequestException or JsonException or FormatException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
                {
                    log.WriteLine($"Images: no image of {label} ({e.Message}).");
                    continue;
                }
            }
            index[key] = index.TryGetValue(key, out var known) ? known with { LastUsed = date } : new Likeness(0, date, "");
            had.Add(key);
        }
        if (had.Count > 0) await SaveAsync(store, index, ct).ConfigureAwait(false);
        return had;
    }

    /// <summary>Where a fight's still is kept, by its event.</summary>
    public static string StillKey(World world, long eventId) =>
        $"{Prefix}scene-{Fingerprint(World(world))}-{eventId.ToString(CultureInfo.InvariantCulture)}.png";

    /// <summary>A still's size: 16:9, within the API's 1,536 and a multiple of 16.</summary>
    public const int StillWidth = 1280, StillHeight = 720;

    /// <summary>
    /// What to ask the API for a side, from the world: a person's full figure (scenario <c>character</c>), a realm's war
    /// banner (a single object); always transparent, as it stands beside the card; seeded by the id, so it can be made
    /// again alike. The subject only: no framing, background or transparency words (the API's guide).
    /// </summary>
    public static ImageRequest? Ask(World world, string reference, string style)
    {
        if (reference.Split('#') is not [var kind, var idText] || !long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;
        if (kind == Character.Kind && world.Characters.TryGetValue(id, out var c))
        {
            // A race is painted as it looks (sub-project 13): its name means nothing to the painter.
            var culture = Peoples.Of(world, c) is { } race ? $" of the {race.Name} race, {race.Looks}"
                : world.Cultures.TryGetValue(c.CultureId, out var cu) ? $" of the {cu.Name} people" : "";
            var role = c.Role switch
            {
                Role.Lord => "noble lord", Role.Knight => "knight", Role.Mage => "mage", Role.Priest => "priest", Role.Merchant => "merchant",
                Role.Elder => "village elder", Role.Adventurer => "adventurer", Role.Rebel => "rebel", _ => "commoner",
            };
            var (who, dress) = (c.Sex == Sex.Female ? "woman" : "man",
                world.Realms.Values.FirstOrDefault(r => Realms.RulerOf(world, r)?.HouseId == c.HouseId && c.HouseId != 0) is { } realm
                    ? $"{realm.Colors[0]} and {realm.Colors.ElementAtOrDefault(1) ?? "white"}" : "earthy brown and grey");
            var gear = c.Attributes.Prowess >= 14 || c.Role is Role.Knight ? "in battle-worn armour with a sword" : c.Role == Role.Mage ? "in robes with a staff" : "with a sword at the hip";
            var traits = c.Traits.Count > 0 ? $", {string.Join(", ", c.Traits.Take(3))} in bearing" : "";
            var scars = c.Wounds.Count > 0 ? ", scarred from old wounds" : "";
            return new ImageRequest($"a {who} {Decade(c.Age(world.Date)).Replace("their", c.Sex == Sex.Female ? "her" : "his")}, a {role}{culture}, {gear}, clothes in {dress}{traits}{scars}, medieval fantasy duellist",
                "character", style, true, id);
        }
        if (kind == Realm.Kind && world.Realms.TryGetValue(id, out var r2))
        {
            var rank = r2.Rank.ToString().ToLowerInvariant();
            var crest = r2.Government switch
            {
                Government.Theocracy => "a holy sun emblem", Government.Magocracy => "an arcane star emblem", Government.MerchantRepublic => "a ship emblem",
                Government.Tribal => "a beast's head emblem", Government.ElectiveKingdom => "a crowned tower emblem", _ => "a crowned lion emblem",
            };
            // A hanging pennant, flat and seen straight on (the owner, 2026-10-28): cloth on a gilded crossbar, a pointed
            // foot, a stitched border, the crest at its heart.
            return new ImageRequest($"the heraldic banner of the {rank} of {r2.Name}: a flat hanging pennant seen straight on, "
                                    + $"{r2.Colors[0]} cloth with a stitched {r2.Colors.ElementAtOrDefault(1) ?? "gold"} border, hung from a "
                                    + $"horizontal gilded rod with round finials, its bottom edge cut to a point, {crest} in the middle, "
                                    + "no pole, no wind, symmetrical, medieval fantasy",
                "game-asset", style, true, id);
        }
        return null;
    }
}

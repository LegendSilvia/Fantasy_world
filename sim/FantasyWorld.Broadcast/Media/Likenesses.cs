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

/// <summary>What an image is asked for: the subject only (the scenario frames it), the scenario, the style and the seed.</summary>
public sealed record ImageRequest(string Prompt, string Scenario, string Style, bool Transparent, long Seed);

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

    public async Task<MadeImage> MakeAsync(ImageRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = request.Prompt, ["scenario"] = request.Scenario, ["style"] = request.Style,
            ["transparent"] = request.Transparent, ["seed"] = request.Seed,
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/generate")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-access-key", accessKey);
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
/// The face-offs' images (the owner, 2026-10-27): a duellist's full-figure portrait or a realm's war banner, made once from
/// the world by the image API and kept in the bucket (<c>images/char-12.png</c>, <c>images/realm-3.png</c>), then
/// reused every time that side is shown again. An index (<c>images/index.json</c>) records each image's last use, so the
/// bucket's limit lets the longest unused go first. No image is made when the bucket has no room for it, when the API's
/// day is spent (a 429 stops the run's asking, never retried), or without the API's key; the card then shows none.
/// </summary>
public static class Likenesses
{
    public const string Prefix = "images/", IndexKey = "images/index.json";

    /// <summary>The largest image expected (a transparent 832×1216 PNG), counted against the bucket's room before asking.</summary>
    public const long ExpectedBytes = 2_500_000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>Where a side's image is kept, from its reference (<c>char#12</c>, <c>realm#3</c>).</summary>
    public static string? KeyFor(string reference) =>
        reference.Split('#') is [var kind, var id] && long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                                                   && kind is Character.Kind or Realm.Kind
            ? $"{Prefix}{kind}-{id}.png"
            : null;

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
        var sides = episode.Parts.Where(p => p.FaceOff is not null).SelectMany(p => new[] { p.FaceOff!.A, p.FaceOff.B }).ToList();
        if (sides.Count == 0) return episode;
        var index = await IndexAsync(store, ct).ConfigureAwait(false);
        var kept = (await store.ListAsync(Prefix, ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var spent = maker is null;
        if (maker is null) log.WriteLine("Face-off images: no FLUX_ACCESS_KEY, so none is made; kept ones are still shown.");
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var side in sides.DistinctBy(s => s.Ref))
        {
            if (KeyFor(side.Ref) is not { } key) continue;
            if (!kept.Contains(key))
            {
                if (spent || Ask(world, side.Ref, style) is not { } request) continue;
                if (!await room(ExpectedBytes).ConfigureAwait(false))
                {
                    log.WriteLine($"Face-off images: the bucket is near its limit, so no image of {side.Name} is made.");
                    spent = true;
                    continue;
                }
                try
                {
                    var made = await maker!.MakeAsync(request, ct).ConfigureAwait(false);
                    if (made.Warning is { Length: > 0 } warning) log.WriteLine($"Face-off images: {side.Name}: {warning}");
                    await store.PutAsync(key, made.Bytes, made.Mime, ct).ConfigureAwait(false);
                    index[key] = new Likeness(made.Bytes.LongLength, date, request.Prompt);
                    kept.Add(key);
                    log.WriteLine($"Face-off images: made {key} ({made.Bytes.LongLength / 1000} kB{(made.Transparent ? "" : ", not transparent")}).");
                }
                catch (ImageBudgetSpentException e)
                {
                    log.WriteLine($"Face-off images: {e.Message}; no more are asked for today.");
                    spent = true;
                    continue;
                }
                catch (Exception e) when (e is IOException or HttpRequestException or JsonException or FormatException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
                {
                    log.WriteLine($"Face-off images: no image of {side.Name} ({e.Message}).");
                    continue;
                }
            }
            index[key] = index.TryGetValue(key, out var known) ? known with { LastUsed = date } : new Likeness(0, date, "");
            images[side.Ref] = key;
        }
        if (images.Count > 0) await SaveAsync(store, index, ct).ConfigureAwait(false);

        FaceOffSide With(FaceOffSide side) => images.TryGetValue(side.Ref, out var key) ? side with { Image = key } : side;
        var sections = episode.Parts.Select(p => p.FaceOff is { } f ? p with { FaceOff = f with { A = With(f.A), B = With(f.B) } } : p).ToList();
        return episode with { Sections = sections };
    }

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
            var culture = world.Cultures.TryGetValue(c.CultureId, out var cu) ? $" of the {cu.Name} people" : "";
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
            return new ImageRequest($"a {c.Age(world.Date)}-year-old {who}, a {role}{culture}, {gear}, clothes in {dress}{traits}{scars}, medieval fantasy duellist",
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
            return new ImageRequest($"the war banner of the {rank} of {r2.Name}, a tall swallow-tailed heraldic flag in {r2.Colors[0]} and "
                                    + $"{r2.Colors.ElementAtOrDefault(1) ?? "white"} with {crest}, on a carved wooden pole with a gilded finial, medieval fantasy",
                "game-asset", style, true, id);
        }
        return null;
    }
}

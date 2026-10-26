using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;

namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// Cloudflare R2 through its S3 API (<c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>, region <c>auto</c>),
/// path-style: <c>/&lt;bucket&gt;/&lt;key&gt;</c>. Errors carry the status code only, never a credential.
/// </summary>
public sealed class R2ObjectStore(HttpClient http, string accountId, string bucket, string accessKeyId, string secretAccessKey,
    Func<DateTimeOffset>? clock = null) : IObjectStore
{
    public const string Region = "auto";

    private static readonly XNamespace S3 = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public string Host => $"{accountId}.r2.cloudflarestorage.com";

    /// <summary>Reads the store's settings from the environment, or null when any of them is missing.</summary>
    public static R2ObjectStore? FromEnvironment(HttpClient http, Func<string, string?> env) =>
        (env("R2_ACCOUNT_ID"), env("R2_BUCKET"), env("R2_ACCESS_KEY_ID"), env("R2_SECRET_ACCESS_KEY")) is
            ({ Length: > 0 } account, { Length: > 0 } bucket, { Length: > 0 } id, { Length: > 0 } secret)
            ? new R2ObjectStore(http, account, bucket, id, secret)
            : null;

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, key, [], null, null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        Ensure(response, "read", key);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Put, key, [], content, contentType, ct).ConfigureAwait(false);
        Ensure(response, "write", key);
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        string? token = null;
        do
        {
            var query = new List<KeyValuePair<string, string>> { new("list-type", "2"), new("prefix", prefix) };
            if (token is not null) query.Add(new("continuation-token", token));
            using var response = await SendAsync(HttpMethod.Get, null, query, null, null, ct).ConfigureAwait(false);
            Ensure(response, "list", prefix);
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            keys.AddRange(xml.Descendants(S3 + "Contents").Select(c => (string)c.Element(S3 + "Key")!));
            token = (string?)xml.Root!.Element(S3 + "IsTruncated") == "true" ? (string?)xml.Root.Element(S3 + "NextContinuationToken") : null;
        }
        while (token is not null);
        return keys.Order(StringComparer.Ordinal).ToList();
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, key, [], null, null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) Ensure(response, "delete", key);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string? key, List<KeyValuePair<string, string>> query,
        byte[]? content, string? contentType, CancellationToken ct)
    {
        var path = "/" + SigV4.Encode(bucket) + (key is null ? "" : "/" + SigV4.Encode(key, keepSlashes: true));
        var queryText = string.Join("&", query.Select(kv => $"{SigV4.Encode(kv.Key)}={SigV4.Encode(kv.Value)}"));
        var request = new HttpRequestMessage(method, $"https://{Host}{path}{(queryText.Length > 0 ? "?" + queryText : "")}");
        var payloadHash = SigV4.Hash(content ?? []);
        var now = _clock();
        var headers = new Dictionary<string, string>
        {
            ["host"] = Host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = SigV4.AmzDate(now),
        };
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", headers["x-amz-date"]);
        request.Headers.TryAddWithoutValidation("Authorization",
            SigV4.Authorization(method.Method, path, query, headers, payloadHash, now, Region, "s3", accessKeyId, secretAccessKey));
        if (content is not null)
        {
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/octet-stream");
        }
        // Transport failures become IOException, and name no host: the host carries the account id.
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new IOException($"R2 could not be reached ({e.HttpRequestError}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("R2 did not answer in time.");
        }
    }

    private static void Ensure(HttpResponseMessage response, string action, string key)
    {
        if (!response.IsSuccessStatusCode)
            throw new IOException($"R2 could not {action} '{key}': HTTP {(int)response.StatusCode}.");
    }
}

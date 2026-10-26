using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// AWS Signature Version 4, which R2's S3 API accepts. Written by hand (about sixty lines) rather than taking the AWS SDK:
/// the broadcast needs four calls, and the SDK's newer default checksums are a known source of trouble with S3-compatible
/// stores. Checked against AWS's published example signature.
/// </summary>
public static class SigV4
{
    public const string Algorithm = "AWS4-HMAC-SHA256";

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// The Authorization header for a request. <paramref name="headers"/> are the headers to sign, with lowercase names;
    /// they must include <c>host</c>, <c>x-amz-date</c> and <c>x-amz-content-sha256</c>.
    /// </summary>
    public static string Authorization(string method, string canonicalUri, IEnumerable<KeyValuePair<string, string>> query,
        IReadOnlyDictionary<string, string> headers, string payloadHash, DateTimeOffset time, string region, string service,
        string accessKeyId, string secretAccessKey)
    {
        var day = time.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var stamp = AmzDate(time);
        var canonicalQuery = string.Join("&", query
            .Select(kv => (Key: Encode(kv.Key), Value: Encode(kv.Value)))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        var signed = headers.OrderBy(h => h.Key, StringComparer.Ordinal).ToList();
        var canonicalHeaders = string.Concat(signed.Select(h => $"{h.Key}:{h.Value.Trim()}\n"));
        var signedHeaders = string.Join(";", signed.Select(h => h.Key));
        var canonicalRequest = $"{method}\n{canonicalUri}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var scope = $"{day}/{region}/{service}/aws4_request";
        var stringToSign = $"{Algorithm}\n{stamp}\n{scope}\n{Hash(Encoding.UTF8.GetBytes(canonicalRequest))}";
        var key = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), day), region), service), "aws4_request");
        var signature = Convert.ToHexString(Hmac(key, stringToSign)).ToLowerInvariant();
        return $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    public static string AmzDate(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>S3's URI encoding: every byte but the unreserved characters as %XX; slashes kept only in paths.</summary>
    public static string Encode(string value, bool keepSlashes = false)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~' || (keepSlashes && c == '/'))
                builder.Append(c);
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}

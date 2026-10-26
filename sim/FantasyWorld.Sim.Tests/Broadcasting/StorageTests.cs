using System.Net;
using System.Text;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

public class StorageTests
{
    /// <summary>Answers each request from a list of canned responses and keeps every request it saw.</summary>
    internal sealed class FakeHttp(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpRequestMessage Request, byte[]? Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct)));
            var (status, body) = answers[Math.Min(_next++, answers.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public void SigV4_MatchesAwsPublishedExample()
    {
        // "GET Object" from AWS's Signature Version 4 examples for S3.
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = SigV4.Hash([]),
            ["x-amz-date"] = "20130524T000000Z",
        };
        var auth = SigV4.Authorization("GET", "/test.txt", [], headers, SigV4.Hash([]),
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero), "us-east-1", "s3",
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        Assert.Equal("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, " +
                     "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, " +
                     "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", auth);
        Assert.Equal("a%20b%2Fc~d", SigV4.Encode("a b/c~d"));
        Assert.Equal("live/backups/2026-10-27.fw.db", SigV4.Encode("live/backups/2026-10-27.fw.db", keepSlashes: true));
    }

    [Fact]
    public async Task R2_SignsPathStyleRequests_AndReadsMissingAsNull()
    {
        var http = new FakeHttp((HttpStatusCode.OK, ""), (HttpStatusCode.NotFound, "<Error/>"), (HttpStatusCode.Forbidden, ""));
        var store = new R2ObjectStore(new HttpClient(http), "acct", "fw", "KEYID", "SECRET",
            () => new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero));
        await store.PutAsync("live/world.fw.db", [1, 2, 3], "application/octet-stream");
        Assert.Null(await store.GetAsync("live/missing.json"));
        var error = await Assert.ThrowsAsync<IOException>(() => store.GetAsync("live/world.fw.db"));

        var (put, body) = http.Seen[0];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://acct.r2.cloudflarestorage.com/fw/live/world.fw.db", put.RequestUri!.ToString());
        Assert.Equal([1, 2, 3], body);
        Assert.Equal(SigV4.Hash([1, 2, 3]), put.Headers.GetValues("x-amz-content-sha256").Single());
        var auth = put.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=KEYID/20261026/auto/s3/aws4_request, SignedHeaders=host;x-amz-content-sha256;x-amz-date,", auth);
        Assert.DoesNotContain("SECRET", auth);
        Assert.Contains("HTTP 403", error.Message);
        Assert.DoesNotContain("SECRET", error.Message);
    }

    private sealed class Unreachable(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw error;
    }

    [Fact]
    public async Task R2_TurnsTransportFailuresIntoIOErrors_ThatNameNoHost()
    {
        foreach (var error in new Exception[] { new HttpRequestException(HttpRequestError.NameResolutionError, "acct.r2.cloudflarestorage.com:443"), new TaskCanceledException() })
        {
            var store = new R2ObjectStore(new HttpClient(new Unreachable(error)), "acct", "fw", "KEYID", "SECRET");
            var failure = await Assert.ThrowsAsync<IOException>(() => store.GetAsync("live/world.fw.db"));
            Assert.DoesNotContain("acct", failure.Message);
        }
    }

    [Fact]
    public async Task R2_ListsEveryPage()
    {
        const string ns = "http://s3.amazonaws.com/doc/2006-03-01/";
        var http = new FakeHttp(
            (HttpStatusCode.OK, $"<ListBucketResult xmlns=\"{ns}\"><IsTruncated>true</IsTruncated><NextContinuationToken>t/1=</NextContinuationToken>" +
                                "<Contents><Key>live/backups/b</Key></Contents><Contents><Key>live/backups/a</Key></Contents></ListBucketResult>"),
            (HttpStatusCode.OK, $"<ListBucketResult xmlns=\"{ns}\"><IsTruncated>false</IsTruncated><Contents><Key>live/backups/c</Key></Contents></ListBucketResult>"));
        var store = new R2ObjectStore(new HttpClient(http), "acct", "fw", "KEYID", "SECRET");
        Assert.Equal(["live/backups/a", "live/backups/b", "live/backups/c"], await store.ListAsync("live/backups/"));
        Assert.Equal("?list-type=2&prefix=live%2Fbackups%2F", http.Seen[0].Request.RequestUri!.Query);
        Assert.Equal("?list-type=2&prefix=live%2Fbackups%2F&continuation-token=t%2F1%3D", http.Seen[1].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task LocalAndPrefixedStores_KeepKeysApart()
    {
        using var dir = new TempDir();
        var root = new LocalObjectStore(dir.Path);
        var live = new PrefixedStore(root, "live/");
        var staging = new PrefixedStore(root, "staging/");
        await live.PutAsync("backups/2026-10-27.fw.db", Encoding.UTF8.GetBytes("a"), "application/octet-stream");
        await staging.PutAsync("world.fw.db", Encoding.UTF8.GetBytes("b"), "application/octet-stream");
        Assert.Equal(["backups/2026-10-27.fw.db"], await live.ListAsync(""));
        Assert.Equal(["live/backups/2026-10-27.fw.db", "staging/world.fw.db"], await root.ListAsync(""));
        Assert.Null(await live.GetAsync("world.fw.db"));
        await live.DeleteAsync("backups/2026-10-27.fw.db");
        Assert.Empty(await live.ListAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => root.GetAsync("../outside"));
    }
}

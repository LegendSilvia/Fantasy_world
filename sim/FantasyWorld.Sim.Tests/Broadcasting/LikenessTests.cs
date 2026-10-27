using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Tests.Chronicle;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Broadcasting;

/// <summary>The face-offs' images (FLUX portraits and banners, kept in the bucket) and the bucket's 10 GB limit.</summary>
public class LikenessTests
{
    private sealed class FakeMaker(Func<ImageRequest, MadeImage> make) : IImageMaker
    {
        public List<ImageRequest> Asked { get; } = [];

        public Task<MadeImage> MakeAsync(ImageRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(make(request));
        }
    }

    private static MadeImage Png(ImageRequest _) => new([0x89, 0x50, 0x4e, 0x47, 1, 2, 3], "image/png", true, null);

    [Fact]
    public async Task EachSide_GetsItsImage_MadeOnce_AndReusedAfter()
    {
        var (world, log, strong, sly, _) = FaceOffTests.ADuelDeclared();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        using var dir = new TempDir();
        var store = new PrefixedStore(new LocalObjectStore(dir.Path), "live/");
        var maker = new FakeMaker(Png);
        var output = new StringWriter();

        var told = await Likenesses.AttachAsync(episode, world, store, maker, "hand-painted", "2026-10-27", _ => Task.FromResult(true), output);
        var f = told.Parts.Single(p => p.FaceOff is not null).FaceOff!;
        Assert.Equal(($"images/char-{strong.Id}.png", $"images/char-{sly.Id}.png"), (f.A.Image, f.B.Image));
        Assert.NotNull(await store.GetAsync(f.A.Image!));
        // A full figure, standing beside the card, seeded by the person, the subject only.
        Assert.All(maker.Asked, r => Assert.Equal(("character", "hand-painted", true), (r.Scenario, r.Style, r.Transparent)));
        Assert.Equal([strong.Id, sly.Id], maker.Asked.Select(r => r.Seed));
        Assert.All(maker.Asked, r => Assert.DoesNotMatch("(?i)background|transparen|frame|portrait of", r.Prompt));
        Assert.Contains("30-year-old", maker.Asked[0].Prompt);

        // The next time these two are shown, nothing is made again; the index says when each was last used.
        var again = await Likenesses.AttachAsync(episode, world, store, maker, "hand-painted", "2026-10-29", _ => Task.FromResult(true), output);
        Assert.Equal(2, maker.Asked.Count);
        Assert.Equal(f.A.Image, again.Parts.Single(p => p.FaceOff is not null).FaceOff!.A.Image);
        Assert.Equal("2026-10-29", (await Likenesses.IndexAsync(store))[f.A.Image!].LastUsed);
    }

    [Fact]
    public async Task NoImage_IsAskedFor_PastA429_OrWithoutRoom_AndTheCardsGoOutWithout()
    {
        var (world, log, _, _, _) = FaceOffTests.ADuelDeclared();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        using var dir = new TempDir();
        var store = new LocalObjectStore(dir.Path);
        var output = new StringWriter();

        var spent = new FakeMaker(_ => throw new ImageBudgetSpentException("the image API's daily budget is spent until 00:00 UTC"));
        var told = await Likenesses.AttachAsync(episode, world, store, spent, "hand-painted", "2026-10-27", _ => Task.FromResult(true), output);
        Assert.Single(spent.Asked);
        Assert.All(told.Parts.Where(p => p.FaceOff is not null), p => Assert.Null(p.FaceOff!.A.Image ?? p.FaceOff.B.Image));
        Assert.Contains("no more are asked for today", output.ToString());

        var full = new FakeMaker(Png);
        await Likenesses.AttachAsync(episode, world, store, full, "hand-painted", "2026-10-27", _ => Task.FromResult(false), output);
        Assert.Empty(full.Asked);
        Assert.Contains("near its limit", output.ToString());
    }

    [Fact]
    public async Task TheApi_IsAskedWithTheKeyInItsHeader_AndA429_IsNeverRetried()
    {
        var image = Convert.ToBase64String([0x89, 0x50, 0x4e, 0x47]);
        var ok = new StorageTests.FakeHttp((HttpStatusCode.OK, $"{{\"image\":\"{image}\",\"mime\":\"image/png\",\"transparent\":true}}"));
        var made = await new FluxImages(new HttpClient(ok), "https://flux.example/", "SECRET").MakeAsync(new ImageRequest("a knight", "character", "hand-painted", true, 7));
        Assert.Equal(4, made.Bytes.Length);
        var (request, body) = ok.Seen.Single();
        Assert.Equal("https://flux.example/generate", request.RequestUri!.ToString());
        Assert.Equal("SECRET", request.Headers.GetValues("x-access-key").Single());
        var sent = JsonNode.Parse(body!)!;
        Assert.Equal(("character", 7L, true), ((string?)sent["scenario"], (long)sent["seed"]!, (bool)sent["transparent"]!));

        var busy = new StorageTests.FakeHttp((HttpStatusCode.TooManyRequests, "{\"error\":\"budget\"}"));
        var spent = await Assert.ThrowsAsync<ImageBudgetSpentException>(() =>
            new FluxImages(new HttpClient(busy), "https://flux.example", "SECRET").MakeAsync(new ImageRequest("a knight", "character", "hand-painted", true, 7)));
        Assert.Single(busy.Seen);
        Assert.DoesNotContain("SECRET", spent.Message);
    }

    [Fact]
    public void ARealm_IsShownByItsBanner()
    {
        var world = FantasyWorld.Sim.WorldGen.WorldGenerator.Generate(
            new FantasyWorld.Sim.WorldGen.WorldGenOptions(3, FantasyWorld.Sim.WorldGen.WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var realm = world.Realms.Values.First();
        var request = Likenesses.Ask(world, realm.Ref.ToString(), "hand-painted")!;
        Assert.Equal(("game-asset", true, realm.Id), (request.Scenario, request.Transparent, request.Seed));
        Assert.Contains(realm.Name, request.Prompt);
        Assert.Contains(realm.Colors[0], request.Prompt);
        Assert.Equal($"images/realm-{realm.Id}.png", Likenesses.KeyFor(realm.Ref.ToString()));
    }

    private static StoredObject O(string key, double megabytes) => new(key, (long)(megabytes * 1_000_000));

    [Fact]
    public void TheBucket_StaysUnderItsLimit_TheLeastNeededGoingFirst()
    {
        var limit = StorageBudget.Bytes(1);
        var objects = new List<StoredObject>
        {
            O("live/world.fw.db", 100), O("live/manifest.json", 1), O("live/web/world.json.gz", 50),
            O("live/backups/2026-10-01.fw.db", 50), O("live/backups/2026-10-26.fw.db", 50),
            O("live/episodes/2026-10-01/episode.mp4", 150), O("live/episodes/2026-10-01/film/chunk-00.mp4", 100),
            O("live/episodes/2026-10-26/film/chunk-00.mp4", 100), O("live/episodes/2026-10-27/film/chunk-00.mp4", 100),
            O("live/episodes/2026-10-26/episode.mp4", 150), O("staging/episodes/2026-10-20/episode.mp4", 60),
            O("live/images/character-1.png", 2), O("live/images/realm-3.png", 2),
        };
        // 965 MB of 1 GB: past the line (900), so down to 800.
        var doomed = StorageBudget.ToDelete(objects, "2026-10-27", limit, "live/");
        Assert.Equal(["live/episodes/2026-10-01/film/chunk-00.mp4", "live/episodes/2026-10-26/film/chunk-00.mp4"], doomed);

        // Further over: dry runs' media, then live videos past a week; never the world, today's film or the newest backup.
        objects.Add(O("live/episodes/2026-10-02/episode.mp4", 300));
        doomed = StorageBudget.ToDelete(objects, "2026-10-27", limit, "live/", new Dictionary<string, string> { ["live/images/realm-3.png"] = "2026-10-27" });
        Assert.Equal(["live/episodes/2026-10-01/film/chunk-00.mp4", "live/episodes/2026-10-26/film/chunk-00.mp4",
            "staging/episodes/2026-10-20/episode.mp4", "live/episodes/2026-10-01/episode.mp4", "live/episodes/2026-10-02/episode.mp4"], doomed);
        Assert.True(objects.Where(o => !doomed.Contains(o.Key)).Sum(o => o.Bytes) <= limit * StorageBudget.PruneTo);

        Assert.Empty(StorageBudget.ToDelete([O("live/world.fw.db", 899)], "2026-10-27", limit, "live/"));
        Assert.True(StorageBudget.Room(890_000_000, 2_500_000, limit));
        Assert.False(StorageBudget.Room(899_000_000, 2_500_000, limit));
    }

    [Fact]
    public async Task Stores_TellEachObjectsSize()
    {
        using var dir = new TempDir();
        var root = new LocalObjectStore(dir.Path);
        await root.PutAsync("live/a.bin", new byte[5], "application/octet-stream");
        await root.PutAsync("staging/b.bin", new byte[3], "application/octet-stream");
        Assert.Equal([new StoredObject("live/a.bin", 5), new StoredObject("staging/b.bin", 3)], await root.ListSizesAsync(""));
        Assert.Equal([new StoredObject("a.bin", 5)], await new PrefixedStore(root, "live/").ListSizesAsync(""));

        const string ns = "http://s3.amazonaws.com/doc/2006-03-01/";
        var http = new StorageTests.FakeHttp((HttpStatusCode.OK,
            $"<ListBucketResult xmlns=\"{ns}\"><IsTruncated>false</IsTruncated><Contents><Key>live/x</Key><Size>1234</Size></Contents></ListBucketResult>"));
        Assert.Equal([new StoredObject("live/x", 1234)], await new R2ObjectStore(new HttpClient(http), "acct", "fw", "K", "S").ListSizesAsync("live/"));
    }
}

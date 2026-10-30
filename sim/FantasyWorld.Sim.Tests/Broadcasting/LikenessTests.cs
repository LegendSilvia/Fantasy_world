using System.Net;
using System.Text.Json.Nodes;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Storage;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
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
        Assert.Equal((Likenesses.KeyFor(world, strong.Ref.ToString()), Likenesses.KeyFor(world, sly.Ref.ToString())), (f.A.Image, f.B.Image));
        Assert.StartsWith($"images/char-{strong.Id}-", f.A.Image);
        Assert.NotNull(await store.GetAsync(f.A.Image!));
        // A full figure, standing beside the card, seeded by the person, the subject only.
        var portraits = maker.Asked.Where(r => r.Scenario == "character").ToList();
        Assert.All(portraits, r => Assert.Equal(("hand-painted", true), (r.Style, r.Transparent)));
        Assert.Equal([strong.Id, sly.Id], portraits.Select(r => r.Seed));
        Assert.All(maker.Asked, r => Assert.DoesNotMatch("(?i)background|transparen|frame|portrait of", r.Prompt));
        Assert.Contains("a man in the prime of life", portraits[0].Prompt);

        // The next time these two are shown, nothing is made again; the index says when each was last used.
        var asked = maker.Asked.Count;
        var again = await Likenesses.AttachAsync(episode, world, store, maker, "hand-painted", "2026-10-29", _ => Task.FromResult(true), output);
        Assert.Equal(asked, maker.Asked.Count);
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
        Assert.Null(sent["ref"]);
        var withRef = new StorageTests.FakeHttp((HttpStatusCode.OK, $"{{\"image\":\"{image}\"}}"));
        await new FluxImages(new HttpClient(withRef), "https://flux.example", "SECRET")
            .MakeAsync(new ImageRequest("a duel", "environment", "hand-painted", false, 8, Reference: [1, 2, 3]));
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), (string?)JsonNode.Parse(withRef.Seen.Single().Body!)!["ref"]);

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
        Assert.StartsWith($"images/realm-{realm.Id}-", Likenesses.KeyFor(world, realm.Ref.ToString()));
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

    /// <summary>A duel declared and then fought: the fight's section gets a still.</summary>
    private static (World World, EventLog Log, Character Strong, Character Sly) ADuelFought()
    {
        var (world, log, strong, sly, declared) = FaceOffTests.ADuelDeclared();
        log.Append(world.Date, new EventDraft("DuelFought",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = strong.Ref, ["defender"] = sly.Ref, ["victor"] = strong.Ref },
            Place: declared.Place, Payload: new Dictionary<string, string> { ["stakes"] = "to the death" },
            Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 0.9) }, Importance: 45, Tags: new[] { "Characters", "Deeds" }));
        return (world, log, strong, sly);
    }

    [Fact]
    public async Task AFight_IsPaintedAsAStill_FromItsPicture_WithoutNames()
    {
        var (world, log, strong, sly) = ADuelFought();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        var fight = Assert.Single(episode.Parts, p => p.Scene is not null);
        Assert.Equal("DuelFought", log.All.Single(e => e.Id == fight.EventId).Type);
        Assert.Contains("two duellists crossing swords", fight.Scene);
        Assert.DoesNotContain(strong.Name, fight.Scene);
        Assert.DoesNotContain(sly.Name, fight.Scene);
        Assert.DoesNotContain(fight.Scene!, episode.Script);

        using var dir = new TempDir();
        var store = new LocalObjectStore(dir.Path);
        var maker = new FakeMaker(Png);
        var told = await Likenesses.AttachAsync(episode, world, store, maker, "hand-painted", "2026-10-27", _ => Task.FromResult(true), new StringWriter());
        var still = maker.Asked.Single(r => r.Scenario == "environment");
        Assert.Equal((fight.Scene, false, fight.EventId, 1280, 720), (still.Prompt, still.Transparent, still.Seed, still.Width!.Value, still.Height!.Value));
        Assert.Equal(Likenesses.StillKey(world, fight.EventId), told.Parts.Single(p => p.EventId == fight.EventId).Still);
        Assert.NotNull(await store.GetAsync(Likenesses.StillKey(world, fight.EventId)));
    }

    [Fact]
    public async Task AFightsStill_DrawsTheFighters_AsTheirPortraitsShowThem()
    {
        var (world, log, strong, sly) = ADuelFought();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        var fight = Assert.Single(episode.Parts, p => p.Scene is not null);
        using var dir = new TempDir();
        var store = new LocalObjectStore(dir.Path);
        var maker = new FakeMaker(Png);
        var laid = new List<IReadOnlyList<byte[]>>();
        byte[] sheet = [9, 9, 9];
        await Likenesses.AttachAsync(episode, world, store, maker, "hand-painted", "2026-10-27", _ => Task.FromResult(true), new StringWriter(),
            events: log, sheet: (images, _) => { laid.Add(images); return Task.FromResult<byte[]?>(sheet); });

        // The portraits are made first, then laid side by side as the still's reference; the words name each one's look.
        var still = maker.Asked.Single(r => r.Scenario == "environment");
        Assert.StartsWith(fight.Scene!.TrimEnd('.'), still.Prompt);
        Assert.Contains($"the first duellist: {Likenesses.Ask(world, strong.Ref.ToString(), "")!.Prompt.Replace(", medieval fantasy duellist", "")}", still.Prompt);
        Assert.Contains("the second: ", still.Prompt);
        Assert.Contains("image 0", still.Prompt);
        Assert.DoesNotContain(strong.Name, still.Prompt);
        Assert.DoesNotContain(sly.Name, still.Prompt);
        Assert.True(still.Prompt.Length <= Likenesses.MaxPrompt);
        Assert.Equal(sheet, still.Reference);
        Assert.Equal(2, Assert.Single(laid).Count);
        Assert.Null(maker.Asked.First(r => r.Scenario == "character").Reference);

        // Without the portraits kept, the words alone.
        using var empty = new TempDir();
        var alone = await Likenesses.StillAsync(world, log, fight, new LocalObjectStore(empty.Path), "hand-painted", (_, _) => Task.FromResult<byte[]?>(sheet));
        Assert.Null(alone!.Reference);
        Assert.Contains("the first duellist: ", alone.Prompt);
        Assert.DoesNotContain("image 0", alone.Prompt);
    }

    [Fact]
    public void TheChronicler_PaintsTheFight_InItsOwnWords_AndNeverAloud()
    {
        var (world, log, _, _) = ADuelFought();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        var painted = "Steel flashes in the rain as a knight in crimson drives a cloaked rival in azure back against a stone well.";
        LlmNarrator Narrator(object answer, out FantasyWorld.Sim.Llm.FakeLlmProvider fake)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(answer);
            fake = new FantasyWorld.Sim.Llm.FakeLlmProvider(_ => json);
            return new LlmNarrator(new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal())));
        }

        var written = Narrator(new { sections = episode.Parts.Select(p => p.Text), scenes = episode.Parts.Select(p => p.Scene is null ? "" : painted) }, out var fake)
            .EpisodeScript(episode, 20_000);
        Assert.Contains("A scene to paint", fake.Requests.First(r => r.Key.StartsWith("narrate|episode-sections", StringComparison.Ordinal)).Prompt);
        Assert.Equal(painted, written.Parts.Single(p => p.Scene is not null).Scene);
        Assert.DoesNotContain(painted, written.Script);

        // An answer without pictures keeps the template's.
        var plain = Narrator(new { sections = episode.Parts.Select(p => p.Text) }, out _).EpisodeScript(episode, 20_000);
        Assert.Equal(episode.Parts.Single(p => p.Scene is not null).Scene, plain.Parts.Single(p => p.Scene is not null).Scene);
    }

    [Fact]
    public void APortrait_IsDrawnAnew_AsTheyVisiblyAge_AndOnMajorChanges_NotEachYear()
    {
        var world = TestPeople.World();
        var at31 = TestPeople.Add(world, 1, 31);
        var at38 = TestPeople.Add(world, 1, 38);
        var at41 = TestPeople.Add(world, 1, 41);
        string Picture(Character c) => Likenesses.Ask(world, c.Ref.ToString(), "hand-painted")!.Prompt;
        // The same kind of person: one picture while they look the same age, a new one once they look older.
        Assert.Equal(Picture(at31), Picture(at38));
        Assert.NotEqual(Picture(at38), Picture(at41));

        var key = Likenesses.KeyFor(world, at31.Ref.ToString());
        Assert.Equal(key, Likenesses.KeyFor(world, at31.Ref.ToString()));
        at31.Wounds.Add("a scar across the face");
        var scarred = Likenesses.KeyFor(world, at31.Ref.ToString());
        Assert.NotEqual(key, scarred);
        at31.Role = Role.Knight;
        Assert.NotEqual(scarred, Likenesses.KeyFor(world, at31.Ref.ToString()));
        // How old they look is told in words for their people, never in years (the owner, 2026-10-30): one of a people living four
        // common lives is in their prime at a hundred, while a common person of that age is bent with it.
        Assert.DoesNotMatch("[0-9]", Likenesses.Ask(world, at31.Ref.ToString(), "")!.Prompt);
        var hundred = TestPeople.Add(world, 1, 100);
        Assert.StartsWith("a very old man", Likenesses.Ask(world, hundred.Ref.ToString(), "")!.Prompt);
        var race = new Race { Id = world.Ids.Take(Race.Kind), Name = "Longfolk", Span = 4 };
        world.Races[race.Id] = race;
        world.Cultures[hundred.CultureId].RaceId = race.Id;
        Assert.StartsWith("a man in the prime of life", Peoples.Looks(world, hundred));
    }

    [Fact]
    public void OtherStories_GetTheirScenery_AfterTheFights_FourAtMost()
    {
        var (world, log, _, _) = ADuelFought();
        var town = world.Settlements.Values.First();
        for (var i = 0; i < 6; i++)
            log.Append(world.Date, new EventDraft("FestivalHeld", Place: town.Ref, Importance: 20 + i));
        var episode = Episode.Build(world, log, 0, "2026-10-27", stories: 8, maxCharacters: 4000);
        var painted = episode.Parts.Where(p => p.Scene is not null).ToList();
        Assert.InRange(painted.Count, 2, Stills.PerEpisode);
        Assert.Contains(painted, p => log.All.Single(e => e.Id == p.EventId).Type == "DuelFought");
        Assert.Contains(painted, p => p.Scene!.Contains("medieval fantasy") && !p.Scene.Contains("duellists"));
    }

    [Fact]
    public async Task TheKey_IsCheckedWithoutMakingAnImage_AndSentTrimmed()
    {
        var accepted = new StorageTests.FakeHttp((HttpStatusCode.BadRequest, "{\"error\":\"Unknown scenario\"}"));
        Assert.Equal((true, 400), await new FluxImages(new HttpClient(accepted), "https://flux.example", "SECRET \n").CheckAsync());
        var (request, body) = accepted.Seen.Single();
        // A secret pasted with a trailing newline or space goes out as the key alone.
        Assert.Equal("SECRET", request.Headers.GetValues("x-access-key").Single());
        Assert.Equal("no-such-scenario", (string?)JsonNode.Parse(body!)!["scenario"]);

        var refused = new StorageTests.FakeHttp((HttpStatusCode.Unauthorized, "{\"error\":\"Missing or wrong access key.\"}"));
        Assert.Equal((false, 401), await new FluxImages(new HttpClient(refused), "https://flux.example", "WRONG").CheckAsync());
    }
}

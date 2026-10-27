using FantasyWorld.Broadcast.Channels;
using FantasyWorld.Broadcast.Media;
using FantasyWorld.Broadcast.Speech;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Tests.Support;

namespace FantasyWorld.Sim.Tests.Chronicle;

/// <summary>The face-offs (sub-project 8 B3): declared duels and wars told with both sides' figures and the odds.</summary>
public class FaceOffTests
{
    internal static (World World, EventLog Log, Character Strong, Character Sly, WorldEvent Declared) ADuelDeclared()
    {
        var world = TestPeople.World();
        var log = new EventLog();
        var strong = TestPeople.Add(world, 1, 30, role: Role.Knight);
        strong.Attributes.Prowess = 17;
        strong.Attributes.Martial = 15;
        strong.Attributes.Intrigue = 4;
        var sly = TestPeople.Add(world, 1, 34);
        sly.Attributes.Prowess = 8;
        sly.Attributes.Martial = 6;
        sly.Attributes.Intrigue = 16;
        var declared = log.Append(world.Date, new EventDraft("DuelDeclared",
            Actors: new Dictionary<string, EntityRef> { ["challenger"] = strong.Ref, ["defender"] = sly.Ref },
            Place: world.Settlements[sly.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["stakes"] = "to the death", ["odds"] = "71", ["day"] = "400" },
            Importance: 40, Tags: new[] { "Characters", "Deeds" }));
        return (world, log, strong, sly, declared);
    }

    [Fact]
    public void ADeclaredDuel_IsAFaceOff_WithBothSidesFigures_AfterTheHeadline()
    {
        var (world, log, strong, sly, declared) = ADuelDeclared();
        var episode = Episode.Build(world, log, 0, "2026-10-27");

        var section = Assert.Single(episode.Parts, p => p.Kind == EpisodeSection.Challenge);
        var f = section.FaceOff!;
        Assert.Equal((FaceOff.Duel, declared.Id, "to the death", 71), (f.Kind, f.EventId, f.Stakes, f.OddsA));
        Assert.Equal((strong.Ref.ToString(), sly.Ref.ToString()), (f.A.Ref, f.B.Ref));
        Assert.Equal(17, f.A.Stats.Single(s => s.Name == "Prowess").Value);
        Assert.All(f.A.Stats.Where(s => s.Name != "Renown"), s => Assert.Equal(20, s.Max));
        Assert.Contains("aged 30", f.A.Notes);
        Assert.Equal($"Face-off: {f.A.Name} v {f.B.Name}", section.Title);
        // Told plainly when no chronicler answers: who is stronger at what, and the odds.
        Assert.StartsWith($"The face-off: {f.A.Name} against {f.B.Name}, to the death, at ", section.Text);
        Assert.True(section.Text.Contains($"{f.A.Name} is the stronger in martial and prowess; {f.B.Name} in intrigue"), section.Text);
        Assert.EndsWith($"The odds favour {f.A.Name}, 71 in a hundred.", section.Text);
        // Not told again among the stories, and the episode keeps it through a round trip.
        Assert.DoesNotContain(episode.Stories, s => s.EventId == declared.Id);
        var back = Episode.FromJson(episode.ToJson()).Parts.Single(p => p.Kind == EpisodeSection.Challenge).FaceOff;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(f), System.Text.Json.JsonSerializer.Serialize(back));
        Assert.Contains("Chance of winning:", f.Figures());
        Assert.Contains("prowess 17", f.Figures());
    }

    [Fact]
    public void ADeclaredWar_IsAFaceOff_OfTheTwoRealms_OnOneScale()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        world.Realms[1].Treasury = 900;
        world.Realms[2].Treasury = 300;
        var declared = log.Append(world.Date, new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = world.Realms[1].Ref, ["defender"] = world.Realms[2].Ref },
            Payload: new Dictionary<string, string> { ["war"] = "the War of Ash", ["odds"] = "35", ["attacker strength"] = "120", ["defender strength"] = "200" },
            Importance: 60, Tags: new[] { "War" }));

        var f = FaceOffs.For(world, declared)!;
        Assert.Equal((FaceOff.War, "the War of Ash", 35), (f.Kind, f.Stakes, f.OddsA));
        Assert.Equal((120d, 200d), (f.A.Stats[0].Value, f.B.Stats[0].Value));
        Assert.Equal((200d, 200d), (f.A.Stats[0].Max, f.B.Stats[0].Max));
        Assert.Equal((900d, 900d), (f.A.Stats.Single(s => s.Name == "Treasury").Max, f.B.Stats.Single(s => s.Name == "Treasury").Max));
        Assert.EndsWith($"The odds favour {f.B.Name}, 65 in a hundred.", FaceOffs.Tell(f, "nowhere"));
        Assert.Equal(declared.Id, Episode.Build(world, log, 0, "2026-10-27").Parts.Single(p => p.Kind == EpisodeSection.Challenge).EventId);
    }

    [Fact]
    public void TheChronicler_IsGivenTheFigures_AndMayUseThem()
    {
        var (world, log, _, _, _) = ADuelDeclared();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        var f = episode.Parts.Single(p => p.Kind == EpisodeSection.Challenge).FaceOff!;
        var fake = new FakeLlmProvider(request =>
        {
            // A commentator's answer that cites a figure only the face-off gives (prowess 17), never said in the template.
            var sections = episode.Parts.Select(p => p.Kind == EpisodeSection.Challenge
                ? $"A prowess of 17 against 8: {f.A.Name} hits harder, but {f.B.Name} schemes better. The odds favour {f.A.Name}, 71 in a hundred. " + p.Text
                : p.Text);
            return System.Text.Json.JsonSerializer.Serialize(new { sections });
        });
        var story = new StoryContext(world, log, new LlmGateway(fake, new LlmSettings { Provider = "fake" }, new LlmJournal()));
        var written = new LlmNarrator(story).EpisodeScript(episode, 20_000);

        var prompt = fake.Requests.Single().Prompt;
        Assert.Contains("Face-off figures:", prompt);
        Assert.Contains("prowess 17", prompt);
        Assert.Contains("commentator", prompt);
        Assert.Equal(Episode.ChroniclerWriter, written.Writer);
        Assert.StartsWith("A prowess of 17 against 8", written.Parts.Single(p => p.Kind == EpisodeSection.Challenge).Text);
        Assert.Equal(f, written.Parts.Single(p => p.Kind == EpisodeSection.Challenge).FaceOff);
    }

    [Fact]
    public void TheFilmAndThePost_CarryTheFaceOff()
    {
        var (world, log, _, _, _) = ADuelDeclared();
        var episode = Episode.Build(world, log, 0, "2026-10-27");
        var f = episode.Parts.Single(p => p.Kind == EpisodeSection.Challenge).FaceOff!;
        var timeline = new Timeline(60, episode.Parts.Select((p, i) => new Chapter(p.Title, i * 10)).ToList());
        var shots = DioramaFilm.Timed(episode.Parts.Select(_ => Shot.Whole).ToList(), timeline, episode.Parts);
        var index = episode.Parts.ToList().FindIndex(p => p.Kind == EpisodeSection.Challenge);
        Assert.Equal(f, shots[index].FaceOff);
        Assert.All(shots.Where((_, i) => i != index), s => Assert.Null(s.FaceOff));
        var round = DioramaFilm.FromBytes<List<FilmShot>>(DioramaFilm.ToBytes(shots))!;
        Assert.Equal(f.OddsA, round[index].FaceOff!.OddsA);

        var post = DiscordWebhook.Payload(episode, "The Chronicle").ToJsonString();
        Assert.Contains("Face-offs", post);
        Assert.Contains($"{f.A.Name} v {f.B.Name} (to the death): 71% to 29%", post);
    }
}

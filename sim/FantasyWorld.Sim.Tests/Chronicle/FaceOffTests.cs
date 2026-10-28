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
        Assert.Equal($"{f.A.Name} vs {f.B.Name}", section.Title);
        // Told plainly when no chronicler answers: who is stronger at what, and the odds.
        Assert.StartsWith($"The gauntlet is thrown: {f.A.Name} against {f.B.Name}, to the death, at ", section.Text);
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
        world.Realms[1].Stocks[ResourceKinds.Ore] = 40;
        world.Realms[2].Stocks[ResourceKinds.Horses] = 12;
        world.Realms[2].Conditions["short:ore"] = 1;
        var declared = log.Append(world.Date, new EventDraft("WarDeclared",
            Actors: new Dictionary<string, EntityRef> { ["attacker"] = world.Realms[1].Ref, ["defender"] = world.Realms[2].Ref },
            Payload: new Dictionary<string, string> { ["war"] = "the War of Ash", ["odds"] = "35", ["attacker strength"] = "120", ["defender strength"] = "200" },
            Importance: 60, Tags: new[] { "War" }));

        var f = FaceOffs.For(world, declared)!;
        Assert.Equal((FaceOff.War, "the War of Ash", 35), (f.Kind, f.Stakes, f.OddsA));
        Assert.Equal((120d, 200d), (f.A.Stats[0].Value, f.B.Stats[0].Value));
        Assert.Equal((200d, 200d), (f.A.Stats[0].Max, f.B.Stats[0].Max));
        Assert.Equal((900d, 900d), (f.A.Stats.Single(s => s.Name == "Treasury").Max, f.B.Stats.Single(s => s.Name == "Treasury").Max));
        Assert.Equal((40d, 0d, 40d), (f.A.Stats.Single(s => s.Name == "Ore").Value, f.B.Stats.Single(s => s.Name == "Ore").Value, f.B.Stats.Single(s => s.Name == "Ore").Max));
        Assert.Equal((0d, 12d), (f.A.Stats.Single(s => s.Name == "Horses").Value, f.B.Stats.Single(s => s.Name == "Horses").Value));
        Assert.Contains("short of ore", f.B.Notes);
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
        Assert.Contains("Showdowns", post);
        Assert.Contains($"{f.A.Name} vs {f.B.Name} (to the death): 71% to 29%", post);
    }

    [Fact]
    public void ADuelUnderWay_ShowsItsDay_AndTheOddsOfItsChallenge_AndAFoughtOne_ItsResult()
    {
        var (world, log, strong, sly, declared) = ADuelDeclared();
        var place = world.Settlements[sly.SettlementId].Ref;
        var duel = new Dictionary<string, EntityRef> { ["challenger"] = strong.Ref, ["defender"] = sly.Ref };
        var begun = log.Append(world.Date, new EventDraft("DuelBegun", Actors: duel, Place: place,
            Payload: new Dictionary<string, string> { ["stakes"] = "to the death", ["days"] = "3" },
            Causes: new[] { new Cause(declared.Id, CauseKind.Trigger, 1.0) }, Importance: 30));
        var turned = log.Append(world.Date, new EventDraft("DuelTurned", Actors: duel, Place: place,
            Payload: new Dictionary<string, string> { ["stakes"] = "to the death", ["day"] = "2", ["days"] = "3" },
            Causes: new[] { new Cause(begun.Id, CauseKind.Trigger, 1.0) }, Importance: 20));

        var going = FaceOffs.OfFight(world, log, turned)!;
        Assert.Equal((FaceOff.UnderWay, 71, "day 2 of 3, still undecided"), (going.Stage, going.OddsA, going.Result));
        Assert.Contains("So far: day 2 of 3", going.Figures());

        var fought = log.Append(world.Date, new EventDraft("DuelFought", Actors: duel, Place: place,
            Payload: new Dictionary<string, string> { ["stakes"] = "to the death", ["outcome"] = "defender_kills", ["days"] = "3" },
            Causes: new[] { new Cause(begun.Id, CauseKind.Trigger, 1.0) }, Importance: 40));
        var result = FaceOffs.OfFight(world, log, fought)!;
        Assert.Equal((FaceOff.Fought, 0), (result.Stage, result.OddsA));
        Assert.Equal($"{result.B.Name} won after 3 days; {result.A.Name} was slain.", result.Result);
        Assert.Contains($"Result: {result.B.Name} won", result.Figures());
        Assert.DoesNotContain("Chance of winning", result.Figures());

        // In the episode, the fight's section carries the card of how it ended.
        var episode = Episode.Build(world, log, declared.Id, "2026-10-28");
        var section = Assert.Single(episode.Parts, p => p.FaceOff is { Stage: FaceOff.Fought });
        Assert.Equal(fought.Id, section.FaceOff!.EventId);
    }

    [Fact]
    public void AFoughtBattle_ShowsTheVictor_AndBothSidesLosses()
    {
        var world = TestRealms.TwoKingdoms();
        var log = new EventLog();
        var battle = log.Append(world.Date, new EventDraft("BattleFought",
            Actors: new Dictionary<string, EntityRef> { ["victor"] = world.Realms[2].Ref, ["vanquished"] = world.Realms[1].Ref },
            Place: world.Regions[1].Ref,
            Payload: new Dictionary<string, string> { ["victor losses"] = "120", ["vanquished losses"] = "480" }, Importance: 50, Tags: new[] { "War" }));

        var f = FaceOffs.OfFight(world, log, battle)!;
        Assert.Equal((FaceOff.Fought, 100, world.Realms[2].Name), (f.Stage, f.OddsA, f.A.Name));
        Assert.Equal(("Losses", 120d, 480d), (f.A.Stats[0].Name, f.A.Stats[0].Value, f.B.Stats[0].Value));
        Assert.Equal((480d, 480d), (f.A.Stats[0].Max, f.B.Stats[0].Max));
        Assert.Equal(f.A.Stats.Select(x => x.Name), f.B.Stats.Select(x => x.Name));
        Assert.Equal($"{world.Realms[2].Name} won; 120 of theirs and 480 of {world.Realms[1].Name}'s fell.", f.Result);
    }
}

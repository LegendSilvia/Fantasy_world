using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

/// <summary>One article of the wiki: what a thing is, a few facts, and who or what in the world it names.</summary>
public sealed record WikiEntry(string Name, string Text, IReadOnlyList<string> Facts, IReadOnlyList<Mention> Links);

/// <summary>A section of the wiki: how that part of the world works, then its articles.</summary>
public sealed record WikiCategory(string Key, string Title, string Intro, IReadOnlyList<WikiEntry> Entries);

/// <summary>
/// The world's wiki (the owner, 2026-10-30: "explaining thing like resource, races, class etc pretty much everything
/// introduced in the world separated by categories"): how each part of the world works, with this world's own examples.
/// It shows only what the chronicle has shown by the day asked: a beast nobody has survived, a treasure not yet found, a
/// power not yet awakened stay out (an unfound treasure only as a rumour).
/// </summary>
public sealed record WikiScreen(string World, IReadOnlyList<WikiCategory> Categories)
{
    private static readonly IReadOnlyList<string> None = [];
    private static readonly IReadOnlyList<Mention> Nobody = [];

    public static WikiScreen Build(World world, EventLog log, long? until = null)
    {
        bool Shown(long eventId) => eventId != 0 && log.TryGet(eventId, out var e) && (until is not { } u || e.Date.Day <= u);
        Mention? Who(long id) => world.Characters.TryGetValue(id, out var c) ? new Mention(c.Ref, c.Name) : null;
        static WikiEntry Entry(string name, string text, IEnumerable<string>? facts = null, IEnumerable<Mention?>? links = null) =>
            new(name, text, facts?.ToList() ?? None, links?.OfType<Mention>().ToList() ?? Nobody);

        var categories = new List<WikiCategory>
        {
            Races(world),
            ClassesOf(world, Shown, Who),
            Resources(world, Shown, Who),
            Faiths(world),
            RealmsOf(world),
            Wars(),
            Fights(),
            Beginnings(),
            Changes(),
            Beasts(world, Shown, Who),
            Landmarks(world, Shown),
            Shadow(world, Shown, Who),
            Trade(world),
        };
        return new WikiScreen(world.Name, categories);

        static WikiCategory Races(World world) => new("races", "Races",
            "Every people of this world was made for it. A race leans its people's talents one way, sets how long they live, and "
            + "gives its realms a temper: warlike, pious, arcane, mercantile, wild or stoic. Old hatreds between races are a "
            + "reason for war of their own; old friendships make alliances easier.",
            world.Races.Values.OrderBy(r => r.Id).Select(r => Entry(r.Name, Join(r.Nature, r.Looks.Length > 0 ? $"They look {r.Looks.TrimEnd('.')}." : ""),
                [
                    $"Temper: {r.Temper}",
                    $"Grow old at {Peoples.Lifespan(r.Span).Old}, live to {Peoples.Lifespan(r.Span).Limit} at most",
                    .. Listed("Friends", r.Friends.Select(id => world.Races.GetValueOrDefault(id)?.Name)),
                    .. Listed("Foes", r.Foes.Select(id => world.Races.GetValueOrDefault(id)?.Name)),
                ])).ToList());

        static WikiCategory ClassesOf(World world, Func<long, bool> shown, Func<long, Mention?> who)
        {
            var entries = Classes.All.Select(k => Entry(Classes.Title(k),
                $"Beats the {Classes.Title(Beaten(k)).ToLowerInvariant()}: {Classes.Why(k)}.",
                [$"Beaten by the {Classes.Title(Classes.All.First(o => Classes.Counters(o, k))).ToLowerInvariant()}"])).ToList();
            entries.AddRange(world.Callings.Where(c => shown(c.EventId)).Select(c => Entry(c.Name,
                Join(c.Origin, c.Does),
                [$"Awakened from the {c.Base}", .. c.Condition.Length > 0 ? [$"Condition: {c.Condition}"] : None],
                world.Characters.Values.Where(x => x.Class == Classes.Awaken(c.Base, c.Name)).OrderBy(x => x.Id).Select(x => who(x.Id)))));
            return new("classes", "Classes",
                "Everyone grown takes one of six classes, in two triangles where each beats one and is beaten by another. A class "
                + $"that counters its foe fights {Percent(Classes.CounterEdge)} stronger, in a duel, in a champions' clash and as a "
                + "commander. What befalls a character can awaken them into a new class of their own, named when it happens: it "
                + $"stands where its old class stood on the wheel and fights {Percent(Classes.AwakenedEdge)} stronger.",
                entries);

            static string Beaten(string k) => Classes.All.First(o => Classes.Counters(k, o));
        }

        static WikiCategory Resources(World world, Func<long, bool> shown, Func<long, Mention?> who)
        {
            var entries = new List<WikiEntry>
            {
                Entry("Grain and fish", "The food of a region's towns, from its fields, coasts and lakes. A region with less than a month's food "
                    + "falls into shortage: its people go hungry and die, and its towns stir. A realm in a fresh famine over a good share of "
                    + "its lands may march on a neighbour's fields."),
                Entry("Ore", "Arms the hosts: soldiers in the field use it each month, and raising a host costs it. A realm short of ore fights a quarter weaker."),
                Entry("Horses", "Mount the knights and the hosts. A realm with horses in hand rides: its hosts march faster and strike a tenth harder."),
                Entry("Timber and stone", "Build: each work rising uses them every year, and a realm short of them builds nothing until its stores recover."),
                Entry("Mithril, dragonbone and arcane crystal", "Rare finds in the mountains, the old bone-fields and where the ley runs strong. Worth "
                    + "many times the common goods to a realm's treasury."),
            };
            foreach (var w in world.Wonders)
                entries.Add(!w.Hidden && shown(w.FoundEventId)
                    ? Entry(w.Name, w.What, [$"Its power: {w.Power}", .. world.Regions.TryGetValue(w.RegionId, out var at) ? [$"Found in {at.Name}"] : None],
                        [who(w.FinderId)])
                    : Entry("An unfound treasure", "The songs speak of a treasure of this world no one has found yet. It will be found only when a secret of the world is uncovered.",
                        [$"Its power, the songs say: {w.Power}"]));
            return new("resources", "Resources",
                "Each realm gathers what its lands yield into stores that keep two years of it. Plenty steadies a realm; every "
                + "shortage unsettles it and stirs its towns. Besides the common goods, every world has one to three treasures of its "
                + "own, each with a part to play in the story.",
                entries);
        }

        static WikiCategory Faiths(World world) => new("faiths", "Gods and faiths",
            "Each faith has its god and its tenets, which lean its realms toward war, trade or peace. A zealous faith may proclaim a "
            + "holy war, and one that loses its holy land wants it back. The viewers are the gods: the world's people pray to them "
            + "before their fights, and the gods may answer.",
            world.Faiths.Values.OrderBy(f => f.Id).Select(f => Entry(f.Name, $"The faith of {f.Deity}.",
                [.. Listed("Tenets", f.Tenets), .. world.Regions.TryGetValue(f.HolyRegionId, out var holy) ? [$"Holy land: {holy.Name}"] : None])).ToList());

        static WikiCategory RealmsOf(World world)
        {
            var entries = new List<WikiEntry>
            {
                Entry("Rulers and councils", "A realm is ruled by its sovereign, advised by a council (a marshal for war and others), and passed down by succession."),
                Entry("Treaties", "Realms make alliances, pacts of non-aggression, trade pacts and tribute, and declare rivalries. Allies may join each other's wars."),
                Entry("Revolutions", "A realm whose people are unhappy enough may rise under a leader of their own. A revolution is crushed, breaks away as a new realm, or overthrows the crown."),
                Entry("Festivals", "Each realm keeps a festival of its own, in its own month, with its own rite."),
            };
            entries.AddRange(Model.Realms.Independent(world).Where(r => r.Festival is not null).OrderBy(r => r.Id)
                .Select(r => Entry(r.Festival!.Name, r.Festival.Rite, [$"Kept by {r.Name}", $"Honours {r.Festival.Honours}"], [new Mention(r.Ref, r.Name)])));
            return new("realms", "Realms and rule",
                "The world's realms hold its lands and towns. Each has a ruler, a council, a treasury and stores, and a temper from its people.",
                entries);
        }

        static WikiCategory Wars() => new("war", "War",
            "A realm declares war at the year's end and fights it the next year, so everyone knows what is coming. Its hosts muster, "
            + "march one move at a time (mountains bar the way), besiege towns and meet in battle. A war ends in a peace that may "
            + "cede lands or lay tribute. Every war has a reason:",
            new[]
            {
                CasusBelli.Claim, CasusBelli.HolyWar, CasusBelli.Revenge, CasusBelli.Reconquest, CasusBelli.ResourceNeed, CasusBelli.Covet,
                CasusBelli.ArtifactRecovery, CasusBelli.Independence, CasusBelli.BorderDispute, CasusBelli.Glory, CasusBelli.Revolution,
                CasusBelli.AncientHatred, CasusBelli.Opportunity, CasusBelli.DarkConquest,
            }.Select(k => Entry(Capital(k), $"War declared {English.CasusBelli(k)}.")).ToList());

        static WikiCategory Fights() => new("fights", "Fights",
            "Characters fight one another as well as realms. Each fight is shown as a face-off card with the two sides and their odds.",
            [
                Entry("Duel to the death", "A challenge given and taken, with a day set the next year. One of them will not walk away."),
                Entry("Duel to first blood", "A challenge for honour, with a day set. The first wound ends it."),
                Entry("Encounter", "Enemies who meet by chance and fight at once. No day is set; the clash goes on, and its outcome is decided the next year."),
                Entry("Ambush", $"One lies in wait for the other. The ambusher fights {Percent(DeedsSystem.Edge(DeedsSystem.Ambush))} stronger; the outcome is decided the next year."),
                Entry("Planned attack", $"A strike prepared in secret. The attacker fights {Percent(DeedsSystem.Edge(DeedsSystem.Attack))} stronger; the outcome is decided the next year."),
                Entry("Weapons and companions", "Fighters may carry a named weapon or bring a companion, a person or a beast, which tips a fight their way."),
            ]);

        static WikiCategory Beginnings() => new("beginnings", "Beginnings",
            "Three to six times a year, a chain of history begins: an event with a cause, and often a culprit, sometimes hidden until they are unmasked years later. What follows grows from it.",
            [
                Entry("Council", "A realm's council meets and decides something that will change the world."),
                Entry("Loss", "A realm's stores or riches are lost, and it must answer the want."),
                Entry("Meeting", "Conspirators meet in secret about something they want."),
                Entry("Plot", "A plan against someone's life, carried out or foiled the next year."),
                Entry("Outbreak", "A labyrinth spills its beasts into the land."),
                Entry("Theft", "A treasure is stolen."),
                Entry("Abduction", "Someone is carried off."),
                Entry("Blight", "A sickness falls on a land's fields or people."),
                Entry("Forgery", "False letters or seals set people against each other."),
                Entry("Insult", "An open slight that cannot be forgotten."),
                Entry("Sedition", "Talk of rising against the crown spreads."),
                Entry("Sacrilege", "A holy place is defiled."),
                Entry("Raid", "A band strikes across a border."),
                Entry("Quest", "Someone swears to win a great prize, such as the treasure at the bottom of a labyrinth."),
                Entry("Pretender", "Someone claims a throne that is not theirs."),
                Entry("Envoy", "An embassy is sent, and what becomes of it matters."),
                Entry("Betrayal", "A friend or ally turns."),
                Entry("Lovers", "A forbidden love that others will not allow."),
                Entry("Framed", "Someone is blamed for what another did."),
            ]);

        static WikiCategory Changes() => new("changes", "How people change",
            "What befalls a character can change them: coming back from the brink of death, slaying a legend, uncovering a secret, killing a foe, or a great loss.",
            [
                Entry("Vengeance", "Sworn to revenge on the one who nearly killed them or took someone from them."),
                Entry("A new desire", "Stirred to want something they never wanted before."),
                Entry("A fighting art", "They make an ability of their own."),
                Entry("Awakening", "They grow into a new class, named when it happens. Some awakenings sleep for years before they show."),
                Entry("Forgot who they were", "A blow or a fever takes their memory, and their old hatreds and longings go with it. They take a new name."),
                Entry("Forgotten by the world", "A curse, a bargain with a god or a spell gone wrong makes everyone forget them. They take a new name."),
                Entry("Hidden", "They cast off their name to hide from their enemies."),
                Entry("Reborn", "Grief makes them over, and they take a new name for the life that is left."),
                Entry("Connections", "Everyone has ties: kin, friends, rivals, lovers, sworn companions. Each tie has a bond, from bitter hatred to a friendship that would die for them."),
            ]);

        static WikiCategory Beasts(World world, Func<long, bool> shown, Func<long, Mention?> who)
        {
            var seen = world.Monsters.Values.Where(m => shown(m.RevealedEventId)).OrderByDescending(m => m.Legendary).ThenByDescending(m => m.Might).ThenBy(m => m.Id)
                .Select(m => Entry(m.Name, $"{English.Capitalise(English.WithArticle(m.Breed))}{(m.Legendary ? " of legend" : "")}.",
                    [
                        m.State == MonsterState.Slain && shown(m.SlainEventId) ? "Slain" : "Still at large",
                        .. world.Regions.TryGetValue(m.RegionId, out var at) ? [$"Seen in {at.Name}"] : None,
                    ],
                    [new Mention(m.Ref, m.Name), who(m.RevealedById), m.SlayerId != 0 && shown(m.SlainEventId) ? who(m.SlayerId) : null]));
            var entries = new List<WikiEntry>
            {
                Entry("Labyrinths", "Deep places full of beasts and treasure. Delvers go down level by level. Clearing one uncovers a secret of the world, and an open labyrinth may spill its beasts."),
                Entry("Relics", "Named treasures with their own story: won in labyrinths, handed down, stolen, and fought over."),
            };
            entries.AddRange(seen);
            return new("beasts", "Beasts and labyrinths",
                "Beasts roam the wild lands and the seas. No one knows a beast is there until someone faces it and lives to tell. "
                + "Only those beasts are listed here.",
                entries);
        }

        static WikiCategory Landmarks(World world, Func<long, bool> shown)
        {
            var standing = world.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State == StructureState.Standing && shown(s.CompletedEventId)).ToList();
            string Built(string purpose) => standing.Count(s => s.Purpose == purpose) is var n and > 0 ? $"{n} standing" : "None standing yet";
            return new("landmarks", "Landmarks",
                "Towns raise landmarks, and what each is for matters.",
                [
                    Entry("Memorial", "Raised for a war or a great death: a monument, a hall of the fallen, an arch of victory.", [Built(StructureSystem.Memorial)]),
                    Entry("Temple", "Raised for a faith; it stirs the zeal of the faithful.", [Built(StructureSystem.Temple)]),
                    Entry("Wonder", "A colossus, a great lighthouse, a grand market. Travellers come to see it, and it brings the town riches.", [Built(StructureSystem.Wonder)]),
                    Entry("Beauty", "Gardens, fountains, bridges: raised for nothing but beauty, and the people are happier for it.", [Built(StructureSystem.Beauty)]),
                    Entry("War engine", "Purely for war: a Great Arcane Cannon, a storm ballista, a dragonfire bastion. It makes the realm's hosts hit harder.", [Built(StructureSystem.Engine)]),
                ]);
        }

        static WikiCategory Shadow(World world, Func<long, bool> shown, Func<long, Mention?> who)
        {
            var saga = world.Sagas.Values.OrderByDescending(s => s.Id).FirstOrDefault();
            return new("shadow", "Heroes and the demon lord",
                "World events, the greatest of all. A demon lord may rise and wage war to swallow the living lands. Heroes are called to face them. "
                + "The demon lord can be slain, or sealed away, but only by one who holds the treasure that can seal it.",
                [
                    Entry("The demon lord", "A warlock of terrible power who gathers the dark and makes war on every realm.",
                        saga is null ? ["None has risen yet"] : [saga.Active ? "Abroad in the world" : $"Ended: {saga.Outcome}"],
                        saga is null ? null : [who(saga.DemonLordId)]),
                    Entry("Heroes", "Those the world calls to face the demon lord. A hero who falls may be followed by another.",
                        links: saga is { HeroId: not 0 } ? [who(saga.HeroId)] : null),
                ]);
        }

        static WikiCategory Trade(World world) => new("trade", "Lands, seas and trade",
            "The world is a few continents with sea between them. Towns trade along roads and, from their ports, across the sea.",
            [
                Entry("Continents", "Great lands parted by sea. Each has its own beasts.", [$"{world.Continents.Count} in this world"]),
                Entry("Trade routes", "Roads between towns that carry goods and riches. War cuts them."),
                Entry("Sea routes", "Lanes between ports, carrying more than any road. A sea beast on the way closes the lane."),
                Entry("Regions", "Every land has its biome, fertility, deposits and the strength of its ley. Drought, blight, flood and plague come and go."),
            ]);
    }

    private static IEnumerable<string> Listed(string label, IEnumerable<string?> names) =>
        names.OfType<string>().ToList() is { Count: > 0 } list ? [$"{label}: {string.Join(", ", list)}"] : [];

    private static string Join(params string[] parts) => string.Join(" ", parts.Where(p => p.Length > 0));

    private static string Percent(double edge) => $"{(int)Math.Round((edge - 1) * 100)}%";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

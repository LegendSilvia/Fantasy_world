using System.Globalization;
using System.Numerics;
using System.Text.Json;
using FantasyWorld.Client;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.Views;
using Microsoft.JSInterop;

namespace FantasyWorld.Web;

/// <summary>
/// What the page asks of the world (sub-project 11 W2): it loads the day's bundle once, then asks for screens (the same
/// view JSON the game reads) and for the diorama's geometry (the same meshes the game draws), in block space: x and z
/// across the map from −1 to 1, y up. Meshes come packed (see <see cref="Pack"/>); everything else is JSON. Read-only.
/// </summary>
public static class Viewer
{
    /// <summary>At most this many companions in the lore: the story's own, not every beast.</summary>
    public const int MaxCompanions = 8;

    private static World? _world;
    private static EventLog? _log;

    private static World World => _world ?? throw new InvalidOperationException("Load the world first.");
    private static EventLog Log => _log ?? throw new InvalidOperationException("Load the world first.");
    private static Geography Geography => World.Geography ?? throw new InvalidOperationException("This world has no geography.");

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Loads the gzipped bundle; answers the world's name, its first and current year, and today's date.</summary>
    [JSInvokable]
    public static string Load(byte[] gzip)
    {
        (_world, _log) = WebBundle.FromGzip(gzip).Load();
        return JsonSerializer.Serialize(new { name = DigestScreen.DisplayName(World), year = World.Date.Year, firstYear = FirstYear, today = World.Date.ToString() }, Json);
    }

    /// <summary>
    /// A screen's JSON, as the game's views make it: <c>map</c> (arg: a year, or empty for today), <c>character</c>,
    /// <c>family</c>, <c>realm</c>, <c>artifact</c> (arg: an id), <c>why</c> (arg: an event id), <c>chronicle</c> (arg: a year, or empty).
    /// </summary>
    /// <summary>
    /// What the players may answer today (sub-project 13 N5): the open prayers (who prays, to whom, for what, told as the
    /// chronicle tells it) and the face-offs still to be fought, with both sides, as JSON.
    /// </summary>
    [JSInvokable]
    public static string Prayers()
    {
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(World);
        var prayers = World.Prayers.Values.Where(p => p.IsOpen && Log.TryGet(p.EventId, out _)).OrderByDescending(p => p.Day).ThenBy(p => p.Id)
            .Select(p => new
            {
                id = p.Id,
                plea = p.Plea,
                god = World.Faiths.GetValueOrDefault(p.FaithId)?.Deity,
                text = narrator.Sentence(Log.Get(p.EventId)),
                supplicant = World.Characters[p.SupplicantId].Ref.ToString(),
                year = new WorldDate(p.Day).Year,
            });
        var faceOffs = PrayerSystem.Blessable(World, Log).Select(e => FantasyWorld.Sim.Chronicle.FaceOffs.For(World, e)).OfType<FantasyWorld.Sim.Chronicle.FaceOff>()
            .Select(f => new
            {
                id = f.EventId,
                kind = f.Kind,
                stakes = f.Stakes,
                odds = f.OddsA,
                a = new { name = f.A.Name, colour = V3(WarLayout.Colour(f.A.Colour)), @class = f.A.Class },
                b = new { name = f.B.Name, colour = V3(WarLayout.Colour(f.B.Colour)), @class = f.B.Class },
            });
        return JsonSerializer.Serialize(new { prayers, faceOffs }, Json);
        static float[] V3(Vector3 v) => [v.X, v.Y, v.Z];
    }

    /// <summary>
    /// The world's lore (the owner, 2026-10-29): its legend as its people tell it, and the secrets deeds have uncovered so far,
    /// newest first, each with who uncovered it, where and when.
    /// </summary>
    [JSInvokable]
    public static string Lore(string year = "")
    {
        var until = Until(year);
        var secrets = World.Secrets.AsEnumerable().Reverse().Where(s => By(until, s.Day)).Select(s => new
        {
            title = s.Title,
            text = s.Text,
            year = new WorldDate(s.Day).Year,
            @event = $"event#{s.EventId}",
            place = World.Regions.GetValueOrDefault(s.RegionId)?.Name,
            by = World.Characters.TryGetValue(s.RevealerId, out var c) ? new { @ref = c.Ref.ToString(), name = c.Name } : null,
        });
        // Everything else the chronicle knows (the owner, 2026-10-29): its peoples, gods, realms, labyrinths, relics and the shadow.
        object? Who(long id) => World.Characters.TryGetValue(id, out var c) ? new { @ref = c.Ref.ToString(), name = c.Name } : null;
        var races = World.Races.Values.OrderBy(r => r.Id).Select(r => new
        {
            name = r.Name,
            looks = r.Looks,
            nature = r.Nature,
            temper = r.Temper,
            lifespan = new { old = Peoples.Lifespan(r.Span).Old, limit = Peoples.Lifespan(r.Span).Limit },
            foes = r.Foes.Select(id => World.Races.GetValueOrDefault(id)?.Name).OfType<string>(),
            friends = r.Friends.Select(id => World.Races.GetValueOrDefault(id)?.Name).OfType<string>(),
        });
        var gods = World.Faiths.Values.OrderBy(f => f.Id).Select(f => new { deity = f.Deity, faith = f.Name, tenets = f.Tenets });
        var realms = Realms.Independent(World).Select(r => (Realm: r, Lands: World.Regions.Values.Count(g => g.OwnerRealmId == r.Id)))
            .Where(r => r.Lands > 0).OrderByDescending(r => r.Lands).ThenBy(r => r.Realm.Id).Select(r => new
            {
                @ref = r.Realm.Ref.ToString(),
                title = Statecraft.Title(r.Realm),
                lands = r.Lands,
                ruler = Realms.RulerOf(World, r.Realm) is { } ruler ? Who(ruler.Id) : null,
                festival = r.Realm.Festival is { } f ? new { name = f.Name, rite = f.Rite } : null,
            });
        var labyrinths = World.Structures.Values.Where(s => s.Type == StructureKind.Labyrinth || s.ClearedEventId != 0).OrderBy(s => s.ClearedEventId == 0).ThenBy(s => s.Name)
            .Select(s => new
            {
                name = s.Name,
                place = World.Regions.GetValueOrDefault(s.RegionId)?.Name,
                levels = s.Levels,
                depth = s.Depth,
                cleared = s.ClearedEventId != 0 && Log.TryGet(s.ClearedEventId, out var c) && By(until, c.Date.Day)
                    ? new { year = c.Date.Year, by = c.Actors.TryGetValue("delver", out var d) ? Who(d.Id) : null, @event = $"event#{c.Id}" } : null,
            });
        var relics = World.Artifacts.Values.Where(a => a.Place != ArtifactPlace.Destroyed).OrderByDescending(a => a.Fame).ThenBy(a => a.Id).Take(15).Select(a => new
        {
            @ref = a.Ref.ToString(),
            name = a.Name,
            lore = a.Lore,
            where = a.Place.ToString().ToLowerInvariant(),
            holder = a.Place == ArtifactPlace.Held ? Who(a.HolderId) : null,
        });
        // The powers awakened in the world (the owner, 2026-10-29): where each comes from, what it does, its condition, who bears it.
        var callings = World.Callings.Where(k => EventBy(until, k.EventId)).Select(k => new
        {
            name = k.Name,
            @base = k.Base,
            origin = k.Origin,
            does = k.Does,
            condition = k.Condition,
            @event = k.EventId != 0 ? $"event#{k.EventId}" : null,
            bearers = World.Characters.Values.Where(c => c.Class == Classes.Awaken(k.Base, k.Name))
                .OrderByDescending(c => c.IsActive).ThenBy(c => c.Id).Select(c => new { @ref = c.Ref.ToString(), name = c.Name, living = c.IsActive }),
        });
        // The beasts of the story (the owner, 2026-10-29: the lore is no register of every pet): those at the side of the
        // story's own (FantasyWorld.Sim.Systems.Prominence) or born of a beast their keeper slew, most notable first. Every
        // companion still shows on its keeper's own page.
        var companions = World.Characters.Values
            .Where(c => c.IsActive && c.Companion is { Kind: Companion.Beast } m && By(until, m.Day) && (m.What.StartsWith("the young of ", StringComparison.Ordinal)
                                                                                     || FantasyWorld.Sim.Systems.Prominence.IsNotable(World, c)))
            .OrderByDescending(c => FantasyWorld.Sim.Systems.Prominence.Of(World, c)).ThenBy(c => c.Id).Take(MaxCompanions)
            .Select(c => new { name = c.Companion!.Name, what = c.Companion.What, lore = c.Companion.Lore, of = new { @ref = c.Ref.ToString(), name = c.Name } });
        // What the songs tell as of the year shown (the owner, 2026-10-29: the lore revised every 25 years): tales sung by
        // then and not yet forgotten.
        var thenYear = until is { } cut ? new WorldDate(cut).Year : World.Date.Year;
        var tales = World.Tales.Where(t => t.SungFrom <= thenYear && (!t.Forgotten || t.ForgottenIn > thenYear))
            .OrderByDescending(t => t.Year).Select(t => new { title = t.Title, text = t.Text, year = t.Year, @event = $"event#{t.EventId}", since = t.SungFrom });
        // The towns' landmarks (the owner, 2026-10-29): what each is for, and what it was raised for.
        var landmarks = World.Structures.Values.Where(s => s.Type == StructureKind.Landmark && s.State == StructureState.Standing && EventBy(until, s.CompletedEventId))
            .OrderBy(s => s.Name).Select(s => new
            {
                name = s.Name,
                purpose = s.Purpose,
                town = World.Settlements.GetValueOrDefault(s.SettlementId)?.Name,
                @for = s.ForEventId != 0 && EventBy(until, s.ForEventId) ? $"event#{s.ForEventId}" : null,
            });
        // The world's own resources (the owner, 2026-10-29): found ones with their story and who found them; the hidden only as a rumour.
        var wonders = World.Wonders.Select(w => !w.Hidden && EventBy(until, w.FoundEventId)
            ? (object)new
            {
                name = w.Name,
                what = w.What,
                power = w.Power,
                place = World.Regions.GetValueOrDefault(w.RegionId)?.Name,
                by = Who(w.FinderId),
                @event = w.FoundEventId != 0 ? $"event#{w.FoundEventId}" : null
            }
            : new { name = (string?)null, what = (string?)null, power = w.Power, place = (string?)null, by = (object?)null, @event = (string?)null });
        var shadow = World.Sagas.Values.OrderByDescending(s => s.Id).Select(s => new
        {
            demon = Who(s.DemonLordId),
            active = s.Active,
            outcome = s.Outcome,
            hero = s.HeroId != 0 ? Who(s.HeroId) : null,
        }).FirstOrDefault();
        return JsonSerializer.Serialize(new
        {
            name = World.Name,
            legend = World.Legend?.Full,
            summary = World.Legend?.Summary,
            secrets,
            tales,
            races,
            gods,
            realms,
            labyrinths,
            relics,
            shadow,
            callings,
            companions,
            wonders,
            landmarks,
        }, Json);
    }

    /// <summary>
    /// The people of the world (the owner, 2026-10-29): the living notables, most renowned first, each with their class, role,
    /// age, realm and home, for the website's list; a click opens their page.
    /// </summary>
    [JSInvokable]
    public static string People(string year = "")
    {
        var until = Until(year);
        var then = until is { } u ? new WorldDate(u) : World.Date;
        // Those living then (born by then, not yet dead, nor faded from the story today), and the dead by then (the owner,
        // 2026-10-29: a filter for the living or the dead), each grown before they were gone.
        bool Living(Character c) => until is null ? c.IsActive : c.Died is null || c.Died.Value.Day > then.Day;
        bool DeadBy(Character c) => c.Died is { } d && d.Day <= then.Day;
        bool Grown(Character c) => ((DeadBy(c) ? c.Died!.Value.Day : then.Day) - c.BirthDay) / WorldDate.DaysPerYear >= Character.AdultAge;
        var born = World.Characters.Values.Where(c => c.BirthDay <= then.Day && Grown(c)).ToList();
        var people = born.Where(Living).OrderByDescending(c => c.Renown).ThenBy(c => c.Id).Take(400)
            .Concat(born.Where(DeadBy).OrderByDescending(c => c.Renown).ThenBy(c => c.Id).Take(300))
            .Select(c =>
            {
                var home = World.Settlements.GetValueOrDefault(c.SettlementId);
                var realm = home is not null && World.Regions.TryGetValue(home.RegionId, out var region) ? World.Realms.GetValueOrDefault(region.OwnerRealmId) : null;
                return new
                {
                    @ref = c.Ref.ToString(),
                    name = c.Name,
                    @class = Classes.Base(c.Class),
                    title = c.Class.Length > 0 ? Classes.Title(c.Class) : "",
                    role = c.Role.ToString().ToLowerInvariant(),
                    alive = !DeadBy(c),
                    died = DeadBy(c) ? c.Died!.Value.Year : (int?)null,
                    age = c.Age(DeadBy(c) ? c.Died!.Value : then),
                    renown = (int)Math.Round(c.Renown),
                    realm = realm?.Name,
                    home = home?.Name,
                };
            });
        return JsonSerializer.Serialize(new { people }, Json);
    }

    /// <summary>
    /// A fight on the map as a face-off card (the owner, 2026-10-29): both sides with their figures, the odds at the challenge
    /// or the result and each side's fate; null where the event is no fight.
    /// </summary>
    [JSInvokable]
    public static string Fight(string eventId)
    {
        var id = long.Parse(eventId, CultureInfo.InvariantCulture);
        if (!Log.TryGet(id, out var ev)) return "null";
        var f = FantasyWorld.Sim.Chronicle.FaceOffs.OfFight(World, Log, ev) ?? FantasyWorld.Sim.Chronicle.FaceOffs.For(World, ev);
        if (f is null) return "null";
        object Side(FantasyWorld.Sim.Chronicle.FaceOffSide side) => new
        {
            @ref = side.Ref,
            name = side.Name,
            colour = V3(WarLayout.Colour(side.Colour)),
            @class = side.Class,
            fate = side.Fate,
            notes = side.Notes,
            stats = side.Stats.Select(x => new { name = x.Name, value = x.Value, max = x.Max, shown = FantasyWorld.Sim.Chronicle.FaceOffStat.Compact(x.Value) }),
        };
        return JsonSerializer.Serialize(new
        {
            kind = f.Kind,
            stage = f.Stage,
            stakes = f.Stakes,
            odds = f.OddsA,
            result = f.Result,
            words = f.Odds(),
            a = Side(f.A),
            b = Side(f.B),
        }, Json);
        static float[] V3(Vector3 v) => [v.X, v.Y, v.Z];
    }

    /// <summary>
    /// How a character fights and what they are known for (the owner, 2026-10-29): their class (awakened ones by their own
    /// name), talents, abilities (the signature one, then those won since), and their renown in the world.
    /// </summary>
    [JSInvokable]
    public static string Fighter(string characterId, string year = "")
    {
        var id = long.Parse(characterId, CultureInfo.InvariantCulture);
        if (!World.Characters.TryGetValue(id, out var c)) return "null";
        var until = Until(year);
        // An awakening not yet come shows the class they were born to.
        var awoken = World.Callings.FirstOrDefault(k => Classes.Awaken(k.Base, k.Name) == c.Class);
        var calling = awoken is not null && !EventBy(until, awoken.EventId) ? Classes.Base(Classes.Of(World, c)) : Classes.Of(World, c);
        var reputation = until is null ? _reputation ??= FantasyWorld.Sim.Chronicle.Reputation.Of(World, Log)
            : FantasyWorld.Sim.Chronicle.Reputation.Of(World, LogUntil(until));
        return JsonSerializer.Serialize(new
        {
            @class = Classes.Base(calling),
            title = Classes.Title(calling),
            awakened = Classes.IsAwakened(calling),
            talents = Talents.Of(c).Select(t => new { name = t.Name, shows = t.Shows }),
            abilities = Abilities.Of(World, c).Where(a => EventBy(until, a.EventId)).Select(a => new { name = a.Name, text = a.Text, @event = a.EventId != 0 ? $"event#{a.EventId}" : null }),
            known = reputation.Of(c.Id),
            weapon = Arms.Weapon(World, c) is var w ? new { name = w.Name, what = w.What, lore = w.Lore, @ref = w.ArtifactId != 0 ? $"artifact#{w.ArtifactId}" : null } : null,
            companion = c.Companion is { } m && By(until, m.Day)
                ? new { name = m.Name, kind = m.Kind, what = m.What, lore = m.Lore, @ref = m.CharacterId != 0 ? $"char#{m.CharacterId}" : null, @event = m.EventId != 0 ? $"event#{m.EventId}" : null }
                : null,
            calling = awoken is { } k && EventBy(until, k.EventId)
                ? new { name = k.Name, origin = k.Origin, does = k.Does, condition = k.Condition } : null,
        }, Json);
    }

    private static FantasyWorld.Sim.Chronicle.Reputation? _reputation;

    /// <summary>A beast's card (the owner, 2026-10-29): what it is, its legend, where it lairs, and its deeds, newest first.</summary>
    [JSInvokable]
    public static string Beast(string monsterId, string year = "")
    {
        var id = long.Parse(monsterId, CultureInfo.InvariantCulture);
        // Unseen, the audience knows nothing of it (the owner, 2026-10-29).
        if (!World.Monsters.TryGetValue(id, out var m) || !m.Revealed) return "null";
        var until = Until(year);
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(World);
        var reference = m.Ref;
        // Its end, if it came after the year shown, has not come yet.
        var slain = World.Characters.ContainsKey(m.SlayerId) && Log.All.LastOrDefault(e => e.Type == "MonsterSlain" && e.Actors.Values.Contains(reference)) is { } end
            ? By(until, end.Date.Day) : until is null;
        var deeds = Log.All.Where(e => e.Actors.Values.Contains(reference) && By(until, e.Date.Day) && e.Date.Year >= FirstYear).OrderByDescending(e => e.Id).Take(12)
            .Select(e => new { @event = $"event#{e.Id}", year = e.Date.Year, text = narrator.Sentence(e) });
        var lore = Log.TryGet(m.RoseEventId, out var rose) ? rose.Payload.GetValueOrDefault("lore") : null;
        // Unseen until someone faces it and lives (the owner, 2026-10-29): no name, no kind, no legend, no picture.
        var seen = Log.TryGet(m.RevealedEventId, out var witnessed) && By(until, witnessed.Date.Day);
        if (!seen) return "null";
        var witness = seen && World.Characters.TryGetValue(m.RevealedById, out var w) ? new { @ref = w.Ref.ToString(), name = w.Name } : null;
        return JsonSerializer.Serialize(new
        {
            @ref = reference.ToString(),
            name = m.Name,
            breed = m.Breed,
            witness,
            legendary = m.Legendary,
            might = (int)Math.Round(m.Might),
            notoriety = (int)Math.Round(m.Notoriety),
            state = m.State == MonsterState.Slain && !slain ? "roaming" : m.State.ToString().ToLowerInvariant(),
            place = World.Regions.GetValueOrDefault(m.RegionId)?.Name,
            lore,
            risen = m.RoseEventId,
            slayer = slain && World.Characters.TryGetValue(m.SlayerId, out var slayer) ? new { @ref = slayer.Ref.ToString(), name = slayer.Name } : null,
            deeds,
        }, Json);
    }

    /// <summary>The world's wiki (the owner, 2026-10-30): how each part of the world works, by category, as of the year shown.</summary>
    [JSInvokable]
    public static string Wiki(string year = "") => ViewJson.Serialize(WikiScreen.Build(World, Log, Until(year)));

    /// <summary>The world events (the owner, 2026-10-30): the sagas of the demon lord and the heroes, as of the year shown.</summary>
    [JSInvokable]
    public static string WorldEvents(string year = "") => ViewJson.Serialize(WorldEventsScreen.Build(World, Log, Until(year)));

    [JSInvokable]
    public static string Screen(string kind, string arg)
    {
        long Id() => long.Parse(arg, CultureInfo.InvariantCulture);
        return kind switch
        {
            "map" => ViewJson.Serialize(MapAt(arg)),
            "character" => ViewJson.Serialize(CharacterScreen.Build(World, Log, Id())),
            "family" => ViewJson.Serialize(FamilyTree.Build(World, Id())),
            "realm" => ViewJson.Serialize(RealmScreen.Build(World, Log, Id())),
            "artifact" => ViewJson.Serialize(ArtifactScreen.Build(World, Log, Id())),
            "why" => ViewJson.Serialize(WhyScreen.Build(World, Log, Id())),
            "chronicle" => ViewJson.Serialize(int.TryParse(arg, out var y) ? ChronicleScreen.Build(World, Log, y, y) : ChronicleScreen.Build(World, Log)),
            _ => throw new ArgumentException($"No screen '{kind}'."),
        };
    }

    /// <summary>The map at the end of a year, as the game's history slider shows it (empty or this year: today).</summary>
    private static MapScreen MapAt(string year) =>
        MapScreen.Build(World, Log, int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y >= 1 && y < World.Date.Year
            ? new WorldDate((long)Math.Max(y, FirstYear) * WorldDate.DaysPerYear - 1) : null);

    /// <summary>
    /// The last day of a year the history slider shows, or null for today (empty, or this year or later): every panel shows
    /// the world as it stood then (the owner, 2026-10-29: the year filters everything).
    /// </summary>
    private static long? Until(string? year) =>
        int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y >= 1 && y < World.Date.Year
            ? (long)Math.Max(y, FirstYear) * WorldDate.DaysPerYear - 1 : null;

    /// <summary>
    /// The earliest year a viewer may look back to (the owner, 2026-10-29): the chronicle begins at Year 100, so the offline
    /// history before it is never shown as a year of its own (a world younger than that shows from its first year).
    /// </summary>
    public const int ChronicleYear = 100;

    private static int FirstYear => Math.Min(ChronicleYear, World.Date.Year);

    private static (long Day, EventLog Log)? _pastLog;

    /// <summary>The chronicle as it stood at the end of that day: its events up to then (kept for the last day asked).</summary>
    private static EventLog LogUntil(long? day)
    {
        if (day is not { } until) return Log;
        if (_pastLog is { } kept && kept.Day == until) return kept.Log;
        var log = new EventLog();
        foreach (var e in Log.All)
        {
            if (e.Date.Day > until) break;
            log.Restore(e);
        }
        _pastLog = (until, log);
        return log;
    }

    private static bool By(long? until, long day) => until is not { } u || day <= u;

    private static bool EventBy(long? until, long eventId) => until is null || eventId == 0 || Log.TryGet(eventId, out var e) && e.Date.Day <= until;

    /// <summary>
    /// What happened in a year, for the Play button (the owner, 2026-10-29): its most important events, in the order they
    /// happened, each told with its links.
    /// </summary>
    [JSInvokable]
    public static string Happenings(string year)
    {
        var y = Math.Max(int.Parse(year, NumberStyles.Integer, CultureInfo.InvariantCulture), FirstYear);
        var narrator = new FantasyWorld.Sim.Narration.TemplateNarrator(World);
        var nameOf = FantasyWorld.Sim.Query.Names.Of(World);
        var none = new HashSet<EntityRef>();
        var rows = Log.All.Where(e => e.Date.Year == y && !Bestiary.Hidden(World, e))
            .Select(e => (Event: e, Score: FantasyWorld.Sim.Chronicle.Importance.Score(e, World, none)))
            .Where(x => x.Score >= 25).OrderByDescending(x => x.Score).ThenBy(x => x.Event.Id).Take(MaxHappenings)
            .OrderBy(x => x.Event.Id)
            .Select(x => new { eventId = x.Event.Id, season = x.Event.Date.Season.ToString(), text = LinkedText.Link(narrator.Sentence(x.Event), LinkedText.Mentions(x.Event), nameOf) })
            .ToList();
        return ViewJson.Serialize(new { year = y, rows });
    }

    /// <summary>At most this many of a year's events are played.</summary>
    public const int MaxHappenings = 10;

    /// <summary>The terrain block's parts as packed meshes: <c>top</c>, <c>sides</c> or <c>water</c>.</summary>
    [JSInvokable]
    public static byte[] Block(string part, int resolution)
    {
        var grid = Geography.Terrain;
        return Pack(part switch
        {
            "top" => BlockMesh.Top(grid, resolution),
            "sides" => BlockMesh.Sides(grid, resolution),
            "water" => BlockMesh.Water(grid),
            _ => throw new ArgumentException($"No block part '{part}'."),
        });
    }

    /// <summary>
    /// The woods, eight floats a tree (x, y, z, height, kind, r, g, b), leaving clearings round today's settlements
    /// as the game does.
    /// </summary>
    [JSInvokable]
    public static float[] Trees()
    {
        var map = MapScreen.Build(World, Log);
        var clearings = Markers.For(map, Geography).Where(m => m.Shape == MarkerShape.Settlement).Select(m => m.Position);
        return Scatter.Trees(Geography.Terrain, clearings)
            .SelectMany(t => new[] { t.Position.X, t.Position.Y, t.Position.Z, t.Height, (float)t.Kind, t.Colour.X, t.Colour.Y, t.Colour.Z }).ToArray();
    }

    /// <summary>
    /// The things standing on the block in a year (empty: today), as JSON: houses (nine numbers each: x, y, z, size, yaw,
    /// roof r, g, b, tower), markers for landmarks and labyrinths, border and road lines, realms' standards and hosts'
    /// banners with where their names go, and each host's current move as a packed arrow mesh (base64).
    /// </summary>
    [JSInvokable]
    public static string Scene(string year)
    {
        var map = MapAt(year);
        var geography = Geography;
        var grid = geography.Terrain;
        var markers = Markers.For(map, geography);
        float[] V(Vector3 v) => [v.X, v.Y, v.Z];
        var scene = new
        {
            houses = markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, grid))
                .SelectMany(h => new[] { h.Position.X, h.Position.Y, h.Position.Z, h.Size, h.Yaw, h.Roof.X, h.Roof.Y, h.Roof.Z, h.Tower ? 1f : 0f }).ToArray(),
            settlements = markers.Where(m => m.Shape == MarkerShape.Settlement)
                .Select(m => new { @ref = m.Ref.ToString(), name = m.Name, at = V(m.Position), size = m.Size, kind = m.Kind.ToString() }),
            structures = markers.Where(m => m.Shape != MarkerShape.Settlement)
                .Select(m => new { @ref = m.Ref.ToString(), name = m.Name, shape = m.Shape.ToString(), at = V(m.Position), size = m.Size, colour = V(m.Colour) }),
            borders = Markers.BorderLines(map, geography).Select(line => line.SelectMany(V).ToArray()),
            roads = RouteLayout.Roads(map, geography).Select(r => new { open = r.Open, sea = r.Sea, points = r.Line.SelectMany(V).ToArray() }),
            realms = (map.Realms ?? []).Select(r => new
            {
                @ref = r.Ref.ToString(),
                name = r.Name,
                rank = r.Rank,
                regions = r.Regions,
                at = V(Projection.ToBlock(grid, r.Position, 0)),
                colour = V(WarLayout.Colour(r.Colour)),
                accent = V(Palette.Heraldic(r.Accent)),
            }),
            armies = (map.Armies ?? []).Select(a => new
            {
                @ref = a.Ref.ToString(),
                name = a.Name,
                soldiers = a.Soldiers,
                side = a.Side,
                at = V(Projection.ToBlock(grid, a.Position, 0)),
                colour = V(WarLayout.Colour(a.Colour)),
                commander = a.Commander?.Name,
            }),
            arrows = WarLayout.Arrows(map, geography).Select(a => new { army = a.Army.ToString(), mesh = Convert.ToBase64String(Pack(a.Mesh)) }),
            // The map's badges (the owner, 2026-10-28): boss markers over beasts, crossed swords where fights are.
            beasts = (map.Monsters ?? []).Select(m => new
            {
                @ref = m.Ref.ToString(),
                name = m.Name,
                breed = m.Breed,
                might = m.Might,
                at = V(Projection.ToBlock(grid, m.Position, 0)),
                eventId = m.RoseEventId,
                badge = m.Slumbering ? "asleep" : m.Legendary ? "legend" : "boss",
            }),
            fights = (map.Battles ?? []).Select(b => new
            {
                kind = b.Kind,
                underWay = b.UnderWay,
                at = V(Projection.ToBlock(grid, b.Position, 0)),
                eventId = b.EventId,
                badge = (b.Kind == MapBattle.Battle ? "battle-" : "") + (b.UnderWay ? "underway" : "fought"),
            }),
            badges = new Dictionary<string, string>
            {
                ["boss"] = Badges.Boss(false, false),
                ["legend"] = Badges.Boss(true, false),
                ["asleep"] = Badges.Boss(false, true),
                ["fought"] = Badges.Swords(false),
                ["underway"] = Badges.Swords(true),
                ["battle-fought"] = Badges.Banners(false),
                ["battle-underway"] = Badges.Banners(true),
            },
        };
        return JsonSerializer.Serialize(scene, Json);
    }

    /// <summary>
    /// A mesh as little-endian bytes: vertex count and index count (int32), a flags int32 (1: alphas, 2: uvs), then
    /// positions, normals and colours (3 float32 each per vertex), alphas (1 each), uvs (2 each), and the indices (int32).
    /// </summary>
    public static byte[] Pack(MeshData mesh)
    {
        var n = mesh.Positions.Length;
        var flags = (mesh.Alphas is not null ? 1 : 0) | (mesh.Uvs is not null ? 2 : 0);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(n);
        w.Write(mesh.Indices.Length);
        w.Write(flags);
        foreach (var set in new[] { mesh.Positions, mesh.Normals, mesh.Colors })
            foreach (var v in set) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        if (mesh.Alphas is { } alphas) foreach (var a in alphas) w.Write(a);
        if (mesh.Uvs is { } uvs) foreach (var uv in uvs) { w.Write(uv.X); w.Write(uv.Y); }
        foreach (var i in mesh.Indices) w.Write(i);
        w.Flush();
        return stream.ToArray();
    }
}

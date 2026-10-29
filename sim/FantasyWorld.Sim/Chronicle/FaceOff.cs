using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>One figure of a side in a face-off: its name, its value and the bar's full length (both sides share it).</summary>
public sealed record FaceOffStat(string Name, double Value, double Max)
{
    /// <summary>
    /// A figure as the card shows it, short enough to read at a glance (the owner, 2026-10-29): 62, 2.9K, 19.1K, 249K, 3.69M.
    /// </summary>
    public static string Compact(double value)
    {
        var v = Math.Abs(value);
        var sign = value < 0 ? "-" : "";
        string Scaled(double x, string unit) => (x < 10 ? x.ToString("0.##", CultureInfo.InvariantCulture)
            : x < 100 ? x.ToString("0.#", CultureInfo.InvariantCulture) : Math.Round(x).ToString(CultureInfo.InvariantCulture)) + unit;
        return sign + (v < 1_000 ? Math.Round(v).ToString(CultureInfo.InvariantCulture)
            : v < 999_500 ? Scaled(v / 1_000, "K")
            : v < 999_500_000 ? Scaled(v / 1_000_000, "M") : Scaled(v / 1_000_000_000, "B"));
    }
}

/// <summary>
/// A side of a face-off: who, their heraldic colour, their figures and a few words about them (traits, age, ruler), and
/// their image once the broadcast has one (a duellist's portrait or a realm's banner: its key in the broadcast's store), and
/// a duellist's class (<see cref="Classes"/>; the card shows its emblem, and which side's counters the other's), and once
/// the fight is fought, how the side came out of it (<see cref="Fates"/>: stamped over its image, a slain duellist's greyed).
/// </summary>
public sealed record FaceOffSide(string Ref, string Name, string Colour, IReadOnlyList<FaceOffStat> Stats, IReadOnlyList<string> Notes,
    string? Image = null, string? Class = null, string? Fate = null);

/// <summary>How a side came out of a fought fight, as the film stamps it over the side's image (the owner, 2026-10-29).</summary>
public static class Fates
{
    public const string Victor = "Victor", Victorious = "Victorious", Defeated = "Defeated", Slain = "Slain", Maimed = "Maimed",
        Wounded = "Wounded", Escaped = "Escaped", Yielded = "Yielded";

    /// <summary>The loser's fate in a duel by its outcome ("attacker_kills", "defender_escaped", …).</summary>
    public static string OfLoser(string outcome) =>
        outcome.EndsWith("kills", StringComparison.Ordinal) || outcome == "both_die" ? Slain
        : outcome.EndsWith("maims", StringComparison.Ordinal) ? Maimed
        : outcome.EndsWith("wounds", StringComparison.Ordinal) ? Wounded
        : outcome.EndsWith("escaped", StringComparison.Ordinal) ? Escaped : Yielded;
}

/// <summary>
/// A face-off (sub-project 8 §7, "the challenge"): a duel or a war declared this episode, both sides side by side with their
/// figures, the stakes and the first side's chance of winning, as the world reckoned it at the declaration (the odds bar
/// shows it in the two sides' colours). Stored on the episode's section, so the narration, the film and the posts read the
/// same figures.
/// </summary>
public sealed record FaceOff(string Kind, long EventId, string Stakes, FaceOffSide A, FaceOffSide B, int OddsA,
    string Stage = FaceOff.Challenge, string? Result = null)
{
    public const string Duel = "duel", War = "war";

    /// <summary>The face-off's moment (the owner, 2026-10-28): a challenge before the fight, a fight under way (OddsA the
    /// chance reckoned at its declaration), or a fight fought (OddsA 100 or 0, the victor's side; Result says how).</summary>
    public const string Challenge = "challenge", UnderWay = "under way", Fought = "fought";

    /// <summary>The figures as a brief reads them, one line a side (for the chronicler, and for the narration guard's facts).</summary>
    public string Figures() =>
        string.Join("\n", new[] { A, B }.Select(s => $"{s.Name}: " + string.Join(", ", s.Stats.Select(f => $"{f.Name.ToLowerInvariant()} {Number(f.Value)}"))
                                                    + (s.Notes.Count > 0 ? $"; {string.Join(", ", s.Notes)}" : "")))
        + (Stage == Fought
            ? $"\nResult: {Result}"
            : $"\nThe odds: {Odds()}." + (Result is null ? "" : $"\nSo far: {Result}"));

    /// <summary>
    /// The odds in words, never as a number (the owner, 2026-10-29: the narrator speaks of no percentage; the card shows it):
    /// "Aldren of Vale is heavily favoured", "they are evenly matched".
    /// </summary>
    public string Odds()
    {
        var (name, odds) = OddsA >= 50 ? (A.Name, OddsA) : (B.Name, 100 - OddsA);
        return odds switch
        {
            < 53 => "they are evenly matched",
            < 60 => $"{name} has a slight edge",
            < 72 => $"{name} is favoured",
            < 85 => $"{name} is clearly favoured",
            _ => $"{name} is heavily favoured",
        };
    }

    private static string Number(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Makes the face-offs of an episode from its declarations: who stands against whom, and with what.</summary>
public static class FaceOffs
{
    /// <summary>At most this many face-offs an episode, the weightiest first.</summary>
    public const int PerEpisode = 2;

    public static readonly IReadOnlySet<string> Declarations = new HashSet<string>(StringComparer.Ordinal) { "DuelDeclared", "WarDeclared" };

    /// <summary>The face-off of a declaration, if both sides are still known to the world.</summary>
    public static FaceOff? For(World world, WorldEvent ev)
    {
        var odds = ev.Payload.TryGetValue("odds", out var o) && int.TryParse(o, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 50;
        switch (ev.Type)
        {
            case "DuelDeclared" when ev.Actors.TryGetValue("challenger", out var a) && ev.Actors.TryGetValue("defender", out var b)
                                     && world.Characters.TryGetValue(a.Id, out var challenger) && world.Characters.TryGetValue(b.Id, out var defender):
                return new FaceOff(FaceOff.Duel, ev.Id, ev.Payload.GetValueOrDefault("stakes") ?? "first blood",
                    Fighter(world, challenger, "crimson"), Fighter(world, defender, "azure"), odds);
            case "WarDeclared" when ev.Actors.TryGetValue("attacker", out var a) && ev.Actors.TryGetValue("defender", out var b)
                                    && world.Realms.TryGetValue(a.Id, out var attacker) && world.Realms.TryGetValue(b.Id, out var defender):
                double Strength(string key, Realm realm) =>
                    ev.Payload.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : DiplomacySystem.Strength(world, realm);
                var (sa, sd) = (Strength("attacker strength", attacker), Strength("defender strength", defender));
                return Scaled(new FaceOff(FaceOff.War, ev.Id, ev.Payload.GetValueOrDefault("war") ?? "war",
                    Host(world, attacker, sa, Math.Max(sa, sd)), Host(world, defender, sd, Math.Max(sa, sd)), odds));
            default:
                return null;
        }
    }

    /// <summary>The fights whose sections get a face-off of their result or of their course so far.</summary>
    public static readonly IReadOnlySet<string> Fights = new HashSet<string>(StringComparer.Ordinal)
    {
        "DuelBegun", "DuelTurned", "DuelFought", "BattleJoined", "BattleTurned", "BattleFought",
    };

    /// <summary>
    /// The face-off of a fight told in the episode (the owner, 2026-10-28): the two sides as they stand now, with how it
    /// ended (who won, how they fared, the losses) or how far it has gone (its day, the odds reckoned at its declaration).
    /// </summary>
    public static FaceOff? OfFight(World world, EventLog log, WorldEvent ev)
    {
        if (!Fights.Contains(ev.Type)) return null;
        var place = ev.Place is { } at ? Names.Of(world)(at) : null;
        var day = ev.Payload.GetValueOrDefault("day");
        var days = ev.Payload.GetValueOrDefault("days");
        var soFar = day is not null && days is not null ? $"day {day} of {days}, still undecided"
            : days is not null ? $"the first of {days} days, still undecided" : "still undecided";
        switch (ev.Type)
        {
            case "DuelBegun" or "DuelTurned" or "DuelFought" when ev.Actors.TryGetValue("challenger", out var a) && ev.Actors.TryGetValue("defender", out var b)
                                                                 && world.Characters.TryGetValue(a.Id, out var challenger) && world.Characters.TryGetValue(b.Id, out var defender):
            {
                var stakes = ev.Payload.GetValueOrDefault("stakes") ?? "first blood";
                var (sa, sb) = (Fighter(world, challenger, "crimson"), Fighter(world, defender, "azure"));
                if (ev.Type != "DuelFought") return new FaceOff(FaceOff.Duel, ev.Id, stakes, sa, sb, DeclaredOdds(log, ev), FaceOff.UnderWay, soFar);
                var outcome = ev.Payload.GetValueOrDefault("outcome") ?? "";
                var challengerWon = outcome.StartsWith("attacker", StringComparison.Ordinal);
                var (winner, loser) = challengerWon ? (sa.Name, sb.Name) : (sb.Name, sa.Name);
                var both = outcome == "both_die";
                var how = both ? "both were slain"
                    : outcome.EndsWith("kills", StringComparison.Ordinal) ? $"{loser} was slain"
                    : outcome.EndsWith("maims", StringComparison.Ordinal) ? $"{loser} was maimed"
                    : outcome.EndsWith("wounds", StringComparison.Ordinal) ? "both were wounded"
                    : outcome.EndsWith("escaped", StringComparison.Ordinal) ? $"{loser} was wounded but escaped" : $"{loser} yielded";
                var (won, lost) = (both ? Fates.Slain : Fates.Victor, Fates.OfLoser(outcome));
                (sa, sb) = challengerWon ? (sa with { Fate = won }, sb with { Fate = lost }) : (sa with { Fate = lost }, sb with { Fate = won });
                return new FaceOff(FaceOff.Duel, ev.Id, stakes, sa, sb, challengerWon ? 100 : 0, FaceOff.Fought,
                    both ? $"Neither won{(days is null ? "" : $" after {days} days")}; {how}." : $"{winner} won{(days is null ? "" : $" after {days} days")}; {how}.");
            }
            case "BattleJoined" or "BattleTurned" when ev.Actors.TryGetValue("attacker", out var a) && ev.Actors.TryGetValue("defender", out var b)
                                                       && world.Realms.TryGetValue(a.Id, out var attacker) && world.Realms.TryGetValue(b.Id, out var defender):
            {
                var (sa, sd) = (DiplomacySystem.Strength(world, attacker), DiplomacySystem.Strength(world, defender));
                return Scaled(new FaceOff(FaceOff.War, ev.Id, place is null ? "battle" : $"the battle at {place}",
                    Host(world, attacker, sa, Math.Max(sa, sd)), Host(world, defender, sd, Math.Max(sa, sd)), DeclaredOdds(log, ev), FaceOff.UnderWay, soFar));
            }
            case "BattleFought" when ev.Actors.TryGetValue("victor", out var a) && ev.Actors.TryGetValue("vanquished", out var b)
                                     && world.Realms.TryGetValue(a.Id, out var victor) && world.Realms.TryGetValue(b.Id, out var vanquished):
            {
                var (sv, sq) = (DiplomacySystem.Strength(world, victor), DiplomacySystem.Strength(world, vanquished));
                double Lost(string key) => double.TryParse(ev.Payload.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                var (lv, lq) = (Lost("victor losses"), Lost("vanquished losses"));
                FaceOffSide WithLosses(FaceOffSide side, double lost) =>
                    side with { Stats = side.Stats.Prepend(new FaceOffStat("Losses", lost, Math.Max(1, Math.Max(lv, lq)))).ToList() };
                var f = Scaled(new FaceOff(FaceOff.War, ev.Id, place is null ? "battle" : $"the battle at {place}",
                    Host(world, victor, sv, Math.Max(sv, sq)), Host(world, vanquished, sq, Math.Max(sv, sq)), 100, FaceOff.Fought,
                    $"{victor.Name} won{(days is null ? "" : $" after {days} days")}; {Number(lv)} of theirs and {Number(lq)} of {vanquished.Name}'s fell."));
                return f with { A = WithLosses(f.A, lv) with { Fate = Fates.Victorious }, B = WithLosses(f.B, lq) with { Fate = Fates.Defeated } };
            }
            default:
                return null;
        }
    }

    private static string Number(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The chance its declaration reckoned for the fight's first side (50 when none was): the declaration is a cause
    /// of the fight's beginning, which is a cause of each later day.</summary>
    private static int DeclaredOdds(EventLog log, WorldEvent ev)
    {
        var frontier = new List<WorldEvent> { ev };
        for (var depth = 0; depth < 4 && frontier.Count > 0; depth++)
        {
            if (frontier.FirstOrDefault(e => e.Payload.ContainsKey("odds")) is { } declared
                && int.TryParse(declared.Payload["odds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var odds)) return odds;
            frontier = frontier.SelectMany(e => e.Causes).Select(c => log.TryGet(c.EventId, out var cause) ? cause : null).OfType<WorldEvent>().ToList();
        }
        return 50;
    }

    /// <summary>
    /// A duellist, in a fantasy's terms (the owner, 2026-10-28): might (prowess), valour (martial), cunning (intrigue) and
    /// arcana (lore), each 0–20 with what their artifacts add; glory (renown, 0–100); the relics they carry; their class;
    /// race, age, traits and wounds as notes.
    /// </summary>
    private static FaceOffSide Fighter(World world, Character c, string fallbackColour)
    {
        var relics = world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList();
        FaceOffStat Gift(string name, string attribute)
        {
            var value = c.Attributes.Get(attribute) + relics.Sum(r => r.Bonus(attribute));
            return new FaceOffStat(name, value, Math.Max(20, value));
        }
        var stats = new List<FaceOffStat>
        {
            Gift("Might", "prowess"), Gift("Valour", "martial"), Gift("Cunning", "intrigue"), Gift("Arcana", "lore"),
            new("Glory", Math.Round(c.Renown), 100), new("Relics", relics.Count, Math.Max(1, relics.Count)),
        };
        var notes = new List<string> { $"aged {c.Age(world.Date)}" };
        if (Peoples.Of(world, c) is { } race) notes.Insert(0, race.Name.ToLowerInvariant());
        notes.AddRange(c.Traits.Take(4));
        if (c.Wounds.Count > 0) notes.Add(c.Wounds.Count == 1 ? "bearing a wound" : "bearing old wounds");
        var colour = world.Houses.TryGetValue(c.HouseId, out var house) && world.Realms.Values.FirstOrDefault(r => Realms.RulerOf(world, r)?.HouseId == house.Id) is { } realm
            ? realm.Colors[0] : fallbackColour;
        return new FaceOffSide(c.Ref.ToString(), Names.Full(world, c), colour, stats, notes, Class: Classes.Of(world, c));
    }

    /// <summary>A realm at war, in a fantasy's terms: its hosts (fighting strength, both bars on one scale), lands, war chest,
    /// steel and steeds in store, the crown's right (legitimacy) and morale (stability); its ruler and rank as notes, and
    /// any shortage.</summary>
    private static FaceOffSide Host(World world, Realm realm, double strength, double strongest)
    {
        var land = Realms.Territory(world, realm).Count();
        var stats = new List<FaceOffStat>
        {
            new(realm.Government == Government.Dominion ? "Horde" : "Hosts", Math.Round(strength), Math.Max(1, strongest)),
            new("Lands", land, Math.Max(1, land)),
            new("War chest", Math.Round(Math.Max(0, realm.Treasury)), Math.Max(1, realm.Treasury)),
            new("Steel", Math.Round(Stocks.Stock(realm, ResourceKinds.Ore)), Math.Max(1, Stocks.Stock(realm, ResourceKinds.Ore))),
            new("Steeds", Math.Round(Stocks.Stock(realm, ResourceKinds.Horses)), Math.Max(1, Stocks.Stock(realm, ResourceKinds.Horses))),
            new("Crown's right", Math.Round(realm.Legitimacy), 100),
            new("Morale", Math.Round(realm.Stability), 100),
        };
        var notes = new List<string> { realm.Rank.ToString().ToLowerInvariant() };
        if (Realms.RulerOf(world, realm) is { } ruler) notes.Add($"ruled by {Names.Full(world, ruler)}");
        notes.AddRange(Stocks.Shortages(realm).Select(k => $"short of {k}"));
        return new FaceOffSide(realm.Ref.ToString(), realm.Name, realm.Colors[0], stats, notes);
    }

    /// <summary>
    /// Puts both sides' relative figures (lands, war chest, steel, steeds, relics, and gifts past 20) on one scale, so their bars compare: each side's bar is drawn
    /// against the larger of the two.
    /// </summary>
    public static FaceOff Scaled(FaceOff f)
    {
        FaceOffSide Rescale(FaceOffSide side, FaceOffSide other) => side with
        {
            Stats = side.Stats.Select((s, i) => i >= other.Stats.Count ? s : s.Name switch
            {
                "Lands" or "War chest" or "Steel" or "Steeds" or "Relics" => s with { Max = Math.Max(1, Math.Max(s.Value, other.Stats[i].Value)) },
                "Might" or "Valour" or "Cunning" or "Arcana" => s with { Max = Math.Max(20, Math.Max(s.Value, other.Stats[i].Value)) },
                _ => s,
            }).ToList(),
        };
        return f with { A = Rescale(f.A, f.B), B = Rescale(f.B, f.A) };
    }

    /// <summary>
    /// The face-off told plainly (the template's words, spoken if no chronicler answers): who meets whom, where each is the
    /// stronger, and the odds.
    /// </summary>
    public static string Tell(FaceOff f, string place)
    {
        string Better(FaceOffSide side, FaceOffSide other)
        {
            var leads = side.Stats.Select((s, i) => (s.Name, Lead: (s.Value - other.Stats[i].Value) / Math.Max(1, s.Max)))
                .Where(x => x.Lead > 0.08).OrderByDescending(x => x.Lead).Take(2).Select(x => x.Name.ToLowerInvariant()).ToList();
            return leads.Count switch { 0 => "", 1 => leads[0], _ => $"{leads[0]} and {leads[1]}" };
        }
        var (a, b) = (Better(f.A, f.B), Better(f.B, f.A));
        var opening = f.Kind == FaceOff.Duel
            ? $"The gauntlet is thrown: {f.A.Name} against {f.B.Name}, {f.Stakes}, at {place}, a year and a day from the challenge."
            : $"War is declared: {f.A.Name} against {f.B.Name} in {f.Stakes}; the hosts muster for a year before they meet.";
        var strengths = (a.Length, b.Length) switch
        {
            (0, 0) => "They are evenly matched.",
            (> 0, 0) => $"{f.A.Name} has the better of it in {a}.",
            (0, > 0) => $"{f.B.Name} has the better of it in {b}.",
            _ => $"{f.A.Name} is the stronger in {a}; {f.B.Name} in {b}.",
        };
        return $"{opening} {strengths} By the odds, {f.Odds()}.";
    }
}

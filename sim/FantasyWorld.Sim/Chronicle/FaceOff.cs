using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Chronicle;

/// <summary>One figure of a side in a face-off: its name, its value and the bar's full length (both sides share it).</summary>
public sealed record FaceOffStat(string Name, double Value, double Max);

/// <summary>
/// A side of a face-off: who, their heraldic colour, their figures and a few words about them (traits, age, ruler), and
/// their image once the broadcast has one (a duellist's portrait or a realm's banner: its key in the broadcast's store).
/// </summary>
public sealed record FaceOffSide(string Ref, string Name, string Colour, IReadOnlyList<FaceOffStat> Stats, IReadOnlyList<string> Notes,
    string? Image = null);

/// <summary>
/// A face-off (sub-project 8 §7, "the challenge"): a duel or a war declared this episode, both sides side by side with their
/// figures, the stakes and the first side's chance of winning, as the world reckoned it at the declaration (the odds bar
/// shows it in the two sides' colours). Stored on the episode's section, so the narration, the film and the posts read the
/// same figures.
/// </summary>
public sealed record FaceOff(string Kind, long EventId, string Stakes, FaceOffSide A, FaceOffSide B, int OddsA)
{
    public const string Duel = "duel", War = "war";

    /// <summary>The figures as a brief reads them, one line a side (for the chronicler, and for the narration guard's facts).</summary>
    public string Figures() =>
        string.Join("\n", new[] { A, B }.Select(s => $"{s.Name}: " + string.Join(", ", s.Stats.Select(f => $"{f.Name.ToLowerInvariant()} {Number(f.Value)}"))
                                                    + (s.Notes.Count > 0 ? $"; {string.Join(", ", s.Notes)}" : "")))
        + $"\nChance of winning: {A.Name} {OddsA} in a hundred, {B.Name} {100 - OddsA} in a hundred.";

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

    /// <summary>A duellist: the six attributes (0–20) and renown (0–100); traits, age and wounds as notes.</summary>
    private static FaceOffSide Fighter(World world, Character c, string fallbackColour)
    {
        var stats = Attributes.Names.Select(n => new FaceOffStat(char.ToUpperInvariant(n[0]) + n[1..], c.Attributes.Get(n), 20)).Append(new FaceOffStat("Renown", Math.Round(c.Renown), 100)).ToList();
        var notes = new List<string> { $"aged {c.Age(world.Date)}" };
        notes.AddRange(c.Traits.Take(4));
        if (c.Wounds.Count > 0) notes.Add(c.Wounds.Count == 1 ? "bearing a wound" : "bearing old wounds");
        var colour = world.Houses.TryGetValue(c.HouseId, out var house) && world.Realms.Values.FirstOrDefault(r => Realms.RulerOf(world, r)?.HouseId == house.Id) is { } realm
            ? realm.Colors[0] : fallbackColour;
        return new FaceOffSide(c.Ref.ToString(), Names.Full(world, c), colour, stats, notes);
    }

    /// <summary>A realm at war: its fighting strength (both bars on one scale), land, treasure, ore and horses in store,
    /// legitimacy and stability; its ruler and rank as notes, and any shortage.</summary>
    private static FaceOffSide Host(World world, Realm realm, double strength, double strongest)
    {
        var land = Realms.Territory(world, realm).Count();
        var stats = new List<FaceOffStat>
        {
            new("Strength", Math.Round(strength), Math.Max(1, strongest)),
            new("Regions", land, Math.Max(1, land)),
            new("Treasury", Math.Round(Math.Max(0, realm.Treasury)), Math.Max(1, realm.Treasury)),
            new("Ore", Math.Round(Stocks.Stock(realm, ResourceKinds.Ore)), Math.Max(1, Stocks.Stock(realm, ResourceKinds.Ore))),
            new("Horses", Math.Round(Stocks.Stock(realm, ResourceKinds.Horses)), Math.Max(1, Stocks.Stock(realm, ResourceKinds.Horses))),
            new("Legitimacy", Math.Round(realm.Legitimacy), 100),
            new("Stability", Math.Round(realm.Stability), 100),
        };
        var notes = new List<string> { realm.Rank.ToString().ToLowerInvariant() };
        if (Realms.RulerOf(world, realm) is { } ruler) notes.Add($"ruled by {Names.Full(world, ruler)}");
        notes.AddRange(Stocks.Shortages(realm).Select(k => $"short of {k}"));
        return new FaceOffSide(realm.Ref.ToString(), realm.Name, realm.Colors[0], stats, notes);
    }

    /// <summary>
    /// Puts both sides' relative figures (regions, treasury, ore, horses) on one scale, so their bars compare: each side's bar is drawn
    /// against the larger of the two.
    /// </summary>
    public static FaceOff Scaled(FaceOff f)
    {
        FaceOffSide Rescale(FaceOffSide side, FaceOffSide other) => side with
        {
            Stats = side.Stats.Select((s, i) => s.Name is "Regions" or "Treasury" or "Ore" or "Horses" ? s with { Max = Math.Max(1, Math.Max(s.Value, other.Stats[i].Value)) } : s).ToList(),
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
        var favoured = f.OddsA >= 50 ? (f.A.Name, f.OddsA) : (f.B.Name, 100 - f.OddsA);
        var opening = f.Kind == FaceOff.Duel
            ? $"The face-off: {f.A.Name} against {f.B.Name}, {f.Stakes}, at {place}, a year and a day from the challenge."
            : $"The face-off: {f.A.Name} against {f.B.Name} in {f.Stakes}; the hosts muster for a year before they meet.";
        var strengths = (a.Length, b.Length) switch
        {
            (0, 0) => "They are evenly matched.",
            (> 0, 0) => $"{f.A.Name} has the better of it in {a}.",
            (0, > 0) => $"{f.B.Name} has the better of it in {b}.",
            _ => $"{f.A.Name} is the stronger in {a}; {f.B.Name} in {b}.",
        };
        return $"{opening} {strengths} The odds favour {favoured.Item1}, {favoured.Item2} in a hundred.";
    }
}

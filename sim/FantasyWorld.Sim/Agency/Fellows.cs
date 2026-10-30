using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Agency;

/// <summary>What a fight leaves of one who fought beside a fighter (the owner, 2026-10-30).</summary>
public enum FellowFate { Unharmed, Injured, Incapacitated, Disabled, Separated, Dead }

/// <summary>
/// One at a fighter's side in a fight: their companion (a beast, or a sworn friend living with them) or a member of a hero's
/// party. <see cref="Key"/> names them to the arbiter; <see cref="CharacterId"/> is 0 for a beast.
/// </summary>
public sealed record Fellow(string Key, bool WithAttacker, long OwnerId, long CharacterId, string Name, string What, bool Party = false);

/// <summary>
/// Companions and party members in a fight (the owner, 2026-10-30: they can die, be injured, incapacitated, disabled or
/// separated, and Claude is the one to decide). When Claude arbitrates the fight it chooses each one's fate; otherwise (or
/// for any it left out) the rules roll it from the fight, the losing side faring worse and a hero's party worst of all.
/// </summary>
public static class Fellows
{
    /// <summary>How long one incapacitated is out of the fight.</summary>
    public const int OutDays = WorldDate.DaysPerYear;

    /// <summary>Whether a companion is at their side today: a beast not laid up, or a sworn friend living with them and able.</summary>
    public static bool Present(World world, Character c) => c.Companion switch
    {
        { Kind: Companion.Beast } beast => beast.OutUntil <= world.Date.Day,
        { Kind: Companion.Sworn, CharacterId: var id } sworn => sworn.OutUntil <= world.Date.Day && world.Characters.TryGetValue(id, out var friend)
                                                               && friend.IsActive && friend.SettlementId == c.SettlementId && friend.Stage(world.Date) == LifeStage.Adult,
        _ => false,
    };

    /// <summary>The fellows of a fight: each fighter's companion at their side, and the attacker's party.</summary>
    public static IReadOnlyList<Fellow> In(World world, Character attacker, Character? defender, IReadOnlyList<Character>? party = null)
    {
        var fellows = new List<Fellow>();
        void Side(Character c, bool withAttacker, string key)
        {
            if (!Present(world, c)) return;
            var k = c.Companion!;
            if (party?.Any(p => p.Id == k.CharacterId) == true) return;
            fellows.Add(new Fellow(key, withAttacker, c.Id, k.Kind == Companion.Sworn ? k.CharacterId : 0, k.Name,
                k.Kind == Companion.Beast ? k.What + (k.Lame ? ", lamed" : "") : $"sworn companion of {c.Name}"));
        }
        Side(attacker, true, "a1");
        if (defender is not null) Side(defender, false, "d1");
        if (party is not null)
            for (var i = 0; i < party.Count; i++)
                fellows.Add(new Fellow($"p{i + 1}", true, attacker.Id, party[i].Id, party[i].Name, $"of {attacker.Name}'s party, level {Levels.Of(party[i])}", Party: true));
        return fellows;
    }

    /// <summary>The rules' fate for a fellow: rolled from them and the moment, worse for the side that lost and for a hero's party.</summary>
    public static FellowFate Roll(Fellow f, bool sideWon, bool deadly, long moment)
    {
        // Out of a hundred: dead, disabled, incapacitated, injured, separated.
        int[] odds = (f.Party, sideWon) switch
        {
            (true, true) => [12, 8, 10, 25, 0],
            (true, false) => [40, 10, 10, 20, 10],
            (false, true) => [2, 2, 4, 12, 2],
            _ => [10, 6, 10, 18, 8],
        };
        if (!deadly) odds = [odds[0] / 4, odds[1] / 2, odds[2], odds[3] + odds[0] - odds[0] / 4 + odds[1] - odds[1] / 2, odds[4]];
        var roll = Hash.Percent(f.OwnerId * 31 + f.CharacterId, 7801 + f.Key.Sum(ch => (int)ch), moment);
        foreach (var (fate, chance) in new[] { FellowFate.Dead, FellowFate.Disabled, FellowFate.Incapacitated, FellowFate.Injured, FellowFate.Separated }.Zip(odds))
        {
            if (roll < chance) return fate;
            roll -= chance;
        }
        return FellowFate.Unharmed;
    }

    public static string Word(FellowFate fate) => fate.ToString().ToLowerInvariant();

    public static FellowFate? Parse(string word) => Enum.TryParse<FellowFate>(word, ignoreCase: true, out var f) && Enum.IsDefined(f) ? f : null;

    /// <summary>Fates written down for a fight that ends on a later day ("a1=dead,p2=injured").</summary>
    public static string Encode(IReadOnlyDictionary<string, FellowFate> fates) => string.Join(",", fates.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={Word(f.Value)}"));

    public static IReadOnlyDictionary<string, FellowFate> Decode(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('='))
            .Where(p => p.Length == 2 && Parse(p[1]) is not null).ToDictionary(p => p[0], p => Parse(p[1])!.Value, StringComparer.Ordinal);

    private static string Told(FellowFate fate, bool beast) => fate switch
    {
        FellowFate.Dead => "fell in the fight",
        FellowFate.Injured => "was hurt in the fight",
        FellowFate.Incapacitated => "was struck down, and will be long in mending",
        FellowFate.Disabled => beast ? "was lamed for good" : "was crippled for good",
        _ => "was lost in the fighting and parted from them",
    };

    /// <summary>
    /// Applies each fellow's fate after the fight (the chosen ones, the rest rolled), each told; returns those it took out of
    /// the fight for good or for long (dead, disabled, incapacitated, separated), so a party can let them go.
    /// </summary>
    public static IReadOnlyList<Fellow> Apply(SimContext ctx, IReadOnlyList<Fellow> fellows, EncounterOutcome outcome, bool deadly, WorldEvent fight,
        Func<Fellow, Character?>? killerOf = null)
    {
        var world = ctx.World;
        var lost = new List<Fellow>();
        foreach (var f in fellows)
        {
            var fate = outcome.FellowFates.TryGetValue(f.Key, out var chosen) ? chosen : Roll(f, f.WithAttacker == outcome.AttackerWins, deadly, fight.Id);
            if (fate == FellowFate.Unharmed || !world.Characters.TryGetValue(f.OwnerId, out var owner)) continue;
            var member = f.CharacterId != 0 ? world.Characters.GetValueOrDefault(f.CharacterId) : null;
            if (f.CharacterId != 0 && member is not { IsAlive: true }) continue;
            var actors = new Dictionary<string, EntityRef> { ["who"] = owner.Ref };
            if (member is not null) actors["fellow"] = member.Ref;
            var told = ctx.Emit(new EventDraft("FellowFate", Actors: actors, Place: fight.Place,
                Payload: new Dictionary<string, string> { ["name"] = f.Name, ["fate"] = Word(fate), ["told"] = Told(fate, member is null), ["party"] = f.Party ? "yes" : "no" },
                Causes: new[] { new Cause(fight.Id, CauseKind.Trigger, 1.0) },
                Importance: Math.Max(Fates.Importance(owner, fate == FellowFate.Dead ? 20 : 10), member is null ? 0 : Fates.Importance(member, fate == FellowFate.Dead ? 20 : 10)),
                Tags: new[] { "Characters", "Deeds" }));
            if (fate != FellowFate.Injured) lost.Add(f);
            var rng = new Rng(unchecked((ulong)(told.Id * 7919 + f.CharacterId)));
            if (member is not null)
            {
                switch (fate)
                {
                    case FellowFate.Dead:
                        Fates.Die(ctx, member, "a fight at another's side", new[] { new Cause(told.Id, CauseKind.Trigger, 1.0) }, killerOf?.Invoke(f) is { IsAlive: true } k ? k : null);
                        break;
                    case FellowFate.Injured or FellowFate.Incapacitated:
                        Fates.Injure(member, Fate.Wounded, rng);
                        break;
                    case FellowFate.Disabled:
                        Fates.Injure(member, Fate.Maimed, rng);
                        break;
                }
                if (!f.Party && owner.Companion is { Kind: Companion.Sworn } bond && bond.CharacterId == member.Id)
                {
                    if (fate == FellowFate.Separated)
                    {
                        owner.Companion = null;
                        if (member.Companion?.CharacterId == owner.Id) member.Companion = null;
                    }
                    else if (fate == FellowFate.Incapacitated) owner.Companion = bond with { OutUntil = ctx.Date.Day + OutDays };
                }
                continue;
            }
            if (owner.Companion is not { Kind: Companion.Beast } beast) continue;
            owner.Companion = fate switch
            {
                FellowFate.Dead or FellowFate.Separated => null,
                FellowFate.Incapacitated => beast with { OutUntil = ctx.Date.Day + OutDays },
                FellowFate.Disabled => beast with { Lame = true },
                _ => beast,
            };
        }
        return lost;
    }
}

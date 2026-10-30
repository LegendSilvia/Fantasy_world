using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Masters, apprentices and successors (the owner, 2026-10-30). Each year:
/// <list type="bullet">
/// <item>A master of their art (level 5 or more, thirty or older, with a calling) may take a young apprentice from near them:
/// sixteen to thirty, of a lower level, who does not dislike them, of the same calling if one can be found; two at most.
/// The bond warms them toward each other, and the apprentice learns faster while it holds.</item>
/// <item>An old master (fifty or more), or a great one (level 6 or more), names a successor: their highest-level
/// apprentice still loyal.</item>
/// <item>When a master dies, their successor (or, with none named, their most loyal apprentice) inherits the mantle: the
/// master's own weapon if they have none, and something of the master's renown and experience.</item>
/// <item>A master and apprentice can fall out, and then they are enemies: when the apprentice outgrows the master and is proud,
/// envious or hungry for renown; when their realms go to war with each other; or when what passed between them has turned
/// them to hatred. Bound or not, they may still come to blows as anyone may.</item>
/// </list>
/// Rolled from the characters and the year, never the world's dice.
/// </summary>
public sealed class MentorSystem : ISimSystem
{
    public string Name => "mentors";

    public const int MasterLevel = 5, MasterAge = 30, ApprenticeMinAge = 16, ApprenticeMaxAge = 30, MostApprentices = 2;
    public const int TakeChance = 25, SuccessorAge = 50, SuccessorLevel = 6;

    /// <summary>The chance each year of a falling-out, in a hundred: an apprentice who outgrew a proud bond, or one whose realm fights the master's.</summary>
    public const int OutgrownChance = 40, WarChance = 30;

    /// <summary>What a master's teaching adds to an apprentice's year: this much experience for each level of the master.</summary>
    public const double TeachingPerLevel = 2;

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var year = ctx.Date.Year;
        var active = world.ActiveCharacters.OrderBy(c => c.Id).ToList();
        IEnumerable<Character> ApprenticesOf(Character m) => world.Characters.Values.Where(a => a.MasterId == m.Id && a.IsActive && !a.Estranged);

        // Inheritance: a master gone, their mantle passes once.
        foreach (var master in world.Characters.Values.Where(m => m.Died is { } d && d.Day >= ctx.Date.Day - WorldDate.DaysPerYear).OrderBy(m => m.Id).ToList())
        {
            var heir = world.Characters.GetValueOrDefault(master.SuccessorId) is { IsActive: true, Estranged: false } named && named.MasterId == master.Id
                ? named
                : ApprenticesOf(master).OrderByDescending(a => Relations.Opinion(a, master)).ThenByDescending(Levels.Of).ThenBy(a => a.Id).FirstOrDefault();
            if (heir is null || ctx.Log.Involving(master.Ref).Any(e => e.Type == "MantleInherited")) continue;
            if (heir.Weapon is null && master.Weapon is { } weapon) heir.Weapon = weapon;
            heir.Renown = Math.Min(100, heir.Renown + master.Renown / 4);
            heir.Experience += master.Experience / 10;
            heir.Level = Math.Max(Levels.Of(heir), Levels.Of(heir.Experience));
            ctx.Emit(new EventDraft("MantleInherited", Actors: new Dictionary<string, EntityRef> { ["master"] = master.Ref, ["successor"] = heir.Ref },
                Place: Home(world, heir), Importance: Math.Max(Fates.Importance(master, 25), Fates.Importance(heir, 25)), Tags: new[] { "Characters" }));
        }

        foreach (var master in active.Where(m => Levels.Of(m) >= MasterLevel && m.Age(ctx.Date) >= MasterAge && m.Class.Length > 0))
        {
            // Taking an apprentice.
            if (ApprenticesOf(master).Count() < MostApprentices && Hash.Percent(master.Id, 7701, year) < TakeChance
                && world.Settlements.TryGetValue(master.SettlementId, out var home))
            {
                var near = new[] { home.RegionId }.Concat(world.Geography?.Graph.Neighbours(home.RegionId) ?? Array.Empty<long>()).ToHashSet();
                var apprentice = active
                    .Where(a => a.Id != master.Id && a.MasterId == 0 && a.Role is not Role.Lord && a.Age(ctx.Date) is >= ApprenticeMinAge and <= ApprenticeMaxAge
                                && Levels.Of(a) < Levels.Of(master) && Relations.Opinion(a, master) >= 0 && !Relations.AreKin(a, master) && a.SpouseId != master.Id
                                && world.Settlements.TryGetValue(a.SettlementId, out var at) && near.Contains(at.RegionId))
                    .OrderByDescending(a => Classes.Base(a.Class) == Classes.Base(master.Class))
                    .ThenBy(a => Hash.Percent(a.Id, 7702, master.Id)).ThenBy(a => a.Id).FirstOrDefault();
                if (apprentice is not null)
                {
                    apprentice.MasterId = master.Id;
                    var taken = ctx.Emit(new EventDraft("ApprenticeTaken", Actors: new Dictionary<string, EntityRef> { ["master"] = master.Ref, ["apprentice"] = apprentice.Ref },
                        Place: home.Ref, Payload: new Dictionary<string, string> { ["art"] = master.Class },
                        Importance: Math.Max(15, Fates.Importance(master, 15)), Tags: new[] { "Characters" }));
                    Fates.Remember(apprentice, taken.Id, master.Id, 40, 60);
                    Fates.Remember(master, taken.Id, apprentice.Id, 30, 60);
                }
            }

            // Naming a successor.
            if (master.SuccessorId == 0 || world.Characters.GetValueOrDefault(master.SuccessorId) is not { IsActive: true, Estranged: false })
            {
                master.SuccessorId = 0;
                if ((master.Age(ctx.Date) >= SuccessorAge || Levels.Of(master) >= SuccessorLevel)
                    && ApprenticesOf(master).OrderByDescending(Levels.Of).ThenByDescending(a => Relations.Opinion(a, master)).ThenBy(a => a.Id).FirstOrDefault() is { } chosen)
                {
                    master.SuccessorId = chosen.Id;
                    ctx.Emit(new EventDraft("SuccessorNamed", Actors: new Dictionary<string, EntityRef> { ["master"] = master.Ref, ["successor"] = chosen.Ref },
                        Place: Home(world, master), Importance: Math.Max(18, Fates.Importance(master, 18)), Tags: new[] { "Characters" }));
                }
            }
        }

        // Teaching, and falling out.
        foreach (var apprentice in active.Where(a => a.MasterId != 0 && !a.Estranged))
        {
            if (!world.Characters.TryGetValue(apprentice.MasterId, out var master) || !master.IsActive) continue;
            var reason = Hatred(apprentice, master) ? "hatred"
                : Levels.Of(apprentice) > Levels.Of(master) && Proud(apprentice) && Hash.Percent(apprentice.Id, 7703, year) < OutgrownChance ? "outgrown"
                : RealmsAtWar(world, apprentice, master) && Hash.Percent(apprentice.Id, 7704, year) < WarChance ? "war"
                : null;
            if (reason is null)
            {
                apprentice.Experience += TeachingPerLevel * Levels.Of(master);
                continue;
            }
            apprentice.Estranged = true;
            if (master.SuccessorId == apprentice.Id) master.SuccessorId = 0;
            var fell = ctx.Emit(new EventDraft("FellOutWithMaster", Actors: new Dictionary<string, EntityRef> { ["apprentice"] = apprentice.Ref, ["master"] = master.Ref },
                Place: Home(world, apprentice), Payload: new Dictionary<string, string> { ["reason"] = reason },
                Importance: Math.Max(25, Math.Max(Fates.Importance(master, 25), Fates.Importance(apprentice, 25))), Tags: new[] { "Characters" }));
            // Enemies now: each remembers the break bitterly, and the grudge may come to blows.
            Fates.Remember(apprentice, fell.Id, master.Id, -70, 80);
            Fates.Remember(master, fell.Id, apprentice.Id, -70, 80);
        }
    }

    private static EntityRef? Home(World world, Character c) => world.Settlements.TryGetValue(c.SettlementId, out var s) ? s.Ref : null;

    /// <summary>Pride that will not stay a pupil once it has outgrown the teacher.</summary>
    private static bool Proud(Character c) => c.Has(Traits.Arrogant) || c.Has(Traits.Envious) || c.Has(Traits.Ambitious) || c.Ambition == Ambitions.Renown;

    /// <summary>What passed between them turned them to hatred, the bond's own warmth aside.</summary>
    private static bool Hatred(Character a, Character b) =>
        Relations.Opinion(a, b) - 25 <= Relations.RivalAt || Relations.Opinion(b, a) - 25 <= Relations.RivalAt;

    private static bool RealmsAtWar(World world, Character a, Character b) =>
        world.Settlements.TryGetValue(a.SettlementId, out var sa) && world.Settlements.TryGetValue(b.SettlementId, out var sb)
        && Realms.SovereignOf(world, sa) is { } ra && Realms.SovereignOf(world, sb) is { } rb && ra.Id != rb.Id && MilitarySystem.AtWarWith(world, ra.Id, rb.Id);
}

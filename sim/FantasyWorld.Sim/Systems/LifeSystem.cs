using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Ageing and death (spec §3 "Death and succession"). Monthly, each notable may die of old age,
/// illness, plague, famine or old wounds; the death cites the plague or shortage behind it. Children
/// come of age at 16, choosing an ambition, and some fade into the populace when the world already
/// has enough notables (heirs, lords, the married and artifact holders never fade).
/// </summary>
public sealed class LifeSystem : ISimSystem
{
    public const double NotablesPerSettlement = 4;

    public string Name => "life";

    /// <summary>The faded age and die off-stage, without events: they left the story when they faded.</summary>
    public void OnYearlyTick(SimContext ctx)
    {
        foreach (var c in ctx.World.Characters.Values.Where(c => c.Faded && c.IsAlive && !SagaSystem.Undying(ctx.World, c)))
            if (ctx.Rng.Chance(BaseMortality(Aged(c.Age(ctx.Date), Peoples.SpanOf(ctx.World, c)))))
            {
                c.Died = ctx.Date;
                c.DeathCause = "old age";
            }
    }

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        var target = NotablesPerSettlement * world.Settlements.Values.Count(s => !s.Abandoned);
        var people = world.ActiveCharacters.ToList();
        foreach (var c in people)
        {
            if (!c.IsActive) continue;
            var region = world.Regions[world.Settlements[c.SettlementId].RegionId];
            // A demon lord does not age (sub-project 13).
            var risks = Risks(c, region, ctx.Date, SagaSystem.Undying(world, c) ? 100 : Peoples.SpanOf(world, c));
            var total = risks.Sum(r => r.PerYear);
            // Where the world's own healing resource is held, sickness and age come gentler (the owner, 2026-10-29).
            if (world.Wonders.Count > 0 && Realms.SovereignOf(world, region) is { } home && Wonders.Holds(world, home, WorldResource.Life)) total *= Wonders.LifeMortality;
            // Nor does plague or famine take one (the owner, 2026-10-30): only a blade or a sealing ends a demon lord.
            if (SagaSystem.Undying(world, c)) total = 0;
            // At their people's limit, none lives another month (the owner, 2026-10-29).
            var spent = !SagaSystem.Undying(world, c) && Aged(c.Age(ctx.Date), Peoples.SpanOf(world, c)) >= Peoples.CommonLimit;
            if (spent)
            {
                Fates.Die(ctx, c, "old age", Array.Empty<Cause>());
                continue;
            }
            if (ctx.Rng.Chance(total / 12))
            {
                var (cause, _, citing) = Pick(risks, ctx.Rng);
                Fates.Die(ctx, c, cause, citing == 0 ? Array.Empty<Cause>() : new[] { new Cause(citing, CauseKind.Trigger, 0.9) });
                continue;
            }
            // Past their people's old age, the old fall ill (the owner, 2026-10-29); the sickness stays, and may carry them off.
            if (!c.Wounds.Contains(Sickness) && !SagaSystem.Undying(world, c) && Aged(c.Age(ctx.Date), Peoples.SpanOf(world, c)) >= Peoples.CommonOld
                && ctx.Rng.Chance(SickenPerYear / 12))
            {
                c.Wounds.Add(Sickness);
                ctx.Emit(new EventDraft("FellIll", Actors: new Dictionary<string, EntityRef> { ["who"] = c.Ref }, Place: world.Settlements[c.SettlementId].Ref,
                    Payload: new Dictionary<string, string> { ["age"] = c.Age(ctx.Date).ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    Importance: Fates.Importance(c, 6), Tags: new[] { "Characters" }));
            }
            if (TurnsAgeThisMonth(c, Character.AdultAge, ctx.Date)) ComeOfAge(ctx, c, people.Count / Math.Max(1, target));
        }
    }

    /// <summary>Yearly death risks with their causes and the event each cites (0 for none).</summary>
    /// <summary>Yearly death risks with their causes and the event each cites (0 for none). A people's span stretches
    /// adulthood: a race living twice as long ages half as fast once grown (sub-project 13).</summary>
    public static List<(string Cause, double PerYear, long Citing)> Risks(Character c, Region region, WorldDate today, double span = 1)
    {
        var age = Aged(c.Age(today), span);
        var risks = new List<(string, double, long)> { (age >= Character.ElderAge ? "old age" : "illness", BaseMortality(age), 0) };
        if (region.Conditions.TryGetValue(Conditions.Plague, out var plague)) risks.Add(("plague", 0.08, plague));
        if (region.Conditions.TryGetValue(Conditions.Shortage, out var shortage))
            risks.Add(("famine", age < Character.AdultAge || age >= Character.ElderAge ? 0.06 : 0.03, shortage));
        if (c.Wounds.Contains(Sickness)) risks.Add(("illness", SicknessMortality, 0));
        if (c.Wounds.Count(w => w != Sickness) is var wounds and > 0) risks.Add(("old wounds", 0.01 * wounds, 0));
        return risks;
    }

    /// <summary>The age a grown character's body has reached, for a people living <paramref name="span"/> common lifetimes.</summary>
    public static int Aged(int age, double span) =>
        span == 1 || age < Character.AdultAge ? age : Character.AdultAge + (int)((age - Character.AdultAge) / span);

    /// <summary>Yearly chance of dying of natural causes at this age.</summary>
    public static double BaseMortality(int age) => age switch
    {
        < 5 => 0.02,
        < 16 => 0.005,
        < 40 => 0.006,
        < 50 => 0.01,
        < 60 => 0.02,
        < 65 => 0.04,
        < 70 => 0.06,
        < 75 => 0.1,
        < 80 => 0.15,
        < 85 => 0.25,
        < 90 => 0.4,
        // Near the end of a common lifetime death comes fast, and none outlive its limit (the owner, 2026-10-29).
        < Peoples.CommonLimit => 0.65,
        _ => 1.0,
    };

    /// <summary>What the old fall ill of, kept among their wounds; it makes death likelier.</summary>
    public const string Sickness = "the sickness of age";

    /// <summary>Yearly chance that one past their people's old age falls ill, and the yearly risk of dying of it once ill.</summary>
    public const double SickenPerYear = 0.2, SicknessMortality = 0.15;

    private static (string Cause, double PerYear, long Citing) Pick(List<(string Cause, double PerYear, long Citing)> risks, Rng rng)
    {
        var roll = rng.NextDouble() * risks.Sum(r => r.PerYear);
        foreach (var risk in risks)
        {
            roll -= risk.PerYear;
            if (roll < 0) return risk;
        }
        return risks[^1];
    }

    public static bool TurnsAgeThisMonth(Character c, int years, WorldDate today)
    {
        var threshold = c.BirthDay + (long)years * WorldDate.DaysPerYear;
        return today.Day >= threshold && today.Day - WorldDate.DaysPerMonth < threshold;
    }

    /// <param name="crowding">Notables per target notable (1.0 = exactly as many as wanted).</param>
    private static void ComeOfAge(SimContext ctx, Character c, double crowding)
    {
        var world = ctx.World;
        var heirApparent = Fates.IsHeirApparent(world, c, ctx.Date);
        var keep = Math.Clamp(1.5 - crowding, 0.15, 1.0);
        var bound = heirApparent || c.Role != Role.None || c.SpouseId != 0
                    || world.Artifacts.Values.Any(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id);
        if (!bound && !ctx.Rng.Chance(keep))
        {
            c.Faded = true;
            c.Memories.Clear();
            return;
        }
        c.Ambition = CharacterFactory.ChooseAmbition(c, ctx.Rng);
        c.Class = Classes.Choose(world, c);
        ctx.Emit(new EventDraft("ComingOfAge",
            Actors: new Dictionary<string, EntityRef> { ["character"] = c.Ref },
            Place: world.Settlements[c.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["ambition"] = c.Ambition, ["class"] = c.Class },
            Importance: Fates.Importance(c, heirApparent ? 15 : 5), Tags: new[] { "Characters" }));
    }
}

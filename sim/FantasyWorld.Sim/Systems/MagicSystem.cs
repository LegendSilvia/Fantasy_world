using System.Globalization;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Magic (sub-project 3 §5). Each region's ley power follows a slow tide around its base (see <see cref="Tide"/>, used by
/// <see cref="RegionSystem"/>), so surges cluster at the peaks. Mage towers train mages where the law allows. A surge in
/// a land with a tower may go wild (a storm, a blight or a portal that lets danger in), the more readily where magic is
/// embraced; realms that suffer it tighten their magic law, and arcane realms loosen it. Where magic is outlawed, mages
/// flee to freer realms or are put to death.
/// </summary>
public sealed class MagicSystem : ISimSystem
{
    public const int TideYears = 48;
    public const double TideAmplitude = 12;
    public const double TrainChance = 0.3;
    public const int MagesPerTower = 2;
    public const double FleeChance = 0.25;
    public const double PersecuteChance = 0.05;

    public string Name => "magic";

    /// <summary>Where a region's ley power is drawn at a date: its base, raised and lowered by a tide of its own phase.</summary>
    public static double Tide(Region region, WorldDate date)
    {
        var phase = region.Id * 7919 % TideYears;
        var years = date.Day / (double)WorldDate.DaysPerYear + phase;
        return Math.Clamp(region.BaseLey + TideAmplitude * Math.Sin(2 * Math.PI * years / TideYears), 0, 100);
    }

    /// <summary>Yearly odds that a surge goes wild in a land with a tower, by the law of the realm that holds it.</summary>
    public static double WildOdds(MagicLaw? law) => law switch
    {
        MagicLaw.Embraced => 0.25,
        MagicLaw.Licensed => 0.08,
        MagicLaw.Outlawed => 0.03,
        _ => 0.15,
    };

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        WildMagic(ctx);
        Train(ctx);
        Laws(ctx);
        Persecute(ctx);
    }

    public static bool HasTower(Settlement s) => !s.Abandoned && s.Buildings.Contains("mage tower");

    private static void WildMagic(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var region in world.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.LeySurge)).ToList())
        {
            var tower = world.LivingSettlements(region).Where(HasTower).OrderBy(s => s.Id).FirstOrDefault();
            if (tower is null) continue;
            var law = Realms.SovereignOf(world, region)?.MagicLaw;
            if (!ctx.Rng.Chance(WildOdds(law))) continue;
            var kind = new[] { "storm", "blight", "portal" }[ctx.Rng.NextInt(0, 3)];
            var dead = 0;
            switch (kind)
            {
                case "storm":
                    foreach (var s in world.LivingSettlements(region))
                    {
                        var before = s.Population;
                        Demography.Kill(s, 0.03, ctx.Rng);
                        dead += before - s.Population;
                    }
                    region.Food *= 0.7;
                    break;
                case "blight":
                    region.Fertility = Math.Max(0, region.Fertility - 20);
                    break;
                default:
                    region.Danger = Math.Min(100, region.Danger + 25);
                    break;
            }
            var built = ctx.Log.Involving(tower.Ref).LastOrDefault(e => e.Type == "BuildingCompleted" && e.Payload.GetValueOrDefault("building") == "mage tower");
            var causes = new List<Cause> { new(region.Conditions[Conditions.LeySurge], CauseKind.Trigger, 0.9) };
            if (built is not null) causes.Add(new Cause(built.Id, CauseKind.Enabler, 0.5));
            ctx.Emit(new EventDraft("WildMagic", Place: tower.Ref,
                Payload: new Dictionary<string, string> { ["kind"] = kind, ["dead"] = dead.ToString(CultureInfo.InvariantCulture) },
                Causes: causes, Importance: 40, Tags: new[] { "Magic" }));
        }
    }

    private static void Train(SimContext ctx)
    {
        var world = ctx.World;
        var notables = world.ActiveCharacters.ToList();
        foreach (var tower in world.Settlements.Values.Where(HasTower).ToList())
        {
            if (Realms.SovereignOf(world, world.Regions[tower.RegionId])?.MagicLaw == MagicLaw.Outlawed) continue;
            var here = notables.Where(c => c.SettlementId == tower.Id).ToList();
            if (here.Count(c => c.Role == Role.Mage) >= MagesPerTower || !ctx.Rng.Chance(TrainChance)) continue;
            var pupil = here.Where(c => c.Role == Role.None && c.Stage(ctx.Date) == LifeStage.Adult && c.Attributes.Lore >= 8)
                .OrderByDescending(c => c.Attributes.Lore).ThenBy(c => c.Id).FirstOrDefault();
            if (pupil is null) continue;
            pupil.Role = Role.Mage;
            pupil.Attributes.Lore = Math.Min(20, pupil.Attributes.Lore + 3);
            var built = ctx.Log.Involving(tower.Ref).LastOrDefault(e => e.Type == "BuildingCompleted" && e.Payload.GetValueOrDefault("building") == "mage tower");
            ctx.Emit(new EventDraft("MageTrained", Actors: new Dictionary<string, EntityRef> { ["mage"] = pupil.Ref }, Place: tower.Ref,
                Causes: built is null ? null : new[] { new Cause(built.Id, CauseKind.Enabler, 0.6) },
                Importance: Fates.Importance(pupil, 15), Tags: new[] { "Magic", "Characters" }));
        }
    }

    private static void Laws(SimContext ctx)
    {
        var world = ctx.World;
        var since = ctx.Date.Day - WorldDate.DaysPerYear;
        foreach (var realm in Realms.Independent(world).ToList())
        {
            // Wild magic breaks loose at towers, so the year's outbreaks are found through the realm's towers.
            var wild = Realms.Territory(world, realm).SelectMany(world.LivingSettlements).Where(HasTower)
                .Select(t => ctx.Log.Involving(t.Ref).LastOrDefault(e => e.Type == "WildMagic")?.Id ?? 0)
                .Where(id => id != 0 && ctx.Log.Get(id).Date.Day >= since).DefaultIfEmpty().Max();
            var arcane = world.Faiths.GetValueOrDefault(realm.FaithId)?.Has("arcane") == true || Realms.RulerOf(world, realm)?.Role == Role.Mage;
            if (wild != 0 && realm.MagicLaw != MagicLaw.Outlawed && !arcane && ctx.Rng.Chance(0.5))
                Change(ctx, realm, realm.MagicLaw + 1, new Cause(wild, CauseKind.Trigger, 0.9));
            else if (wild == 0 && arcane && realm.MagicLaw != MagicLaw.Embraced && ctx.Rng.Chance(0.05))
            {
                var why = ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type is "RealmConverted" or "SuccessionOccurred");
                Change(ctx, realm, realm.MagicLaw - 1, why is null ? null : new Cause(why.Id, CauseKind.Enabler, 0.6));
            }
        }
    }

    private static void Change(SimContext ctx, Realm realm, MagicLaw law, Cause? cause)
    {
        var from = realm.MagicLaw;
        realm.MagicLaw = law;
        ctx.Emit(new EventDraft("MagicLawChanged", Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref },
            Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string> { ["from"] = from.ToString().ToLowerInvariant(), ["law"] = law.ToString().ToLowerInvariant() },
            Causes: cause is { } c ? new[] { c } : null, Importance: 35, Tags: new[] { "Magic", "Politics" }));
    }

    private static void Persecute(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var mage in world.ActiveCharacters.Where(c => c.Role == Role.Mage).ToList())
        {
            var home = world.Settlements[mage.SettlementId];
            var realm = Realms.SovereignOf(world, world.Regions[home.RegionId]);
            if (realm?.MagicLaw != MagicLaw.Outlawed) continue;
            var law = ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type == "MagicLawChanged");
            var because = law is null ? Array.Empty<Cause>() : new[] { new Cause(law.Id, CauseKind.Enabler, 0.8) };
            if (ctx.Rng.Chance(FleeChance) && Refuge(world, home) is { } refuge)
            {
                mage.SettlementId = refuge.Id;
                ctx.Emit(new EventDraft("MageFled", Actors: new Dictionary<string, EntityRef> { ["mage"] = mage.Ref, ["from"] = home.Ref },
                    Place: refuge.Ref, Causes: because, Importance: Fates.Importance(mage, 20), Tags: new[] { "Magic", "Characters" }));
            }
            else if (ctx.Rng.Chance(PersecuteChance))
                Fates.Die(ctx, mage, "burned for sorcery", because);
        }
    }

    /// <summary>The nearest living town in a realm that allows magic, by the region graph.</summary>
    private static Settlement? Refuge(World world, Settlement home)
    {
        var graph = world.Geography?.Graph;
        if (graph is null) return null;
        return world.Settlements.Values
            .Where(s => !s.Abandoned && Realms.SovereignOf(world, world.Regions[s.RegionId])?.MagicLaw is MagicLaw.Embraced or MagicLaw.Licensed)
            .Select(s => (Town: s, Way: s.RegionId == home.RegionId ? 0 : graph.ShortestPath(home.RegionId, s.RegionId)?.Days ?? double.MaxValue))
            .Where(x => x.Way <= 60)
            .OrderBy(x => x.Way).ThenByDescending(x => x.Town.Tier).ThenBy(x => x.Town.Id)
            .Select(x => x.Town).FirstOrDefault();
    }
}

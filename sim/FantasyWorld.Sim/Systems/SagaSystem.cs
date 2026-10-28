using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// The demon lord's saga (sub-project 13 N4, the owner's choice: a world-ending saga). Once in a long age, in the darkest
/// land of the world, a demon lord rises and takes it as its Dominion; a prophecy is spoken; a hero is chosen. The
/// Dominion wars on the living lands each year it is at peace, its hosts swollen by hordes, and every realm it touches
/// stands with the defenders. It ends when the hero slays the demon lord (Claude decides the fight), when a sage seals
/// it away at the cost of their own life (Claude chooses the sacrifice), or when the Dominion holds a fifth of the world
/// and a dark age begins.
/// </summary>
public sealed class SagaSystem(IDecider? decider = null, IArbiter? arbiter = null, IInventor? inventor = null) : ISimSystem
{
    public const int FirstAfterYears = 30, QuietYears = 80, HordePerHost = 1500;
    public const double RisePerYear = 0.03, ConfrontPerYear = 0.1, DarkAgeShare = 0.2;

    /// <summary>A hero is ready to seek out the demon lord after this many years, and with this much renown.</summary>
    public const int ReadyYears = 8, ReadyRenown = 70;

    private readonly IDecider _decider = decider ?? new RulesDecider();
    private readonly IArbiter _arbiter = arbiter ?? new RulesArbiter();
    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "saga";

    public void OnMonthlyTick(SimContext ctx) { }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        if (Active(world) is not { } saga)
        {
            var lastEnd = world.Sagas.Values.Select(s => ctx.Log.TryGet(s.EndedEventId, out var e) ? e.Date.Year : 0).DefaultIfEmpty(0).Max();
            if (ctx.Date.Year >= FirstAfterYears && (lastEnd == 0 || ctx.Date.Year - lastEnd >= QuietYears) && world.Regions.Count >= 30
                && ctx.Rng.Chance(RisePerYear))
                Rise(ctx);
            return;
        }
        var dominion = world.Realms[saga.RealmId];
        var demon = world.Characters[saga.DemonLordId];
        if (dominion.Destroyed || !demon.IsActive) { Driven(ctx, saga, demon, dominion); return; }
        if (Realms.Territory(world, dominion).Count() >= world.Regions.Count * DarkAgeShare) { DarkAge(ctx, saga, dominion); return; }
        Hunger(ctx, dominion);
        ChooseHero(ctx, saga, demon);
        if (Confront(ctx, saga, demon, dominion)) return;
        ConsiderSealing(ctx, saga, demon, dominion);
    }

    public static Saga? Active(World world) => world.Sagas.Values.FirstOrDefault(s => s.Active);

    /// <summary>Whether a character is a demon lord of a saga under way (they do not age or die of years).</summary>
    public static bool IsDemonLord(World world, Character c) => world.Sagas.Values.Any(s => s.Active && s.DemonLordId == c.Id);

    /// <summary>The darkest land with a town: the most dangerous and ley-charged, never a realm's seat.</summary>
    public static Region? Darkest(World world)
    {
        var capitals = world.Realms.Values.Where(r => !r.Destroyed).Select(r => world.Settlements[r.CapitalId].RegionId).ToHashSet();
        return world.Regions.Values.Where(r => !capitals.Contains(r.Id) && world.LivingSettlements(r).Any())
            .OrderByDescending(r => r.Danger + r.LeyPower * 0.5).ThenBy(r => r.Id).FirstOrDefault();
    }

    private void Rise(SimContext ctx)
    {
        var world = ctx.World;
        if (Darkest(world) is not { } region) return;
        var seat = world.LivingSettlements(region).OrderByDescending(s => s.Population).ThenBy(s => s.Id).First();
        var realmId = world.Ids.Peek(Realm.Kind);
        var named = _inventor.Invent(new InventionBrief("demon lord", $"saga#{world.Ids.Peek(Saga.Kind)}",
            $"A demon lord rising from the dark land of {region.Name} in year {ctx.Date.Year}, to make it a dominion of terror.",
            $"{new NameGen(ctx.Rng).Proper()} the Undying"));
        var house = CharacterFactory.NewHouse(world, ctx.Rng, region.CultureId, seat.Id);
        var demon = CharacterFactory.Create(world, ctx.Rng, seat.Id, Sex.Male, ctx.Date.Day - 400L * WorldDate.DaysPerYear, house.Id, Role.Lord, name: named.Name);
        demon.Class = Classes.Warlock;
        foreach (var attribute in Attributes.Names) demon.Attributes.Set(attribute, Math.Max(demon.Attributes.Get(attribute), 16));
        (demon.Attributes.Martial, demon.Attributes.Prowess) = (20, 20);
        demon.Traits.Clear();
        demon.Traits.AddRange([Traits.Cruel, Traits.Wrathful, Traits.Ambitious]);
        demon.Renown = 80;
        // A dread weapon in its hand (sub-project 13): the hero must be ready to face it.
        var weapon = ArtifactForge.Create(ctx.Rng, world.Ids, new NameGen(ctx.Rng), ctx.Log.NextId, fame: 70); // born with the rising, the next event
        world.Artifacts[weapon.Id] = weapon;

        var lands = new List<Region> { region };
        lands.AddRange((world.Geography?.Graph.Neighbours(region.Id) ?? []).Select(id => world.Regions[id])
            .Where(r => r.Danger >= 40 && !world.Realms.Values.Any(k => !k.Destroyed && world.Settlements[k.CapitalId].RegionId == r.Id))
            .OrderByDescending(r => r.Danger).ThenBy(r => r.Id).Take(2));
        var actors = new Dictionary<string, EntityRef> { ["demon lord"] = demon.Ref, ["realm"] = new(Realm.Kind, realmId) };
        if (Realms.SovereignOf(world, region) is { } fallen) actors["from"] = fallen.Ref;
        WorldEvent rose;
        using (ctx.Annotate("lore", named.Lore))
            rose = ctx.Emit(new EventDraft("DemonLordRose", Actors: actors, Place: seat.Ref,
                Payload: new Dictionary<string, string>
                {
                    ["name"] = named.Name,
                    // For the history replay (Views.History): the lands the Dominion took.
                    [RevolutionSystem.RegionIds] = string.Join(",", lands.Select(l => l.Id.ToString(CultureInfo.InvariantCulture))),
                },
                Causes: Darkness(ctx, region, lands),
                Importance: 95, Tags: new[] { "Magic", "War", "Politics" }));
        var treasury = world.Realms.Values.Where(r => !r.Destroyed && r.LiegeId == 0).Select(r => Math.Max(0, r.Treasury)).DefaultIfEmpty(1000).Average() * 2;
        var dominion = new Realm
        {
            Id = world.Ids.Take(Realm.Kind), Name = region.Name, CapitalId = seat.Id, Government = Government.Dominion,
            SuccessionLaw = SuccessionLaw.Seniority, MagicLaw = MagicLaw.Embraced, Legitimacy = 60, Stability = 60, Treasury = treasury,
            CultureId = region.CultureId, FaithId = region.FaithId, Colors = ["sable", "crimson"], FoundedEventId = rose.Id,
            RegionsAtLastReview = lands.Count,
        };
        world.Realms[dominion.Id] = dominion;
        foreach (var land in lands)
        {
            if (world.Realms.TryGetValue(land.OwnerRealmId, out var owner) && !owner.Destroyed) Statecraft.Remember(owner, rose.Id, dominion.Id, -90, 100);
            land.OwnerRealmId = dominion.Id;
            land.OccupierRealmId = 0;
            land.Danger = Math.Min(100, land.Danger + 20);
        }
        if (seat.LordId != 0 && world.Characters.TryGetValue(seat.LordId, out var ousted)) ousted.Role = Role.None;
        seat.LordId = demon.Id;
        house.SeatSettlementId = seat.Id;
        dominion.Rank = Realms.RankFor(world, dominion);

        var saga = new Saga { Id = world.Ids.Take(Saga.Kind), DemonLordId = demon.Id, RealmId = dominion.Id, RoseEventId = rose.Id };
        world.Sagas[saga.Id] = saga;
        Fates.MoveToHolder(weapon, demon, rose.Id, "wielded");
        var prophecy = _inventor.Invent(new InventionBrief("prophecy", saga.Ref.ToString(),
            $"The prophecy spoken when {named.Name} rose in {region.Name}: of the hero who will one day stand against the demon lord.",
            $"When the dark rises in {region.Name}, a blade unlooked-for shall find its heart."));
        saga.ProphecyEventId = ctx.Emit(new EventDraft("ProphecySpoken",
                Actors: new Dictionary<string, EntityRef> { ["demon lord"] = demon.Ref }, Place: seat.Ref,
                Payload: new Dictionary<string, string> { ["prophecy"] = prophecy.Lore ?? prophecy.Name },
                Causes: new[] { new Cause(rose.Id, CauseKind.Trigger, 1.0) }, Importance: 70, Tags: new[] { "Faith", "Magic" })).Id;
    }

    /// <summary>The Dominion at peace hungers: it wars on its weakest neighbour, and every realm it touches stands against it.</summary>
    private void Hunger(SimContext ctx, Realm dominion)
    {
        var world = ctx.World;
        if (DiplomacySystem.AtWar(world, dominion)) return;
        var prey = Realms.Neighbours(world, dominion).Where(r => r.LiegeId == 0 && !r.Destroyed)
            .OrderBy(r => DiplomacySystem.Strength(world, r)).ThenBy(r => r.Id).FirstOrDefault();
        if (prey is null) return;
        var cause = Active(world)!.RoseEventId;
        var war = DiplomacySystem.Declare(ctx, _inventor, dominion,
            new CasusBelliOption(CasusBelli.DarkConquest, prey.Id, cause, world.Settlements[prey.CapitalId].RegionId, 0, 0));
        foreach (var ally in Realms.Neighbours(world, dominion).Where(r => r.LiegeId == 0 && !r.Destroyed && !war.Involves(r.Id)).OrderBy(r => r.Id).ToList())
        {
            war.Defenders.Add(ally.Id);
            ctx.Emit(new EventDraft("AllyJoinedWar",
                Actors: new Dictionary<string, EntityRef> { ["ally"] = ally.Ref, ["war"] = war.Ref },
                Payload: new Dictionary<string, string> { ["side"] = "defenders" }, Place: world.Settlements[ally.CapitalId].Ref,
                Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Trigger, 1.0) }, Importance: 45, Tags: new[] { "Politics", "War" }));
        }
    }

    /// <summary>The hero foretold: the mightiest adventurer or knight beyond the Dominion, sworn against the demon lord.</summary>
    private static void ChooseHero(SimContext ctx, Saga saga, Character demon)
    {
        var world = ctx.World;
        if (world.Characters.TryGetValue(saga.HeroId, out var current) && current.IsActive)
        {
            current.Renown = Math.Min(100, current.Renown + 4); // the hero's legend grows as they make ready
            return;
        }
        var dark = Realms.Territory(world, world.Realms[saga.RealmId]).Select(r => r.Id).ToHashSet();
        var hero = world.ActiveCharacters
            .Where(c => c.Role is Role.Adventurer or Role.Knight && c.Stage(ctx.Date) == LifeStage.Adult
                        && !dark.Contains(world.Settlements[c.SettlementId].RegionId))
            .OrderByDescending(c => c.Attributes.Prowess * 2 + c.Attributes.Martial + c.Renown / 5 + (c.Has(Traits.Brave) ? 5 : 0)).ThenBy(c => c.Id)
            .FirstOrDefault();
        if (hero is null) return;
        saga.HeroId = hero.Id;
        hero.Renown = Math.Min(100, hero.Renown + 20);
        hero.Ambition = Ambitions.Vengeance;
        hero.AmbitionTarget = demon.Id;
        ctx.Emit(new EventDraft("HeroChosen",
            Actors: new Dictionary<string, EntityRef> { ["hero"] = hero.Ref, ["demon lord"] = demon.Ref },
            Place: world.Settlements[hero.SettlementId].Ref,
            Causes: new[] { new Cause(saga.ProphecyEventId != 0 ? saga.ProphecyEventId : saga.RoseEventId, CauseKind.Trigger, 0.9) },
            Importance: 75, Tags: new[] { "Characters", "Magic" }));
    }

    /// <summary>The hero seeks out the demon lord: a fight to the death that Claude decides. True if the saga ended.</summary>
    private bool Confront(SimContext ctx, Saga saga, Character demon, Realm dominion)
    {
        var world = ctx.World;
        if (!world.Characters.TryGetValue(saga.HeroId, out var hero) || !hero.IsActive) return false;
        var chosen = ctx.Log.Involving(hero.Ref).LastOrDefault(e => e.Type == "HeroChosen");
        var ready = hero.Renown >= ReadyRenown && chosen is not null && ctx.Date.Year - chosen.Date.Year >= ReadyYears;
        if (!ready || !ctx.Rng.Chance(ConfrontPerYear)) return false;
        var outcome = _arbiter.Resolve(Encounters.DuelBetween(world, hero, demon, toTheDeath: true), ctx.Rng);
        WorldEvent fight;
        using (ctx.Annotate("account", outcome.Account))
            fight = ctx.Emit(new EventDraft(outcome.AttackerWins ? "DemonLordSlain" : "HeroFell",
                Actors: new Dictionary<string, EntityRef> { ["hero"] = hero.Ref, ["demon lord"] = demon.Ref },
                Place: world.Settlements[dominion.CapitalId].Ref, Payload: new Dictionary<string, string> { ["outcome"] = outcome.Key },
                Causes: new[] { new Cause(ctx.Log.Involving(hero.Ref).LastOrDefault(e => e.Type == "HeroChosen")?.Id ?? saga.RoseEventId, CauseKind.Trigger, 1.0) },
                Importance: outcome.AttackerWins ? 98 : 80, Tags: new[] { "Characters", "Deeds", "Magic" }));
        if (!outcome.AttackerWins)
        {
            if (outcome.AttackerFate == Fate.Dead) Fates.Die(ctx, hero, "the demon lord", new[] { new Cause(fight.Id, CauseKind.Trigger, 1.0) }, demon);
            else Fates.Remember(hero, fight.Id, demon.Id, -100, 100);
            saga.HeroId = 0;
            return false;
        }
        hero.Renown = 100;
        Fates.Die(ctx, demon, "a hero's blade", new[] { new Cause(fight.Id, CauseKind.Trigger, 1.0) }, hero);
        Fall(ctx, dominion, fight);
        End(ctx, saga, Saga.Slain, new List<Cause> { new(fight.Id, CauseKind.Trigger, 1.0) });
        return true;
    }

    /// <summary>
    /// After long terror (fifteen years), or once the Dominion is beaten back to its seat, the most learned sage beyond it
    /// may seal the demon lord away, giving their life to do it (Claude chooses).
    /// </summary>
    private void ConsiderSealing(SimContext ctx, Saga saga, Character demon, Realm dominion)
    {
        var world = ctx.World;
        var years = ctx.Date.Year - ctx.Log.Get(saga.RoseEventId).Date.Year;
        var beaten = Realms.Territory(world, dominion).Count() <= 1;
        if (years < 15 && !beaten) return;
        var dark = Realms.Territory(world, dominion).Select(r => r.Id).ToHashSet();
        var sage = world.ActiveCharacters
            .Where(c => c.Role is Role.Mage or Role.Priest && c.Attributes.Lore >= 15 && !dark.Contains(world.Settlements[c.SettlementId].RegionId))
            .OrderByDescending(c => c.Attributes.Lore).ThenByDescending(c => c.Renown).ThenBy(c => c.Id).FirstOrDefault();
        if (sage is null) return;
        var choice = _decider.Decide(new DecisionBrief(sage.Id, "seal the demon lord", new[]
        {
            new DecisionOption("seal", 10 + years + (beaten ? 40 : 0) + (sage.Has(Traits.Brave) ? 15 : 0), $"give their life to seal {demon.Name} away"),
            new DecisionOption("wait", 60 + (sage.Has(Traits.Craven) ? 30 : 0), "wait, and hope the hero foretold prevails"),
        }), ctx.Rng);
        if (choice.Key != "seal") return;
        WorldEvent sealed_;
        using (ctx.Annotate("motive", choice.Reasoning))
            sealed_ = ctx.Emit(new EventDraft("DemonLordSealed",
                Actors: new Dictionary<string, EntityRef> { ["sealer"] = sage.Ref, ["demon lord"] = demon.Ref },
                Place: world.Settlements[dominion.CapitalId].Ref, Causes: new[] { new Cause(saga.RoseEventId, CauseKind.Enabler, 0.8) },
                Importance: 96, Tags: new[] { "Characters", "Magic" }));
        Fates.Die(ctx, sage, "the sealing", new[] { new Cause(sealed_.Id, CauseKind.Trigger, 1.0) });
        // The sealed lord leaves the story, not the world: it may be told of again.
        Vanish(world, demon, sealed_);
        Fall(ctx, dominion, sealed_);
        End(ctx, saga, Saga.Sealed, new List<Cause> { new(sealed_.Id, CauseKind.Trigger, 1.0) });
    }

    /// <summary>
    /// The living realms broke the Dominion, or its lord met its end some other way: the saga ends with the demon lord
    /// driven into the dark (or dead), its Dominion broken.
    /// </summary>
    private static void Driven(SimContext ctx, Saga saga, Character demon, Realm dominion)
    {
        var world = ctx.World;
        var place = ctx.Log.Get(saga.RoseEventId).Place;
        var driven = ctx.Emit(new EventDraft("DemonLordDriven",
            Actors: new Dictionary<string, EntityRef> { ["demon lord"] = demon.Ref, ["realm"] = dominion.Ref }, Place: place,
            Payload: new Dictionary<string, string> { ["fate"] = demon.IsAlive ? "driven" : "dead" },
            Causes: new[] { new Cause(ctx.Log.Involving(dominion.Ref).LastOrDefault()?.Id ?? saga.RoseEventId, CauseKind.Trigger, 0.9) },
            Importance: 90, Tags: new[] { "Magic", "War" }));
        if (demon.IsAlive) Vanish(world, demon, driven);
        Fall(ctx, dominion, driven);
        End(ctx, saga, demon.IsAlive ? Saga.Driven : Saga.Slain, new List<Cause> { new(driven.Id, CauseKind.Trigger, 1.0) });
    }

    /// <summary>A demon lord gone from the story (sealed or driven into the dark): its seats are empty, and what it held is
    /// lost in the land it ruled, to be found again.</summary>
    private static void Vanish(World world, Character demon, WorldEvent gone)
    {
        foreach (var seat in world.Settlements.Values.Where(s => s.LordId == demon.Id)) seat.LordId = 0;
        foreach (var held in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == demon.Id).ToList())
            Fates.Lose(held, world.Settlements[demon.SettlementId].RegionId, gone.Id, demon.Id);
        demon.Role = Role.None;
        demon.Faded = true;
    }

    private static void DarkAge(SimContext ctx, Saga saga, Realm dominion)
    {
        var dark = ctx.Emit(new EventDraft("DarkAgeBegan",
            Actors: new Dictionary<string, EntityRef> { ["demon lord"] = new(Character.Kind, saga.DemonLordId), ["realm"] = dominion.Ref },
            Place: ctx.World.Settlements[dominion.CapitalId].Ref, Causes: new[] { new Cause(saga.RoseEventId, CauseKind.Enabler, 0.9) },
            Importance: 97, Tags: new[] { "Politics", "Era", "Magic" }));
        End(ctx, saga, Saga.Rules, new List<Cause> { new(dark.Id, CauseKind.Trigger, 1.0) });
    }

    /// <summary>With its lord gone the Dominion breaks: its lands pass to those who hold them or border them.</summary>
    private static void Fall(SimContext ctx, Realm dominion, WorldEvent cause)
    {
        if (!dominion.Destroyed) Statecraft.Fall(ctx, dominion, new[] { new Cause(cause.Id, CauseKind.Trigger, 1.0) });
        foreach (var region in ctx.World.Regions.Values.Where(r => r.Conditions.ContainsKey(Conditions.Dangerous)))
            region.Danger = Math.Max(0, region.Danger - 5);
    }

    private static void End(SimContext ctx, Saga saga, string outcome, List<Cause> causes)
    {
        causes.Add(new Cause(saga.RoseEventId, CauseKind.Enabler, 0.7));
        saga.Outcome = outcome;
        saga.EndedEventId = ctx.Emit(new EventDraft("SagaEnded",
            Actors: new Dictionary<string, EntityRef> { ["demon lord"] = new(Character.Kind, saga.DemonLordId) },
            Place: ctx.Log.Get(saga.RoseEventId).Place, Payload: new Dictionary<string, string> { ["outcome"] = outcome },
            Causes: causes.Take(3).ToList(), Importance: 30, Tags: new[] { "Era" })).Id;
    }

    /// <summary>
    /// Why a demon lord rose where it did: the ley surging and danger in its land, else the latest of its beasts' risings
    /// or raids there (the darkness the chronicle can name).
    /// </summary>
    private static IReadOnlyList<Cause> Darkness(SimContext ctx, Region region, IReadOnlyCollection<Region> lands)
    {
        var cited = Conditions.Cite(region.Conditions, (Conditions.LeySurge, CauseKind.Trigger, 0.8), (Conditions.Dangerous, CauseKind.Enabler, 0.7));
        if (cited.Count > 0) return cited;
        var ids = lands.Select(l => l.Id).Append(region.Id).ToHashSet();
        var settlements = ctx.World.Settlements.Values.Where(s => ids.Contains(s.RegionId)).Select(s => s.Ref).ToHashSet();
        var beasts = ctx.World.Monsters.Values.Where(m => ids.Contains(m.RegionId)).Select(m => m.RoseEventId).Where(id => id != 0).ToHashSet();
        var dark = ctx.Log.All.LastOrDefault(e => beasts.Contains(e.Id)
                                                 || e.Type == "MonsterRaid" && e.Place is { } at && settlements.Contains(at));
        return dark is null ? cited : new[] { new Cause(dark.Id, CauseKind.Enabler, 0.6) };
    }
}

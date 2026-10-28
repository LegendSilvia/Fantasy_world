using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Festivals (sub-project 13 N6). Every nation has a festival of its own, made from its race, faith, land and founding
/// (Claude invents it when there is an answer; the rules' tables otherwise). A nation without one founds it; each year,
/// in its month, the festival is held at the capital and steadies the realm a little. A realm remade (its government
/// changed) reshapes it.
/// </summary>
public sealed class FestivalSystem(IInventor? inventor = null) : ISimSystem
{
    public const double Steadies = 2;

    private readonly IInventor _inventor = inventor ?? new RulesInventor();

    public string Name => "festivals";

    private static readonly (string Name, string Rite)[] Rites =
    [
        ("Feast of Lanterns", "lanterns are set adrift on every river and pool"),
        ("Night of Masks", "the people go masked through the streets from dusk to dawn"),
        ("Ember Days", "bonfires burn on every hill for three nights"),
        ("Tourney of Crowns", "champions joust and wrestle before the throne"),
        ("Long Silence", "a day of fasting and silence, broken at nightfall by bells"),
        ("Breaking of Bread", "the old year's bread is broken and shared in the streets"),
        ("Dawn Chorus", "songs are sung at dawn from the walls and towers"),
        ("Running of Horses", "riders race across the plains from town to town"),
        ("Festival of Kites", "kites in the realm's colours are flown from every height"),
        ("Gathering of Names", "the names of the dead are read aloud and remembered"),
        ("Festival of Mirrors", "mirrors and polished shields are turned to catch the first light"),
        ("Procession of Beasts", "carved and painted beasts are carried through the capital"),
    ];

    public void OnMonthlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var realm in Realms.Independent(world).OrderBy(r => r.Id).ToList())
        {
            if (realm.Festival is null) { Found(ctx, realm, "FestivalFounded"); continue; }
            if (realm.Festival.Month != ctx.Date.Month) continue;
            realm.Stability = Math.Min(100, realm.Stability + Steadies);
            ctx.Emit(new EventDraft("FestivalHeld",
                Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: world.Settlements[realm.CapitalId].Ref,
                Payload: new Dictionary<string, string> { ["festival"] = realm.Festival.Name, ["rite"] = realm.Festival.Rite },
                Importance: 10, Tags: new[] { "Culture" }));
        }
    }

    public void OnYearlyTick(SimContext ctx)
    {
        // A realm remade by revolution keeps its people's feast, but under a new form.
        foreach (var realm in Realms.Independent(ctx.World).Where(r => r.Festival is not null).OrderBy(r => r.Id).ToList())
            if (ctx.Log.Involving(realm.Ref).LastOrDefault(e => e.Type == "RevolutionEnded" && e.Payload.ContainsKey("government")) is { } remade
                && remade.Date.Year == ctx.Date.Year - 1)
                Found(ctx, realm, "FestivalReshaped");
    }

    /// <summary>The rules' festival for a realm: a rite of its own, in honour of its god, its land or its founding.</summary>
    public static Festival Make(World world, Realm realm, Rng rng)
    {
        var taken = world.Realms.Values.Where(r => r.Festival is not null && r.Id != realm.Id).Select(r => r.Festival!.Name).ToHashSet();
        var rites = Rites.Where(r => !taken.Contains(r.Name)).ToList();
        var (name, rite) = rites.Count > 0 ? rites[rng.NextInt(0, rites.Count)] : Rites[rng.NextInt(0, Rites.Length)];
        var faith = world.Faiths.GetValueOrDefault(realm.FaithId);
        var race = Peoples.Of(world, realm);
        var region = world.Regions[world.Settlements[realm.CapitalId].RegionId];
        var honours = rng.NextInt(0, 4) switch
        {
            0 when faith is not null => $"in honour of {faith.Deity}",
            1 when race is not null => race.Temper == Race.Warlike ? "for the war-dead of the " + race.Name : $"for the first days of the {race.Name}",
            2 => region.Coastal ? "for the sea that feeds the realm" : "for the harvest of the land",
            _ => $"for the founding of {realm.Name}",
        };
        return new Festival(taken.Contains(name) ? $"{name} of {realm.Name}" : name, rng.NextInt(1, 13), rite, honours);
    }

    /// <summary>Claude's festival for a realm, or the rules' when there is no answer (subject: the realm and the year).</summary>
    public static Festival Invent(World world, Realm realm, Rng rng, IInventor inventor, int year)
    {
        var rules = Make(world, realm, rng);
        var race = Peoples.Of(world, realm);
        var faith = world.Faiths.GetValueOrDefault(realm.FaithId);
        var facts = $"The festival of {Statecraft.Title(realm)}"
                    + (race is not null ? $", a realm of the {race.Name} ({race.Nature})" : "")
                    + (faith is not null ? $", whose people keep {faith.Name} and worship {faith.Deity}" : "")
                    + $". It falls in month {rules.Month.ToString(CultureInfo.InvariantCulture)}: {rules.Rite}, {rules.Honours}. Unique to this nation.";
        var invented = inventor.Invent(new InventionBrief("festival", $"{realm.Ref}@{year}", facts, rules.Name));
        return invented.Name == rules.Name && invented.Lore is null ? rules : rules with { Name = invented.Name, Lore = invented.Lore };
    }

    private void Found(SimContext ctx, Realm realm, string type)
    {
        var festival = Invent(ctx.World, realm, ctx.Rng, _inventor, ctx.Date.Year);
        realm.Festival = festival;
        using var lore = ctx.Annotate("lore", festival.Lore);
        ctx.Emit(new EventDraft(type,
            Actors: new Dictionary<string, EntityRef> { ["realm"] = realm.Ref }, Place: ctx.World.Settlements[realm.CapitalId].Ref,
            Payload: new Dictionary<string, string>
            {
                ["festival"] = festival.Name, ["rite"] = festival.Rite, ["honours"] = festival.Honours,
                ["month"] = festival.Month.ToString(CultureInfo.InvariantCulture),
            },
            Causes: realm.FoundedEventId != 0 ? new[] { new Cause(realm.FoundedEventId, CauseKind.Enabler, 0.5) } : null,
            Importance: type == "FestivalFounded" ? 22 : 30, Tags: new[] { "Culture" }));
    }
}

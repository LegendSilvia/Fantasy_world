using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Mercenaries (the owner, 2026-10-30): a realm at war hires fighters from among the world's characters. Only one who feels
/// at least neutral toward it will take its coin (<see cref="Toward"/>), never a subject of a realm it fights, never a lord;
/// the strongest willing come first. A host hires one when it takes the field, and more to break a fortified town: the
/// higher its fortification (the town's level), the more it takes, three at most. They ride with the host like its knights,
/// cost a fee by their level and their keep each month, and go home when the host disbands.
/// </summary>
public sealed partial class MilitarySystem
{
    /// <summary>At most this many mercenaries ride with one host.</summary>
    public const int MostMercenaries = 3;

    /// <summary>The fee to hire one: a base and more for each level.</summary>
    public const double MercenaryFee = 40, MercenaryFeePerLevel = 15, MercenaryKeep = 4;

    /// <summary>The mercenaries a host needs to break a town of this level (its fortification): none for open ground, three for the strongest.</summary>
    public static int MercenariesFor(int townLevel) => townLevel switch { <= 1 => 0, 2 => 1, 3 or 4 => 2, _ => MostMercenaries };

    /// <summary>
    /// What a character feels toward a realm, −100…100: a subject leans toward their own; what they think of its ruler counts
    /// half; a shared faith a little. A subject of a realm at war with it is its enemy.
    /// </summary>
    public static double Toward(World world, Character c, Realm realm)
    {
        var home = world.Settlements.TryGetValue(c.SettlementId, out var s) ? Realms.SovereignOf(world, s) : null;
        if (home is not null && home.Id != realm.Id && AtWarWith(world, home.Id, realm.Id)) return -100;
        var feeling = home?.Id == realm.Id ? 25.0 : 0;
        if (Realms.RulerOf(world, realm) is { IsActive: true } ruler && ruler.Id != c.Id) feeling += Relations.Opinion(c, ruler) / 2;
        if (c.FaithId != 0 && c.FaithId == realm.FaithId) feeling += 10;
        return Math.Clamp(feeling, -100, 100);
    }

    private static bool Serving(World world, long characterId) =>
        Active(world).Any(a => a.CommanderId == characterId || a.Knights.Contains(characterId) || a.Mercenaries.Contains(characterId));

    /// <summary>Hires mercenaries for a host until it has <paramref name="wanted"/> (at most three), as its realm can pay.</summary>
    public static void Hire(SimContext ctx, War war, Army army, int wanted, EntityRef? against = null)
    {
        var world = ctx.World;
        var realm = world.Realms[army.RealmId];
        wanted = Math.Min(wanted, MostMercenaries);
        army.Mercenaries.RemoveAll(id => !world.Characters.TryGetValue(id, out var m) || !m.IsActive);
        while (army.Mercenaries.Count < wanted)
        {
            var hired = world.ActiveCharacters
                .Where(c => c.Stage(ctx.Date) == LifeStage.Adult && c.Role is not Role.Lord && !Serving(world, c.Id)
                            && !DeedsSystem.Engaged(ctx.Queue, c.Id) && Toward(world, c, realm) >= 0)
                .Select(c => (c, Fee: MercenaryFee + MercenaryFeePerLevel * Levels.Of(c)))
                .Where(x => x.Fee <= realm.Treasury)
                .OrderByDescending(x => Levels.Of(x.c)).ThenByDescending(x => Encounters.Strength(world, x.c)).ThenBy(x => x.c.Id)
                .Select(x => (Character?)x.c).FirstOrDefault();
            if (hired is null) return;
            var fee = MercenaryFee + MercenaryFeePerLevel * Levels.Of(hired);
            realm.Treasury -= fee;
            army.Mercenaries.Add(hired.Id);
            var actors = new Dictionary<string, EntityRef> { ["mercenary"] = hired.Ref, ["realm"] = realm.Ref, ["army"] = army.Ref, ["war"] = war.Ref };
            if (against is { } town) actors["against"] = town;
            ctx.Emit(new EventDraft("MercenaryHired", Actors: actors, Place: world.Regions[army.RegionId].Ref,
                Payload: new Dictionary<string, string>
                {
                    ["level"] = Levels.Of(hired).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["fee"] = ((int)fee).ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                Causes: new[] { new Cause(war.DeclaredEventId, CauseKind.Enabler, 0.6) },
                Importance: Fates.Importance(hired, 22), Tags: new[] { "War", "Characters" }));
        }
    }
}

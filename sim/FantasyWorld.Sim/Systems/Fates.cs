using System.Globalization;
using FantasyWorld.Sim.Agency;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Shared consequences used by several character systems: death with succession and inheritance,
/// wounds, memories and moving artifacts. A helper library, not a system: it never runs on its own.
/// </summary>
public static class Fates
{
    /// <summary>Settlement condition: the seat is empty; maps to the death that emptied it.</summary>
    public const string VacantSeat = "vacantSeat";

    private static readonly string[] WoundSites = { "sword arm", "left leg", "right eye", "ribs", "face", "hand" };

    /// <summary>Importance of an event about this character: base + renown/2, lords +20.</summary>
    public static int Importance(Character c, int baseline) =>
        Math.Clamp(baseline + (int)(c.Renown / 2) + (c.Role == Role.Lord ? 20 : 0), 0, 100);

    public static void Remember(Character c, long eventId, long about, double feeling, double weight) =>
        c.Memories.Add(new Memory(eventId, about, feeling, weight));

    /// <summary>
    /// Kills a character: the death event, grief and (if killed) vengeance among kin, the end of a
    /// marriage, succession to their seat, and inheritance of what they held — unless they died in the
    /// wilds (lostInRegion), where what they carried is lost to be found again by delvers.
    /// </summary>
    public static WorldEvent Die(SimContext ctx, Character c, string cause, IReadOnlyList<Cause> causes,
        Character? killer = null, long lostInRegion = 0)
    {
        var world = ctx.World;
        var actors = new Dictionary<string, EntityRef> { ["deceased"] = c.Ref };
        if (killer is not null) actors["killer"] = killer.Ref;
        var death = ctx.Emit(new EventDraft("CharacterDied", Actors: actors,
            Place: world.Settlements[c.SettlementId].Ref,
            Payload: new Dictionary<string, string>
            {
                ["cause"] = cause,
                ["age"] = c.Age(ctx.Date).ToString(CultureInfo.InvariantCulture),
            },
            Causes: causes, Importance: Importance(c, 15), Tags: new[] { "Characters" }));
        c.Died = ctx.Date;
        c.DeathCause = cause;
        c.Memories.Clear();

        var widow = Living(world, c.SpouseId);
        if (widow is not null)
        {
            widow.SpouseId = 0;
            Remember(widow, death.Id, killer?.Id ?? 0, -60, 60);
        }
        c.SpouseId = 0;

        var kinIds = c.Children.Concat(new[] { c.FatherId, c.MotherId }).Concat(Siblings(world, c).Select(k => k.Id));
        foreach (var kin in kinIds.Where(id => id != 0 && id != killer?.Id).Distinct().OrderBy(id => id).Select(id => world.Characters[id]).Where(k => k.IsActive))
        {
            Remember(kin, death.Id, killer?.Id ?? 0, -50, 50);
            if (killer is not null && killer.IsAlive && kin.Stage(ctx.Date) != LifeStage.Child && !kin.Has(Traits.Craven))
            {
                kin.Ambition = Ambitions.Vengeance;
                kin.AmbitionTarget = killer.Id;
            }
        }

        foreach (var realm in world.Realms.Values)
            foreach (var seat in realm.Council.Where(s => s.Value == c.Id).Select(s => s.Key).ToList()) realm.Council.Remove(seat);
        foreach (var army in world.Armies.Values.Where(a => !a.Disbanded))
        {
            army.Knights.Remove(c.Id);
            if (army.CommanderId == c.Id) army.CommanderId = 0;
        }

        var heir = Heir(world, c, ctx.Date, killer);
        foreach (var seat in world.Settlements.Values.Where(s => s.LordId == c.Id).ToList())
            Succeed(ctx, seat, c, HeirCandidates(world, c, killer).FirstOrDefault(k => CanTakeSeat(world, k)), death);
        c.Role = Role.None;

        foreach (var artifact in world.Artifacts.Values.Where(a => a.Place == ArtifactPlace.Held && a.HolderId == c.Id).ToList())
        {
            if (lostInRegion != 0) Lose(artifact, lostInRegion, death.Id, c.Id);
            else Inherit(ctx, artifact, c, heir ?? widow, death);
        }
        return death;
    }

    /// <summary>Eldest active child, else eldest active sibling (never the killer). Faded relatives have left the story.</summary>
    public static Character? Heir(World world, Character c, Core.WorldDate today, Character? killer = null) =>
        HeirCandidates(world, c, killer).FirstOrDefault();

    /// <summary>Line of succession: active children by age, then active siblings by age, excluding the killer.</summary>
    public static IEnumerable<Character> HeirCandidates(World world, Character c, Character? killer = null) =>
        c.Children.Select(id => world.Characters[id]).Where(k => k.IsActive && k.Id != killer?.Id)
            .OrderBy(k => k.BirthDay).ThenBy(k => k.Id)
            .Concat(Siblings(world, c).Where(k => k.IsActive && k.Id != killer?.Id).OrderBy(k => k.BirthDay).ThenBy(k => k.Id));

    /// <summary>A seat needs a lord who can live there: not already a lord, not married to one.</summary>
    public static bool CanTakeSeat(World world, Character k) =>
        k.Role != Role.Lord && !(world.Characters.TryGetValue(k.SpouseId, out var spouse) && spouse.Role == Role.Lord);

    /// <summary>Children of either parent, other than c (found through parents, not by scanning everyone).</summary>
    public static IEnumerable<Character> Siblings(World world, Character c) =>
        new[] { c.FatherId, c.MotherId }.Where(p => p != 0).Distinct()
            .SelectMany(p => world.Characters[p].Children).Where(id => id != c.Id).Distinct()
            .Select(id => world.Characters[id]);

    /// <summary>Whether c would inherit a seat today: only a lord's child or sibling can.</summary>
    public static bool IsHeirApparent(World world, Character c, Core.WorldDate today) =>
        new[] { c.FatherId, c.MotherId }.Where(p => p != 0).Select(p => world.Characters[p])
            .Concat(Siblings(world, c))
            .Any(k => k.Role == Role.Lord && Heir(world, k, today)?.Id == c.Id);

    private static void Succeed(SimContext ctx, Settlement seat, Character predecessor, Character? heir, WorldEvent death)
    {
        var world = ctx.World;
        if (heir is null)
        {
            seat.LordId = 0;
            seat.Conditions[VacantSeat] = death.Id;
            return;
        }
        heir.Role = Role.Lord;
        heir.SettlementId = seat.Id;
        seat.LordId = heir.Id;
        if (world.Characters.TryGetValue(heir.SpouseId, out var consort)) consort.SettlementId = seat.Id;
        if (world.Houses.TryGetValue(heir.HouseId, out var house)) house.SeatSettlementId = seat.Id;
        ctx.Emit(new EventDraft("SuccessionOccurred",
            Actors: new Dictionary<string, EntityRef> { ["heir"] = heir.Ref, ["predecessor"] = predecessor.Ref },
            Place: seat.Ref,
            Causes: new[] { new Cause(death.Id, CauseKind.Trigger, 1.0) },
            Importance: seat.Tier switch { SettlementTier.City => 55, SettlementTier.Town => 40, _ => 20 },
            Tags: new[] { "Characters", "Politics" }));
    }

    private static void Inherit(SimContext ctx, Artifact artifact, Character from, Character? to, WorldEvent death)
    {
        var settlement = ctx.World.Settlements[from.SettlementId];
        var ev = ctx.Emit(new EventDraft("ArtifactInherited",
            Actors: to is null
                ? new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref }
                : new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["heir"] = to.Ref },
            Place: settlement.Ref,
            Causes: new[] { new Cause(death.Id, CauseKind.Trigger, 1.0) },
            Importance: (int)Math.Clamp(10 + artifact.Fame / 3, 0, 100), Tags: new[] { "Artifacts" }));
        if (to is null) MoveToTreasury(artifact, settlement.Id, ev.Id, from.Id, "inherited");
        else MoveToHolder(artifact, to, ev.Id, "inherited");
    }

    public static void MoveToHolder(Artifact artifact, Character to, long eventId, string how)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, artifact.Place == ArtifactPlace.Held ? artifact.HolderId : 0, to.Id, how));
        artifact.Place = ArtifactPlace.Held;
        artifact.HolderId = to.Id;
        artifact.SettlementId = 0;
        artifact.RegionId = 0;
    }

    public static void MoveToTreasury(Artifact artifact, long settlementId, long eventId, long fromId, string how)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, fromId, 0, how));
        artifact.Place = ArtifactPlace.Treasury;
        artifact.HolderId = 0;
        artifact.SettlementId = settlementId;
        artifact.RegionId = 0;
    }

    public static void Lose(Artifact artifact, long regionId, long eventId, long fromId)
    {
        artifact.Provenance.Add(new ProvenanceEntry(eventId, fromId, 0, "lost"));
        artifact.Place = ArtifactPlace.Lost;
        artifact.HolderId = 0;
        artifact.SettlementId = 0;
        artifact.RegionId = regionId;
    }

    /// <summary>Applies a non-lethal fate: wounds may scar; maiming leaves a lasting wound.</summary>
    public static string? Injure(Character c, Fate fate, Core.Rng rng)
    {
        if (fate is not (Fate.Wounded or Fate.Maimed)) return null;
        var site = WoundSites[rng.NextInt(0, WoundSites.Length)];
        if (fate == Fate.Maimed)
        {
            c.Wounds.Add(site);
            if (!c.Has(Traits.Maimed)) c.Traits.Add(Traits.Maimed);
        }
        else if (rng.Chance(0.3) && !c.Has(Traits.Scarred)) c.Traits.Add(Traits.Scarred);
        c.Traits.Sort(StringComparer.Ordinal);
        return site;
    }

    private static Character? Living(World world, long id) =>
        world.Characters.TryGetValue(id, out var c) && c.IsAlive ? c : null;
}

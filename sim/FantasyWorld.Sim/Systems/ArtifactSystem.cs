using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Systems;

/// <summary>
/// Artifacts over the years (spec §4): hidden curses come to light and mark their holders; fame grows
/// in renowned hands and fades while lost; a lord claims artifacts lying in their seat's treasury.
/// </summary>
public sealed class ArtifactSystem : ISimSystem
{
    public const double CurseRevealPerYear = 0.15;

    public string Name => "artifacts";

    public void OnMonthlyTick(SimContext ctx)
    {
    }

    public void OnYearlyTick(SimContext ctx)
    {
        var world = ctx.World;
        foreach (var artifact in world.Artifacts.Values)
        {
            switch (artifact.Place)
            {
                case ArtifactPlace.Held:
                    var holder = world.Characters[artifact.HolderId];
                    if (holder.Renown >= 30) artifact.Fame = Math.Min(100, artifact.Fame + 1);
                    if (artifact.Curse is not null && !artifact.CurseRevealed && ctx.Rng.Chance(CurseRevealPerYear))
                        RevealCurse(ctx, artifact, holder);
                    break;
                case ArtifactPlace.Lost:
                    artifact.Fame = Math.Max(5, artifact.Fame - 1);
                    break;
                case ArtifactPlace.Treasury:
                    var seat = world.Settlements[artifact.SettlementId];
                    if (seat.LordId != 0) Claim(ctx, artifact, world.Characters[seat.LordId], seat);
                    break;
            }
        }
    }

    private static void RevealCurse(SimContext ctx, Artifact artifact, Character holder)
    {
        artifact.CurseRevealed = true;
        var acquired = artifact.Provenance.Count > 0 ? artifact.Provenance[^1].EventId : artifact.OriginEventId;
        var ev = ctx.Emit(new EventDraft("ArtifactCurseRevealed",
            Actors: new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["holder"] = holder.Ref },
            Place: ctx.World.Settlements[holder.SettlementId].Ref,
            Payload: new Dictionary<string, string> { ["curse"] = artifact.Curse! },
            Causes: acquired != 0 ? new[] { new Cause(acquired, CauseKind.Enabler, 0.5) } : null,
            Importance: (int)Math.Clamp(20 + artifact.Fame / 2, 0, 100), Tags: new[] { "Artifacts" }));
        switch (artifact.Curse)
        {
            case "paranoia": Become(holder, Traits.Paranoid); break;
            case "bloodlust": Become(holder, Traits.Wrathful); break;
            case "wasting": holder.Wounds.Add("wasting sickness"); break;
            case "misfortune": holder.Renown = Math.Max(0, holder.Renown - 10); break;
        }
        Fates.Remember(holder, ev.Id, 0, -40, 40);
    }

    private static void Claim(SimContext ctx, Artifact artifact, Character lord, Settlement seat)
    {
        var ev = ctx.Emit(new EventDraft("ArtifactClaimed",
            Actors: new Dictionary<string, EntityRef> { ["artifact"] = artifact.Ref, ["claimant"] = lord.Ref },
            Place: seat.Ref,
            Importance: (int)Math.Clamp(10 + artifact.Fame / 3, 0, 100), Tags: new[] { "Artifacts" }));
        Fates.MoveToHolder(artifact, lord, ev.Id, "claimed");
    }

    /// <summary>Gains a trait, dropping its opposite.</summary>
    public static void Become(Character c, string trait)
    {
        if (Traits.OppositeOf(trait) is { } opposite) c.Traits.Remove(opposite);
        if (!c.Has(trait)) c.Traits.Add(trait);
        c.Traits.Sort(StringComparer.Ordinal);
    }
}

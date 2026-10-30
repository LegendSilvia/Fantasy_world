using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Views;

/// <summary>A world event in a saga's course: when, what (told with its links), and the event.</summary>
public sealed record WorldEventRow(int Year, LinkedText Text, long EventId);

/// <summary>
/// One saga of the shadow: the demon lord, the realm it raised, the hero called against it, how it stands or how it ended,
/// and its world events in order.
/// </summary>
public sealed record SagaView(Mention? DemonLord, Mention? Dominion, Mention? Hero, int RoseYear, int? EndedYear, bool Active, string State,
    IReadOnlyList<WorldEventRow> Events);

/// <summary>
/// The world events (the owner, 2026-10-30: the demon lord and the heroes are world events, with a tab of their own on the
/// website): every saga of the shadow, the newest first, as it stood on the day asked.
/// </summary>
public sealed record WorldEventsScreen(string World, IReadOnlyList<SagaView> Sagas)
{
    public static WorldEventsScreen Build(World world, EventLog log, long? until = null)
    {
        var narrator = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        bool By(long day) => until is not { } u || day <= u;
        Mention? Who(long id) => world.Characters.TryGetValue(id, out var c) ? new Mention(c.Ref, c.Name) : null;
        var sagas = new List<SagaView>();
        foreach (var saga in world.Sagas.Values.OrderByDescending(s => s.Id))
        {
            if (!log.TryGet(saga.RoseEventId, out var rose) || !By(rose.Date.Day)) continue;
            var ended = saga.EndedEventId != 0 && log.TryGet(saga.EndedEventId, out var end) && By(end.Date.Day) ? end : null;
            var last = ended?.Date.Day ?? until ?? long.MaxValue;
            var rows = log.All.Where(e => e.Id >= rose.Id && e.Date.Day <= last && EpisodeSection.WorldEvents.Contains(e.Type))
                .Select(e => new WorldEventRow(e.Date.Year, LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf), e.Id)).ToList();
            var heroShown = saga.HeroId != 0 && rows.Any(r => log.TryGet(r.EventId, out var e) && e.Actors.Values.Any(a => a.Kind == Character.Kind && a.Id == saga.HeroId));
            var demon = world.Characters.GetValueOrDefault(saga.DemonLordId);
            var dominion = world.Realms.GetValueOrDefault(saga.RealmId);
            var state = ended is null
                ? $"{demon?.Name ?? "The demon lord"} is abroad in the world{(dominion is null ? "" : $", ruling {dominion.Name}")}. "
                  + (heroShown ? "A hero has been called to face them." : "No hero has yet risen to face them.")
                : saga.Outcome switch
                {
                    Saga.Slain => "The demon lord was slain.",
                    Saga.Sealed => "The demon lord was sealed away. It is not dead, and may be told of again.",
                    Saga.Driven => "The demon lord was driven into the dark. It is not dead, and may return.",
                    Saga.Rules => "The demon lord won: a dark age began.",
                    _ => "The saga ended.",
                };
            sagas.Add(new SagaView(Who(saga.DemonLordId), dominion is null ? null : new Mention(dominion.Ref, dominion.Name),
                heroShown ? Who(saga.HeroId) : null, rose.Date.Year, ended?.Date.Year, ended is null, state, rows));
        }
        return new WorldEventsScreen(world.Name, sagas);
    }
}

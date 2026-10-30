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
/// A world event besides the demon lord (the owner, 2026-10-30): the great labyrinth (with its outbreaks) or an elder dragon,
/// what it is, how it stands, its level, and its course: the companies that went against it and what came of them.
/// </summary>
public sealed record GreatEventView(string Kind, Mention Subject, int Level, int RoseYear, int? EndedYear, bool Active, string State,
    IReadOnlyList<WorldEventRow> Events);

/// <summary>
/// The world events (the owner, 2026-10-30: the demon lord and the heroes are world events, with a tab of their own on the
/// website): every saga of the shadow, the newest first, as it stood on the day asked.
/// </summary>
public sealed record WorldEventsScreen(string World, IReadOnlyList<SagaView> Sagas, IReadOnlyList<GreatEventView>? Great = null)
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
        var great = new List<GreatEventView>();
        WorldEventRow Row(WorldEvent e) => new(e.Date.Year, LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf), e.Id);
        foreach (var maze in world.Structures.Values.Where(s => s.Great).OrderByDescending(s => s.Id))
        {
            if (!log.TryGet(maze.StartedEventId, out var rose) || !By(rose.Date.Day)) continue;
            var cleared = maze.ClearedEventId != 0 && log.TryGet(maze.ClearedEventId, out var end) && By(end.Date.Day) ? end : null;
            var rows = log.Involving(maze.Ref).Where(e => By(e.Date.Day) && e.Type is "GreatLabyrinthRose" or "GreatOutbreak" or "GreatOutbreakEnded"
                or "ExpeditionBegun" or "ExpeditionEnded" or "LabyrinthCleared").Select(Row).ToList();
            var outbreak = log.Involving(maze.Ref).LastOrDefault(e => By(e.Date.Day) && e.Type is "GreatOutbreak" or "GreatOutbreakEnded")?.Type == "GreatOutbreak";
            var state = cleared is not null ? "It was cleared, and its heart is quiet."
                : $"Unconquered: companies have gone down {maze.Depth} of its {maze.Levels} levels; it asks level {Levels.Required(maze)} of its delvers."
                  + (outbreak ? " Its beasts are loose in the lands around it." : "");
            great.Add(new GreatEventView("great labyrinth", new Mention(maze.Ref, maze.Name), Levels.Required(maze), rose.Date.Year, cleared?.Date.Year,
                cleared is null, state, rows));
        }
        foreach (var dragon in world.Monsters.Values.Where(m => m.Elder).OrderByDescending(m => m.Id))
        {
            if (!log.TryGet(dragon.RoseEventId, out var woke) || !By(woke.Date.Day)) continue;
            var slain = dragon.SlainEventId != 0 && log.TryGet(dragon.SlainEventId, out var end) && By(end.Date.Day) ? end : null;
            var rows = log.Involving(dragon.Ref).Where(e => By(e.Date.Day) && e.Type is "ElderDragonWoke" or "ExpeditionBegun" or "DragonWounded"
                or "DragonHuntFailed" or "ElderDragonSlain" or "MonsterRaid").Select(Row).ToList();
            var state = slain is not null ? "It was slain by a company of the world's greatest."
                : dragon.Vigour >= 100 ? "It preys on the realms, unwounded. Only a company can face it."
                : $"It preys on the realms, its strength worn to {(int)dragon.Vigour} in a hundred by the companies that faced it.";
            great.Add(new GreatEventView("elder dragon", new Mention(dragon.Ref, dragon.Name), Levels.Of(dragon), woke.Date.Year, slain?.Date.Year,
                slain is null, state, rows));
        }
        return new WorldEventsScreen(world.Name, sagas, great);
    }
}

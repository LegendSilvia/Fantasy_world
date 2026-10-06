using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

public static class EventText
{
    /// <summary>One line per event. With nameOf, entities read "Brenmoor (region#3)".</summary>
    public static string Describe(WorldEvent ev, Func<EntityRef, string?>? nameOf = null)
    {
        string Name(EntityRef entity) => nameOf?.Invoke(entity) is { } name ? $"{name} ({entity})" : entity.ToString();

        var text = $"#{ev.Id} {ev.Date} {ev.Type}";
        if (ev.Actors.Count > 0)
            text += " " + string.Join(", ", ev.Actors.Select(a => $"{a.Key}={Name(a.Value)}"));
        if (ev.Place is { } place)
            text += $" at {Name(place)}";
        if (ev.Payload.Count > 0)
            text += " " + string.Join(", ", ev.Payload.Select(p => $"{p.Key}={p.Value}"));
        return text;
    }
}

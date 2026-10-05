using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Query;

public static class EventText
{
    public static string Describe(WorldEvent ev)
    {
        var text = $"#{ev.Id} {ev.Date} {ev.Type}";
        if (ev.Actors.Count > 0)
            text += " " + string.Join(", ", ev.Actors.Select(a => $"{a.Key}={a.Value}"));
        if (ev.Place is { } place)
            text += $" at {place}";
        if (ev.Payload.Count > 0)
            text += " " + string.Join(", ", ev.Payload.Select(p => $"{p.Key}={p.Value}"));
        return text;
    }
}

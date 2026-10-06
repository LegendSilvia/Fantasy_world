using FantasyWorld.Sim.Events;

namespace FantasyWorld.Client;

public enum ScreenKind { Digest, Map, Character, Why, Chronicle, Realm, Artifact, Family }

/// <summary>Which screen is showing and for what: an entity or event id, the map's year, and the place a map flies to.</summary>
public sealed record Screen(ScreenKind Kind, long Id = 0, int? Year = null, EntityRef? Focus = null);

/// <summary>
/// Navigation (sub-project 4 §4): every linked name opens its page (characters → character page, places → the map
/// focused there, realms, wars and artifacts → their pages), "why" opens a chain, and Back returns.
/// </summary>
public sealed class ScreenRouter
{
    private readonly Stack<Screen> _back = new();

    public Screen Current { get; private set; } = new(ScreenKind.Digest);

    public bool CanGoBack => _back.Count > 0;

    public event Action<Screen>? Changed;

    public void Go(Screen screen)
    {
        if (screen == Current) return;
        _back.Push(Current);
        Current = screen;
        Changed?.Invoke(screen);
    }

    public bool Back()
    {
        if (_back.Count == 0) return false;
        Current = _back.Pop();
        Changed?.Invoke(Current);
        return true;
    }

    public void Home()
    {
        _back.Clear();
        Current = new Screen(ScreenKind.Digest);
        Changed?.Invoke(Current);
    }

    /// <summary>The screen a linked entity opens, or null when it has none (e.g. a house).</summary>
    public static Screen? For(EntityRef entity) => entity.Kind switch
    {
        "char" => new Screen(ScreenKind.Character, entity.Id),
        "settlement" or "region" or "structure" => new Screen(ScreenKind.Map, Focus: entity),
        "realm" => new Screen(ScreenKind.Realm, entity.Id),
        "artifact" => new Screen(ScreenKind.Artifact, entity.Id),
        "war" => new Screen(ScreenKind.Chronicle),
        _ => null,
    };

    public void Open(EntityRef entity)
    {
        if (For(entity) is { } screen) Go(screen);
    }

    public void Explain(long eventId) => Go(new Screen(ScreenKind.Why, eventId));
}

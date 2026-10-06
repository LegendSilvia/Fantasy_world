using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

public enum RouteState { Open, Severed, Closed }

/// <summary>
/// A trade road between two markets (sub-project 3 §8): the regions it crosses, end to end, how much wealth it carried
/// last year, and whether it is open, severed (by war, occupation or danger, and may reopen) or closed for good (a
/// market was abandoned).
/// </summary>
public sealed class TradeRoute
{
    public const string Kind = "route";

    public long Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>The two market settlements, the lower id first.</summary>
    public long FromId { get; init; }
    public long ToId { get; init; }

    /// <summary>The regions the road crosses, from the first market's region to the second's.</summary>
    public List<long> Regions { get; init; } = new();

    public RouteState State { get; set; }

    /// <summary>Wealth the road carried each way last year.</summary>
    public double Volume { get; set; }

    public long OpenedEventId { get; init; }
    public long SeveredEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

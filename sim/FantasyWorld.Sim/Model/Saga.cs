using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// The saga of a demon lord (sub-project 13 N4, the owner's choice: a world-ending saga): who rose and where, the
/// dominion it rules, the prophecy spoken of it, the hero foretold, and how it ended (slain, sealed, or ruling a dark age).
/// </summary>
public sealed class Saga
{
    public const string Kind = "saga";
    public const string Slain = "slain", Sealed = "sealed", Rules = "rules", Driven = "driven";

    public long Id { get; init; }
    public long DemonLordId { get; init; }
    public long RealmId { get; init; }
    public long RoseEventId { get; init; }
    public long ProphecyEventId { get; set; }
    public long HeroId { get; set; }
    public long EndedEventId { get; set; }
    public string Outcome { get; set; } = "";

    [JsonIgnore] public bool Active => EndedEventId == 0;
    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

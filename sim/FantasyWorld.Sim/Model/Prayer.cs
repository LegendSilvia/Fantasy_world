using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>
/// A prayer to the gods (sub-project 13 N5, the owner: the player is a god). Someone of the world prays in need or hope;
/// the players answer it (grant or deny) until it lapses, and the rules decide it then.
/// </summary>
public sealed class Prayer
{
    public const string Kind = "prayer";
    public const string Open = "open", Granted = "granted", Denied = "denied";

    /// <summary>What is prayed for.</summary>
    public const string Victory = "victory", Healing = "healing", Rain = "rain", Heir = "an heir", Deliverance = "deliverance", Vengeance = "vengeance";

    public long Id { get; init; }
    public long EventId { get; init; }
    public long SupplicantId { get; init; }
    public long FaithId { get; init; }
    public string Plea { get; init; } = "";

    /// <summary>What the plea is about: the war, the region, the beast, the foe (none for healing or an heir).</summary>
    public EntityRef? About { get; init; }

    public long Day { get; init; }
    public string State { get; set; } = Open;
    public long AnsweredEventId { get; set; }

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
    [JsonIgnore] public bool IsOpen => State == Open;
}

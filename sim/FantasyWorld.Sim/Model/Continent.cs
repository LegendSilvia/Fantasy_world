using System.Text.Json.Serialization;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Model;

/// <summary>A landmass parted from the others by open sea (sub-project 13), with the islands nearest it. A world of one
/// landmass has one continent.</summary>
public sealed class Continent
{
    public const string Kind = "continent";

    public long Id { get; init; }
    public string Name { get; init; } = "";

    [JsonIgnore] public EntityRef Ref => new(Kind, Id);
}

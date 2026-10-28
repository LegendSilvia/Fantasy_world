using System.Globalization;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Query;

namespace FantasyWorld.Sim.Llm;

/// <summary>The world an LLM job reads from: set by the host after loading, shared by deciders, arbiters and narrators.</summary>
public sealed class StoryContext(World world, EventLog log, LlmGateway gateway)
{
    public World World { get; } = world;
    public EventLog Log { get; } = log;
    public LlmGateway Gateway { get; } = gateway;
    public string Bible { get; } = WorldBible.Build(world);
}

/// <summary>Builds the per-call parts of prompts: who a character is, what they remember, whom they love or hate.</summary>
public static class Briefs
{
    public static string Character(StoryContext story, Character c)
    {
        var world = story.World;
        var today = world.Date;
        var lines = new List<string>
        {
            $"{PromptText.Data(Names.Full(world, c))}, {c.Sex.ToString().ToLowerInvariant()}, aged {c.Age(today)}, {Role(c)}"
                + (world.Settlements.TryGetValue(c.SettlementId, out var home) ? $" of {PromptText.Data(home.Name)}" : ""),
            $"Culture: {Named(world.Cultures.GetValueOrDefault(c.CultureId)?.Name)}"
                + (Peoples.Of(world, c) is { } race ? $" ({PromptText.Data(race.Name)})" : "")
                + $"; faith: {Named(world.Faiths.GetValueOrDefault(c.FaithId)?.Name)}",
            $"Traits: {(c.Traits.Count > 0 ? string.Join(", ", c.Traits) : "none of note")}",
            $"Ambition: {c.Ambition}; renown {c.Renown.ToString("0", CultureInfo.InvariantCulture)}/100",
            "Strengths: " + string.Join(", ", Attributes.Names.Select(n => $"{n} {c.Attributes.Get(n)}")),
        };
        if (c.Wounds.Count > 0) lines.Add($"Wounds: {string.Join(", ", c.Wounds.Select(w => PromptText.Data(w)))}");
        if (world.Characters.TryGetValue(c.SpouseId, out var spouse)) lines.Add($"Spouse: {PromptText.Data(Names.Full(world, spouse))}");
        var memories = c.Memories.OrderByDescending(m => Math.Abs(m.Feeling) * m.Weight).ThenBy(m => m.EventId).Take(5).ToList();
        if (memories.Count > 0)
        {
            lines.Add("Remembers:");
            var nameOf = Names.Of(world);
            foreach (var m in memories)
            {
                var what = story.Log.TryGet(m.EventId, out var ev) ? $"{Spaced(ev.Type)} ({When(ev)})" : "an old matter";
                var who = world.Characters.TryGetValue(m.About, out var other) ? PromptText.Data(nameOf(other.Ref)) : "someone";
                lines.Add($"- {(m.Feeling >= 0 ? "warmly" : "bitterly")}: {who}, {what}");
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>A host as a battle's arbiter sees it: its realm, troops, commander, morale, supply and strength.</summary>
    public static string Army(StoryContext story, Army army, double strength)
    {
        var world = story.World;
        var commander = world.Characters.TryGetValue(army.CommanderId, out var c)
            ? $"{PromptText.Data(Names.Full(world, c))} (martial {c.Attributes.Martial})" : "none (the captains lead)";
        return $"{PromptText.Data(army.Name)}, of {PromptText.Data(world.Realms[army.RealmId].Name)}\n"
               + $"Troops: {army.Levies} levies, {army.MenAtArms} men-at-arms, {army.Knights.Count} knights, {army.Mages} mages\n"
               + $"Commander: {commander}\n"
               + $"Morale {army.Morale.ToString("0", CultureInfo.InvariantCulture)}/100; supply {army.Supply.ToString("0", CultureInfo.InvariantCulture)}/100; "
               + $"strength {strength.ToString("0", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"DuelFought" → "duel fought".</summary>
    public static string Spaced(string type) =>
        string.Concat(type.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    private static string When(WorldEvent ev) => ev.Date.Year < 1 ? "before history" : $"year {ev.Date.Year}";

    private static string Role(Character c) => c.Role == Model.Role.None ? "commoner of note" : c.Role.ToString().ToLowerInvariant();

    private static string Named(string? name) => name is null ? "unknown" : PromptText.Data(name);
}

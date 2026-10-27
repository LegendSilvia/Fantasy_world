using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Views;

/// <summary>The system an event belongs to, which a client shows as a colour (the design's coloured dots).</summary>
public enum Category { Politics, War, Economy, Characters, FaithAndMagic, Structures, Nature, Chronicle }

public static class Categories
{
    /// <summary>The most specific category among the event's tags; untagged events count as character stories.</summary>
    public static Category Of(WorldEvent ev)
    {
        bool Has(string tag) => ev.Tags.Contains(tag, StringComparer.Ordinal);
        if (Has("War")) return Category.War;
        if (Has("Structures")) return Category.Structures;
        if (Has("Magic") || Has("Faith")) return Category.FaithAndMagic;
        if (Has("Politics") || Has("Diplomacy") || Has("Era")) return Category.Politics;
        if (Has("Economy") || Has("Settlements")) return Category.Economy;
        if (Has("Regions")) return Category.Nature;
        if (Has("Chronicle")) return Category.Chronicle;
        return Category.Characters;
    }

    public static string Label(Category category) => category switch
    {
        Category.FaithAndMagic => "Faith & magic",
        _ => category.ToString(),
    };

    private static readonly IReadOnlyDictionary<string, string> Kickers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PeaceSigned"] = "War ends",
        ["WarDeclared"] = "War",
        ["BattleFought"] = "Battle",
        ["SiegeEnded"] = "Siege ends",
        ["CharacterDied"] = "Death",
        ["CharacterBorn"] = "Birth",
        ["MarriageWed"] = "Marriage",
        ["SuccessionOccurred"] = "Succession",
        ["DuelFought"] = "Duel",
        ["DuelDeclared"] = "A challenge",
        ["DuelCalledOff"] = "Duel called off",
        ["DuelBegun"] = "A duel begins",
        ["DuelTurned"] = "The duel goes on",
        ["BattleJoined"] = "Battle joined",
        ["BattleTurned"] = "The battle goes on",
        ["BattleBrokenOff"] = "Battle broken off",
        ["LabyrinthOpened"] = "Labyrinth",
        ["LandmarkCompleted"] = "Landmark",
        ["EraBegan"] = "A new era",
        ["RealmFell"] = "A realm falls",
        ["IndependenceDeclared"] = "Independence",
        ["ArtifactForged"] = "Artifact",
        ["ArtifactFound"] = "Artifact",
        ["ArtifactStolen"] = "Theft",
        ["RevoltBegan"] = "Revolt",
        ["PlagueOutbreak"] = "Plague",
        ["MonsterRose"] = "A beast rises",
        ["MonsterRaid"] = "Raid",
        ["MonsterLegendary"] = "A legend",
        ["MonsterSlain"] = "A beast slain",
        ["RouteOpened"] = "Trade",
        ["RouteSevered"] = "Trade cut",
        ["RegionConverted"] = "Conversion",
        ["LabyrinthCleared"] = "A labyrinth cleared",
        ["WorksCollapsed"] = "A collapse",
        ["FortificationCompleted"] = "A fortress",
        ["RestorationBegun"] = "Restoration",
        ["WildMagic"] = "Wild magic",
        ["MagicLawChanged"] = "Magic law",
        ["SchismBegan"] = "Schism",
        ["SaintDeclared"] = "A saint",
        ["RealmConverted"] = "A realm converts",
        ["HolySiteLost"] = "Holy land lost",
        ["HolySiteRegained"] = "Holy land regained",
    };

    /// <summary>A short label above a headline ("War ends"), else the event type in words.</summary>
    public static string Kicker(WorldEvent ev)
    {
        if (Kickers.TryGetValue(ev.Type, out var kicker)) return kicker;
        var spaced = Briefs.Spaced(ev.Type);
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}

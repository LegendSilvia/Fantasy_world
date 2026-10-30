using System.Globalization;

namespace FantasyWorld.Client;

/// <summary>
/// The map's badges (the owner, 2026-10-28), drawn as SVG so the game, the film and the website show the same icons:
/// a beast is a boss marker (a horned skull on a shield, its rim gold for a legend, its eyes dark while it sleeps), and
/// a fight is crossed swords on a round plate (gold for one fought this past year, red while it goes on).
/// </summary>
public static class Badges
{
    public const int BossWidth = 64, BossHeight = 76, SwordsSize = 64;

    private const string Bone = "#efe6d2", Night = "#2b2230", Blood = "#5c1717", Gold = "#e3b94f", Ember = "#ff8a3d", Ash = "#77736b", War = "#c0392b";

    /// <summary>A beast's marker: legends wear gold, sleepers are greyed with closed eyes.</summary>
    public static string Boss(bool legendary, bool slumbering)
    {
        var (fill, rim, eye) = slumbering ? (Ash, "#b9b1a2", "#3d3a36") : (Blood, legendary ? Gold : Bone, Ember);
        var glow = legendary && !slumbering ? $"""<path d="M32 3 L36 9 L32 7 L28 9 Z" fill="{Gold}"/>""" : "";
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{BossWidth}" height="{BossHeight}" viewBox="0 0 64 76">
            <path d="M32 74 L24 63 H13 a8 8 0 0 1 -8 -8 V14 a8 8 0 0 1 8 -8 H51 a8 8 0 0 1 8 8 V55 a8 8 0 0 1 -8 8 H40 Z" fill="{fill}" stroke="{rim}" stroke-width="3.5" stroke-linejoin="round"/>
            {glow}
            <path d="M19 24 C10 18 10 10 14 7 C15 13 19 17 24 19 Z" fill="{Bone}"/>
            <path d="M45 24 C54 18 54 10 50 7 C49 13 45 17 40 19 Z" fill="{Bone}"/>
            <path d="M32 15 C21 15 16 23 16 31 C16 37 19 41 23 43 V49 H41 V43 C45 41 48 37 48 31 C48 23 43 15 32 15 Z" fill="{Bone}"/>
            <ellipse cx="25.5" cy="31.5" rx="4.6" ry="5.2" fill="{eye}"/>
            <ellipse cx="38.5" cy="31.5" rx="4.6" ry="5.2" fill="{eye}"/>
            <path d="M32 37 L29.4 42 H34.6 Z" fill="{fill}"/>
            <path d="M27 45 V49 M31 45 V49 M35 45 V49 M39 45 V49" stroke="{fill}" stroke-width="1.7"/>
            </svg>
            """;
    }

    /// <summary>
    /// A battle's marker (the owner, 2026-10-29: nation against nation unlike a duel): two war banners crossed on their poles,
    /// in red and blue, over the same plate, gold-rimmed once fought, red-rimmed while it goes on.
    /// </summary>
    public static string Banners(bool underWay)
    {
        var rim = underWay ? War : Gold;
        string Banner(int angle, string cloth, bool flip) => $"""
            <g transform="rotate({angle.ToString(CultureInfo.InvariantCulture)} 32 32)">
            <rect x="30.6" y="8" width="2.8" height="48" rx="1" fill="#8a6a42"/>
            <circle cx="32" cy="7.5" r="2.4" fill="{Gold}"/>
            <path d="{(flip ? "M30.6 11 H17 L21 17 L17 23 H30.6 Z" : "M33.4 11 H47 L43 17 L47 23 H33.4 Z")}" fill="{cloth}" stroke="#f2ead8" stroke-width="0.8"/>
            </g>
            """;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{SwordsSize}" height="{SwordsSize}" viewBox="0 0 64 64">
            <circle cx="32" cy="32" r="28.5" fill="{Night}" stroke="{rim}" stroke-width="3.5"/>
            {Banner(-28, "#b23a3a", true)}{Banner(28, "#3a66b2", false)}
            </svg>
            """;
    }

    /// <summary>
    /// A labyrinth's marker (the owner, 2026-10-30: labyrinths on the map): a maze of rings with a dark heart and one way in,
    /// on a stone plate; ember-rimmed while unconquered, greyed and gold-rimmed once cleared.
    /// </summary>
    public static string Labyrinth(bool cleared)
    {
        var (rim, plate, wall, heart) = cleared ? (Gold, "#5b5750", "#cfc6b4", "#8a8478") : (Ember, "#3a3128", "#e9dcc0", "#120d0a");
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{SwordsSize}" height="{SwordsSize}" viewBox="0 0 64 64">
            <circle cx="32" cy="32" r="28.5" fill="{plate}" stroke="{rim}" stroke-width="3.5"/>
            <g fill="none" stroke="{wall}" stroke-width="3" stroke-linecap="round">
            <path d="M32 11 A21 21 0 1 1 25.5 12"/>
            <path d="M38 18.5 A15 15 0 1 1 32 17"/>
            <path d="M32 23 A9 9 0 1 1 26 25.3"/>
            </g>
            <circle cx="32" cy="32" r="4.2" fill="{heart}"/>
            </svg>
            """;
    }

    /// <summary>A duel's marker: crossed swords, gold-rimmed once fought, red-rimmed while it goes on.</summary>
    public static string Swords(bool underWay)
    {
        var rim = underWay ? War : Gold;
        string Sword(int angle) => $"""
            <g transform="rotate({angle.ToString(CultureInfo.InvariantCulture)} 32 32)">
            <path d="M30 11 L32 5 L34 11 V43 H30 Z" fill="#e6e3dc" stroke="#8d8a83" stroke-width="0.8"/>
            <rect x="23.5" y="42" width="17" height="3.4" rx="1.4" fill="{Gold}"/>
            <rect x="30.4" y="45.2" width="3.2" height="8" fill="#6b4a2b"/>
            <circle cx="32" cy="55.5" r="2.6" fill="{Gold}"/>
            </g>
            """;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{SwordsSize}" height="{SwordsSize}" viewBox="0 0 64 64">
            <circle cx="32" cy="32" r="28.5" fill="{Night}" stroke="{rim}" stroke-width="3.5"/>
            {Sword(45)}{Sword(-45)}
            </svg>
            """;
    }
}

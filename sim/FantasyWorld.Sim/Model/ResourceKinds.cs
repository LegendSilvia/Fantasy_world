namespace FantasyWorld.Sim.Model;

/// <summary>Resource names used as keys in Region.Resources.</summary>
public static class ResourceKinds
{
    public const string Grain = "grain";
    public const string Timber = "timber";
    public const string Ore = "ore";
    public const string Stone = "stone";
    public const string Horses = "horses";
    public const string Fish = "fish";
    public const string Mithril = "mithril";
    public const string Dragonbone = "dragonbone";
    public const string ArcaneCrystal = "arcane crystal";

    public static readonly IReadOnlyList<string> Rare = new[] { Mithril, Dragonbone, ArcaneCrystal };

    public static bool IsRare(string kind) => Rare.Contains(kind);
}

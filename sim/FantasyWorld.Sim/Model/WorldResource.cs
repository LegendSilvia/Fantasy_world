namespace FantasyWorld.Sim.Model;

/// <summary>
/// One of the world's own resources (the owner, 2026-10-29), one to three a world, named by Claude when the world is made:
/// what it is, where it lies, and the part it plays in the story. A hidden one is found only when a secret of the world is
/// uncovered, by the one who uncovered it. Its role is what makes it matter:
/// <list type="bullet">
/// <item>seal: the one thing that can seal a demon lord away; until it is found, no sage can;</item>
/// <item>forge: arms made of it, so the realm that holds it fights harder;</item>
/// <item>life: it heals, so in the realm that holds it sickness and age come gentler;</item>
/// <item>wealth: it fills a treasury, and neighbours covet the land it lies in.</item>
/// </list>
/// Once found, the realm holding its land gathers it into its stores like any other.
/// </summary>
public sealed record WorldResource(string Kind, string Name, string What, string Role, long RegionId, bool Hidden = true, long FoundEventId = 0,
    long FinderId = 0)
{
    public const string Seal = "seal", Forge = "forge", Life = "life", Wealth = "wealth";

    public static readonly IReadOnlyList<string> Roles = [Seal, Forge, Life, Wealth];

    /// <summary>What it does in the world, in words.</summary>
    public string Power => Role switch
    {
        Seal => "the one thing that can seal a demon lord away",
        Forge => "arms forged of it make the realm that holds it fight harder",
        Life => "it heals: where it is held, sickness and age come gentler",
        _ => "it fills a treasury, and neighbours covet the land it lies in",
    };
}

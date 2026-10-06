using System.Numerics;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Client;

/// <summary>The design's tokens (sub-project 4 §5) as linear-ish RGB in 0–1, plus colours for categories and realms.</summary>
public static class Palette
{
    public static readonly Vector3 Ground = BlockMesh.Hex(0xecebe7);
    public static readonly Vector3 Card = BlockMesh.Hex(0xf7f6f3);
    public static readonly Vector3 Border = BlockMesh.Hex(0xdcdad4);
    public static readonly Vector3 Rule = BlockMesh.Hex(0xe3e1db);
    public static readonly Vector3 Ink = BlockMesh.Hex(0x1a1a18);
    public static readonly Vector3 Body = BlockMesh.Hex(0x4a4842);
    public static readonly Vector3 Muted = BlockMesh.Hex(0x66645d);
    public static readonly Vector3 Conflict = BlockMesh.Hex(0xc4621f);
    public static readonly Vector3 Realm = BlockMesh.Hex(0x3b6fb6);

    public const string DisplayFont = "Instrument Serif";
    public const string UiFont = "Geist";

    public static Vector3 For(Category category) => category switch
    {
        Category.War => Conflict,
        Category.Politics => Realm,
        Category.Economy => BlockMesh.Hex(0xb58a3c),
        Category.FaithAndMagic => BlockMesh.Hex(0x7b5ea7),
        Category.Structures => Ink,
        Category.Nature => BlockMesh.Hex(0x5e8a4a),
        Category.Chronicle => Muted,
        _ => Body,
    };

    private static readonly Dictionary<string, Vector3> Heraldry = new(StringComparer.Ordinal)
    {
        ["crimson"] = BlockMesh.Hex(0xa8323a), ["azure"] = Realm, ["gold"] = BlockMesh.Hex(0xc9a227), ["silver"] = BlockMesh.Hex(0x9aa0a6),
        ["sable"] = Ink, ["vert"] = BlockMesh.Hex(0x3f7d4e), ["purpure"] = BlockMesh.Hex(0x6d4c8f), ["orange"] = Conflict,
        ["white"] = Card, ["russet"] = BlockMesh.Hex(0x8b4a2b), ["grey"] = Muted,
    };

    /// <summary>A realm's heraldic colour name (e.g. "azure") as a colour; unknown names are muted.</summary>
    public static Vector3 Heraldic(string name) => Heraldry.TryGetValue(name, out var colour) ? colour : Muted;
}

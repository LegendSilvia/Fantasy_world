using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// A map badge (the owner, 2026-10-28): one of <see cref="Client.Badges"/>' icons as a sprite that always faces the camera
/// and is never hidden by the land, standing above its place. Textures are drawn once per icon and shared.
/// </summary>
public static class Badge
{
    private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);

    /// <summary>The icon drawn at twice its size, so it stays crisp close up.</summary>
    public static Texture2D Texture(string svg)
    {
        if (Textures.TryGetValue(svg, out var known)) return known;
        var image = new Image();
        image.LoadSvgFromString(svg, 2f);
        return Textures[svg] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>A sprite of the icon, <paramref name="height"/> block units tall, its foot at the node's origin.</summary>
    public static Sprite3D Sprite(string svg, float height, string name)
    {
        var texture = Texture(svg);
        return new Sprite3D
        {
            Name = name,
            Texture = texture,
            PixelSize = height / texture.GetHeight(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            RenderPriority = 10,
            Offset = new Vector2(0, texture.GetHeight() / 2f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
    }
}

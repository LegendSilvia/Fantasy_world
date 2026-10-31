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
    public static Sprite3D Sprite(string svg, float height, string name) => Sprite(Texture(svg), height, name);

    /// <summary>
    /// A painting in its frame, as the website frames a beast (the owner, 2026-10-31): a pale border round a deep red ground,
    /// gold for a legend, grey and drained of colour for a sleeper, the painting whole within it.
    /// </summary>
    public static Texture2D Framed(Image picture, bool legendary, bool asleep)
    {
        const int width = 168, height = 200, border = 8;
        var frame = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        frame.Fill(legendary ? new Color("#e3b94f") : asleep ? new Color("#b9b1a2") : new Color("#efe6d2"));
        frame.FillRect(new Rect2I(border, border, width - 2 * border, height - 2 * border), new Color("#5c1717"));
        var art = (Image)picture.Duplicate();
        art.Convert(Image.Format.Rgba8);
        var room = new Vector2I(width - 2 * border - 4, height - 2 * border - 4);
        var scale = Math.Min((float)room.X / art.GetWidth(), (float)room.Y / art.GetHeight());
        var size = new Vector2I(Math.Max(1, (int)(art.GetWidth() * scale)), Math.Max(1, (int)(art.GetHeight() * scale)));
        art.Resize(size.X, size.Y, Image.Interpolation.Lanczos);
        if (asleep) art.AdjustBcs(0.85f, 1f, 0.2f);
        frame.BlendRect(art, new Rect2I(Vector2I.Zero, size), new Vector2I((width - size.X) / 2, (height - size.Y) / 2));
        frame.GenerateMipmaps();
        return ImageTexture.CreateFromImage(frame);
    }

    /// <summary>A sprite of a texture, <paramref name="height"/> block units tall, its foot at the node's origin.</summary>
    public static Sprite3D Sprite(Texture2D texture, float height, string name)
    {
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

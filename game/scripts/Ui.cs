using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using Godot;

namespace FantasyWorld.Game;

/// <summary>The design's look (sub-project 4 §5) and small builders for the controls every screen uses.</summary>
public static class Ui
{
    private static readonly FontFile Display = GD.Load<FontFile>("res://fonts/InstrumentSerif-Italic.ttf");
    private static readonly FontFile DisplayUpright = GD.Load<FontFile>("res://fonts/InstrumentSerif-Regular.ttf");
    private static readonly FontFile Sans = GD.Load<FontFile>("res://fonts/Geist[wght].ttf");

    public static Color C(Rgb rgb, float alpha = 1) => new(rgb.X, rgb.Y, rgb.Z, alpha);

    public static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFont = Sans, DefaultFontSize = 13 };
        theme.SetColor("font_color", "Label", C(Palette.Ink));
        theme.SetColor("font_color", "Button", C(Palette.Ink));
        theme.SetColor("font_hover_color", "Button", C(Palette.Body));
        theme.SetStylebox("normal", "Button", Pill(filled: false));
        theme.SetStylebox("hover", "Button", Pill(filled: false));
        theme.SetStylebox("pressed", "Button", Pill(filled: true));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        return theme;
    }

    public static StyleBoxFlat Pill(bool filled) => new()
    {
        BgColor = filled ? C(Palette.Ink) : new Color(0, 0, 0, 0),
        BorderColor = C(Palette.Ink),
        BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusBottomLeft = 999, CornerRadiusBottomRight = 999, CornerRadiusTopLeft = 999, CornerRadiusTopRight = 999,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 7, ContentMarginBottom = 7,
    };

    /// <summary>The floating card: warm white, hairline border, soft shadow, 16 px corners.</summary>
    public static PanelContainer Card(string name, float width)
    {
        var card = new PanelContainer { Name = name, CustomMinimumSize = new Godot.Vector2(width, 0) };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = C(Palette.Card), BorderColor = C(Palette.Border),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16, CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16,
            ShadowColor = new Color(0.1f, 0.1f, 0.09f, 0.06f), ShadowSize = 20, ShadowOffset = new Godot.Vector2(0, 12),
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 22, ContentMarginBottom = 22,
        });
        return card;
    }

    /// <summary>The small uppercase label above sections ("OVERNIGHT", "TIES").</summary>
    public static Label Kicker(string text, string name = "Kicker")
    {
        var label = new Label { Name = name, Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", C(Palette.Muted));
        return label;
    }

    /// <summary>Italic serif display text ("Year 312.", names on pages).</summary>
    public static Label Title(string text, int size, string name = "Title", bool italic = true)
    {
        var label = new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontOverride("font", italic ? Display : DisplayUpright);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static Label Body(string text, string name = "Body", Rgb? colour = null, int size = 13)
    {
        var label = new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", C(colour ?? Palette.Body));
        return label;
    }

    public static Button Button(string text, string name, bool primary = false)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Godot.Vector2(0, 34) };
        if (primary)
        {
            button.AddThemeStyleboxOverride("normal", Pill(filled: true));
            button.AddThemeStyleboxOverride("hover", Pill(filled: true));
            button.AddThemeColorOverride("font_color", Colors.White);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
        }
        return button;
    }

    public static VBoxContainer Column(string name, int gap)
    {
        var column = new VBoxContainer { Name = name };
        column.AddThemeConstantOverride("separation", gap);
        return column;
    }

    public static HBoxContainer Row(string name, int gap)
    {
        var row = new HBoxContainer { Name = name };
        row.AddThemeConstantOverride("separation", gap);
        return row;
    }

    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}

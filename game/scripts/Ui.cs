using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;
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
        theme.SetColor("font_color", "CheckBox", C(Palette.Ink));
        theme.SetColor("font_hover_color", "CheckBox", C(Palette.Ink));
        theme.SetColor("font_pressed_color", "CheckBox", C(Palette.Ink));
        theme.SetColor("font_disabled_color", "CheckBox", C(Palette.Muted));
        theme.SetIcon("checked", "CheckBox", Box(14, filled: true));
        theme.SetIcon("unchecked", "CheckBox", Box(14, filled: false));
        theme.SetIcon("checked_disabled", "CheckBox", Box(14, filled: true, Palette.Muted));
        theme.SetIcon("unchecked_disabled", "CheckBox", Box(14, filled: false, Palette.Border));
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("slider", "HSlider", new StyleBoxFlat { BgColor = C(Palette.Border), ContentMarginTop = 1.5f, ContentMarginBottom = 1.5f });
        theme.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat { BgColor = C(Palette.Ink), ContentMarginTop = 1.5f, ContentMarginBottom = 1.5f });
        theme.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxFlat { BgColor = C(Palette.Ink), ContentMarginTop = 1.5f, ContentMarginBottom = 1.5f });
        theme.SetIcon("grabber", "HSlider", Circle(14, Palette.Ink));
        theme.SetIcon("grabber_highlight", "HSlider", Circle(14, Palette.Body));
        return theme;
    }

    /// <summary>A checkbox square: ink-filled with a light inset when checked, a hairline outline when not.</summary>
    public static ImageTexture Box(int size, bool filled, Rgb? colour = null)
    {
        var ink = C(colour ?? Palette.Ink);
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                var inset = x >= 4 && y >= 4 && x < size - 4 && y < size - 4;
                image.SetPixel(x, y, edge ? ink : filled ? (inset ? C(Palette.Card) : ink) : new Color(0, 0, 0, 0));
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A filled disc (slider grabbers).</summary>
    public static ImageTexture Circle(int size, Rgb colour)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var r = size / 2f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Godot.Vector2(x + 0.5f - r, y + 0.5f - r).Length();
                image.SetPixel(x, y, C(colour, Math.Clamp(r - d, 0, 1)));
            }
        return ImageTexture.CreateFromImage(image);
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

    /// <summary>Text whose names are links: clicking one calls <paramref name="open"/> with its entity.</summary>
    public static RichTextLabel Linked(LinkedText text, string name, Action<EntityRef> open, int size = 13, Rgb? colour = null, bool serif = false)
    {
        var label = new RichTextLabel
        {
            Name = name, BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = Markup.Bbcode(text), MetaUnderlined = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("normal_font_size", size);
        label.AddThemeColorOverride("default_color", C(colour ?? Palette.Body));
        if (serif) label.AddThemeFontOverride("normal_font", DisplayUpright);
        label.MetaClicked += meta => open(Importance.ParseRef(meta.AsString()));
        return label;
    }

    /// <summary>A small filled circle (category dots, the status light).</summary>
    public static Panel Dot(Rgb colour, float size = 8, string name = "Dot")
    {
        var dot = new Panel { Name = name, CustomMinimumSize = new Godot.Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dot.AddThemeStyleboxOverride("panel", Round(C(colour), null));
        return dot;
    }

    /// <summary>A pill-shaped panel (labels on pins, the status pill).</summary>
    public static PanelContainer Capsule(string name, Color fill, Color? border = null, int padX = 10, int padY = 3)
    {
        var pill = new PanelContainer { Name = name };
        var style = Round(fill, border);
        (style.ContentMarginLeft, style.ContentMarginRight, style.ContentMarginTop, style.ContentMarginBottom) = (padX, padX, padY, padY);
        pill.AddThemeStyleboxOverride("panel", style);
        return pill;
    }

    /// <summary>A row with a hairline rule above it, like the digest's list.</summary>
    public static PanelContainer Ruled(string name, Control content, bool ruleBelow = false)
    {
        var row = new PanelContainer { Name = name };
        row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0), BorderColor = C(Palette.Rule), BorderWidthTop = 1, BorderWidthBottom = ruleBelow ? 1 : 0,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        row.AddChild(content);
        return row;
    }

    public static StyleBoxFlat Round(Color fill, Color? border)
    {
        var style = new StyleBoxFlat
        {
            BgColor = fill, CornerRadiusBottomLeft = 999, CornerRadiusBottomRight = 999, CornerRadiusTopLeft = 999, CornerRadiusTopRight = 999,
        };
        if (border is { } b) (style.BorderColor, style.BorderWidthBottom, style.BorderWidthTop, style.BorderWidthLeft, style.BorderWidthRight) = (b, 1, 1, 1, 1);
        return style;
    }

    /// <summary>
    /// Pins a control to a corner of its parent: anchors (0 or 1 across and down) plus an offset to its near corner;
    /// a control anchored right or bottom grows left or up as its content grows, so the margin holds.
    /// </summary>
    public static T Place<T>(T control, float anchorX, float anchorY, float x, float y) where T : Control
    {
        (control.AnchorLeft, control.AnchorRight, control.AnchorTop, control.AnchorBottom) = (anchorX, anchorX, anchorY, anchorY);
        (control.OffsetLeft, control.OffsetRight, control.OffsetTop, control.OffsetBottom) = (x, x, y, y);
        control.GrowHorizontal = anchorX > 0 ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        control.GrowVertical = anchorY > 0 ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        return control;
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

using Godot;

namespace Dogeometric.App.UI;

/// <summary>Light theme matching SketchUp 2021 on Windows (colours sampled from screenshots).</summary>
public static class LightTheme
{
    public static readonly Color Text = Color.Color8(43, 43, 43);
    public static readonly Color TextDisabled = Color.Color8(160, 160, 160);
    public static readonly Color MenuBackground = Color.Color8(255, 255, 255);
    public static readonly Color BarBackground = Color.Color8(245, 245, 245);
    public static readonly Color Hover = Color.Color8(204, 232, 255);
    public static readonly Color Border = Color.Color8(204, 204, 204);

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 14 };

        foreach (var type in new[] { "Label", "Button", "MenuBar", "PopupMenu", "LineEdit", "CheckBox", "AcceptDialog", "TooltipLabel" })
        {
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_disabled_color", type, TextDisabled);
            theme.SetColor("font_hover_color", type, Text);
            theme.SetColor("font_pressed_color", type, Text);
            theme.SetColor("font_focus_color", type, Text);
            theme.SetColor("font_hover_pressed_color", type, Text);
        }

        var flat = Box(MenuBackground);
        var hover = Box(Hover);
        theme.SetStylebox("normal", "MenuBar", Box(Colors.Transparent, 8, 3));
        theme.SetStylebox("hover", "MenuBar", Box(Hover, 8, 3));
        theme.SetStylebox("pressed", "MenuBar", Box(Hover, 8, 3));
        theme.SetStylebox("hover_pressed", "MenuBar", Box(Hover, 8, 3));
        theme.SetStylebox("disabled", "MenuBar", Box(Colors.Transparent, 8, 3));

        var popupPanel = Box(MenuBackground, 2, 2);
        popupPanel.BorderColor = Border;
        popupPanel.SetBorderWidthAll(1);
        theme.SetStylebox("panel", "PopupMenu", popupPanel);
        theme.SetStylebox("hover", "PopupMenu", hover);
        theme.SetColor("font_separator_color", "PopupMenu", TextDisabled);
        theme.SetColor("font_accelerator_color", "PopupMenu", Text);
        // SketchUp only draws a mark on checked items; unchecked ones are blank.
        var blank = new PlaceholderTexture2D { Size = new Vector2(12, 12) };
        theme.SetIcon("unchecked", "PopupMenu", blank);
        theme.SetIcon("radio_unchecked", "PopupMenu", blank);
        var separator = new StyleBoxLine { Color = Border, Thickness = 1 };
        theme.SetStylebox("separator", "PopupMenu", separator);

        var edit = Box(MenuBackground, 6, 2);
        edit.BorderColor = Border;
        edit.SetBorderWidthAll(1);
        theme.SetStylebox("normal", "LineEdit", edit);
        theme.SetStylebox("focus", "LineEdit", edit);
        theme.SetStylebox("read_only", "LineEdit", edit);
        theme.SetColor("font_uneditable_color", "LineEdit", Text);
        theme.SetColor("caret_color", "LineEdit", Text);

        theme.SetStylebox("panel", "PanelContainer", Box(BarBackground, 6, 3));
        theme.SetStylebox("panel", "AcceptDialog", flat);
        theme.SetStylebox("panel", "TooltipPanel", popupPanel);
        return theme;
    }

    public static StyleBoxFlat Box(Color color, int padX = 0, int padY = 0) => new()
    {
        BgColor = color,
        ContentMarginLeft = padX,
        ContentMarginRight = padX,
        ContentMarginTop = padY,
        ContentMarginBottom = padY,
    };
}

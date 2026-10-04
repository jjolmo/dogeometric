using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>A collapsible panel of SketchUp's tray: a header bar with a triangle, and its content below.</summary>
public partial class TraySection : VBoxContainer
{
    private Button _header = null!;
    private MarginContainer _body = null!;

    public Control Content { get; private set; } = null!;

    public static TraySection Create(string title, Control content, bool expanded = true)
    {
        var s = new TraySection { Name = title, Content = content };
        s.AddThemeConstantOverride("separation", 0);
        s._header = new Button
        {
            Text = (expanded ? "▼  " : "▶  ") + title,
            Alignment = HorizontalAlignment.Left,
            Flat = false,
            FocusMode = FocusModeEnum.None,
        };
        s._header.AddThemeStyleboxOverride("normal", LightTheme.Box(new Color(0.88f, 0.88f, 0.88f), 6, 2));
        s._header.AddThemeStyleboxOverride("hover", LightTheme.Box(new Color(0.82f, 0.86f, 0.92f), 6, 2));
        s._header.AddThemeStyleboxOverride("pressed", LightTheme.Box(new Color(0.82f, 0.86f, 0.92f), 6, 2));
        s._header.Pressed += () => s.Toggle(title);
        s.AddChild(s._header);
        s._body = new MarginContainer { Visible = expanded };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            s._body.AddThemeConstantOverride($"margin_{side}", 6);
        s._body.AddChild(content);
        s.AddChild(s._body);
        return s;
    }

    private void Toggle(string title)
    {
        _body.Visible = !_body.Visible;
        _header.Text = (_body.Visible ? "▼  " : "▶  ") + title;
    }
}

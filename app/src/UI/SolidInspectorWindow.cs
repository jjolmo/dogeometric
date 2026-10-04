using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// Solid Inspector²'s window: "Fix All" on top, then one row per kind of error with its name, a large count, a "?"
/// that unfolds the explanation and a Fix (or Info) button. Clicking a row shows only that kind in the model;
/// clicking the empty area shows them all again.
/// </summary>
public sealed partial class SolidInspectorWindow : Window
{
    private static readonly Color RowColor = Color.Color8(238, 238, 238);
    private static readonly Color SelectedColor = Color.Color8(255, 224, 215);
    private static readonly Color CountColor = Color.Color8(170, 170, 170);

    private VBoxContainer _rows = null!;
    private Button _fixAll = null!;
    private Action<SolidErrorKind> _fixKind = null!;
    private Action<SolidErrorKind?> _select = null!;
    private SolidErrorKind? _selected;

    public static SolidInspectorWindow Open(Node owner, Action fixAll, Action<SolidErrorKind> fixKind, Action<SolidErrorKind?> select, Func<InputEventKey, bool> key)
    {
        var w = new SolidInspectorWindow
        {
            Title = "Solid Inspector²",
            Size = new Vector2I(400, 600),
            MinSize = new Vector2I(400, 250),
            Transient = true,
            Theme = LightTheme.Create(),
            _fixKind = fixKind,
            _select = select,
        };
        var root = new PanelContainer();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeStyleboxOverride("panel", LightTheme.Box(Colors.White));
        var layout = new VBoxContainer();
        root.AddChild(layout);

        var top = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            top.AddThemeConstantOverride($"margin_{side}", 8);
        w._fixAll = new Button { Text = "Fix All", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        w._fixAll.Pressed += fixAll;
        top.AddChild(w._fixAll);
        layout.AddChild(top);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        w._rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        w._rows.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(w._rows);
        // Clicking outside the rows shows every kind again.
        scroll.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                w.Select(null);
        };
        layout.AddChild(scroll);
        w.AddChild(root);

        // Keys typed while the window has focus still drive the tool (Esc, Tab, arrows, Return), as in the extension.
        w.WindowInput += e =>
        {
            if (e is InputEventKey { Pressed: true } k && key(k))
                w.SetInputAsHandled();
        };
        owner.GetTree().Root.AddChild(w);
        // Beside the main window's right edge, as the extension's dialog first opens.
        var main = owner.GetWindow();
        w.Position = main.Position + new Vector2I(Math.Max(main.Size.X - w.Size.X - 40, 0), 120);
        w.Show();
        return w;
    }

    public void List(List<SolidError> errors, SolidErrorKind? selected)
    {
        _selected = selected;
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        var groups = errors.GroupBy(e => e.Kind).ToList();
        _fixAll.Disabled = groups.Count == 0;
        if (groups.Count == 0)
        {
            var none = new Label
            {
                Text = "No Errors\nEverything is shiny\n\n:)",
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 220),
                VerticalAlignment = VerticalAlignment.Center,
            };
            none.AddThemeColorOverride("font_color", Color.Color8(153, 153, 153));
            none.AddThemeFontSizeOverride("font_size", 22);
            _rows.AddChild(none);
            return;
        }
        foreach (var group in groups)
            _rows.AddChild(Row(group.Key, group.Count()));
    }

    private Control Row(SolidErrorKind kind, int count)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = LightTheme.Box(kind == _selected ? SelectedColor : RowColor, 10, 10);
        style.BorderColor = LightTheme.Border;
        style.BorderWidthBottom = 1;
        panel.AddThemeStyleboxOverride("panel", style);
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Select(kind == _selected ? null : kind);
        };

        var line = new HBoxContainer();
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var titleRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var title = new Label { Text = SolidInspector.DisplayName(kind), MouseFilter = Control.MouseFilterEnum.Ignore };
        title.AddThemeColorOverride("font_color", LightTheme.Text);
        titleRow.AddChild(title);
        var help = new Button { Text = "?", Flat = true, TooltipText = "Click to expand help", CustomMinimumSize = new Vector2(22, 22) };
        titleRow.AddChild(help);
        text.AddChild(titleRow);
        var number = new Label { Text = count.ToString(), MouseFilter = Control.MouseFilterEnum.Ignore };
        number.AddThemeFontSizeOverride("font_size", 44);
        number.AddThemeColorOverride("font_color", CountColor);
        text.AddChild(number);
        var description = new Label
        {
            Text = SolidInspector.Description(kind),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            CustomMinimumSize = new Vector2(240, 0),
        };
        var descriptionPanel = new PanelContainer { Visible = false };
        descriptionPanel.AddThemeStyleboxOverride("panel", LightTheme.Box(new Color(0, 0, 0, 0.8f), 10, 10));
        description.AddThemeColorOverride("font_color", Colors.White);
        description.Visible = true;
        descriptionPanel.AddChild(description);
        text.AddChild(descriptionPanel);
        help.Pressed += () => descriptionPanel.Visible = !descriptionPanel.Visible;
        line.AddChild(text);

        var fixable = SolidInspector.IsFixable(kind);
        var fix = new Button { Text = fixable ? "Fix" : "Info", CustomMinimumSize = new Vector2(80, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        fix.Pressed += () =>
        {
            if (fixable)
                _fixKind(kind);
            else
                Message(SolidInspector.Description(kind));
        };
        line.AddChild(fix);
        panel.AddChild(line);
        return panel;
    }

    private void Select(SolidErrorKind? kind)
    {
        _selected = kind;
        _select(kind);
    }

    public void Message(string text)
    {
        var dialog = new AcceptDialog { Title = "Solid Inspector²", DialogText = text, DialogAutowrap = true, Size = new Vector2I(380, 0) };
        AddChild(dialog);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered();
    }
}

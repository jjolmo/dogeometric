using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>A SketchUp toolbar: icon buttons bound to commands. Unimplemented commands show disabled.</summary>
public partial class Toolbar : PanelContainer
{
    public const int Separator = 0;

    private readonly List<(Button Button, Command Command)> _buttons = [];
    private Container _box = null!;

    /// <param name="layout">Command ids in order; <see cref="Separator"/> inserts a gap.</param>
    /// <param name="columns">0 lays the buttons out in a row; 2 makes SketchUp's two-column Large Tool Set.</param>
    public static Toolbar Create(string name, CommandRegistry registry, IReadOnlyDictionary<int, string> icons, int[] layout, int columns = 0)
    {
        var bar = new Toolbar { Name = name };
        bar.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground, 2, 2));
        bar._box = columns > 0
            ? new GridContainer { Columns = columns }
            : new HBoxContainer();
        bar._box.AddThemeConstantOverride("separation", 1);
        bar._box.AddThemeConstantOverride("h_separation", 1);
        bar._box.AddThemeConstantOverride("v_separation", 1);
        bar.AddChild(bar._box);

        foreach (var id in layout)
        {
            if (id == Separator)
            {
                if (columns == 0)
                    bar._box.AddChild(new VSeparator());
                continue;
            }
            var cmd = registry.Get(id);
            var button = new Button
            {
                Flat = true,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(30, 30),
                ExpandIcon = false,
                IconAlignment = HorizontalAlignment.Center,
                TooltipText = Tooltip(cmd),
            };
            if (icons.TryGetValue(id, out var icon) && ResourceLoader.Exists($"res://icons/{icon}.svg"))
                button.Icon = GD.Load<Texture2D>($"res://icons/{icon}.svg");
            button.Pressed += () => registry.Execute(id);
            bar._box.AddChild(button);
            bar._buttons.Add((button, cmd));
        }
        bar.Refresh();
        return bar;
    }

    /// <summary>Re-reads enabled and checked state (active tool, projection, toggles).</summary>
    public void Refresh()
    {
        foreach (var (button, cmd) in _buttons)
        {
            button.Disabled = !cmd.IsImplemented;
            button.Modulate = cmd.IsImplemented ? Colors.White : new Color(1, 1, 1, 0.35f);
            var on = cmd.IsChecked?.Invoke() == true;
            button.Flat = !on;
        }
    }

    private static string Tooltip(Command cmd)
    {
        var shortcut = CommandRegistry.ShortcutText(cmd.Shortcut);
        var title = cmd.Label.Length > 0 ? cmd.Label.TrimEnd('.') : cmd.Description;
        return shortcut.Length > 0 ? $"{title} ({shortcut})" : title;
    }
}

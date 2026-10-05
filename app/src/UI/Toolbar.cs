using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// A SketchUp toolbar: a grip to drag it by, then icon buttons bound to commands (unimplemented ones show
/// disabled). It lays out horizontally in the top and bottom docks and vertically in the side docks; the Large
/// Tool Set keeps its two columns (or two rows).
/// </summary>
public partial class Toolbar : PanelContainer
{
    public const int Separator = 0;

    /// <summary>Toolbar items that are controls rather than buttons (the Tags toolbar's list), by their id.</summary>
    public static Dictionary<int, Func<Control>> Widgets { get; } = [];

    private readonly List<(Button Button, Command Command)> _buttons = [];
    private CommandRegistry _registry = null!;
    private IReadOnlyDictionary<int, string> _icons = null!;
    private int[] _layout = [];
    private int _lines;
    private Container _box = null!;
    private Control _grip = null!;

    public string Title { get; private set; } = "";

    /// <summary>In a top or bottom dock, this toolbar begins a new row.</summary>
    public bool RowStart { get; set; }
    public bool Vertical { get; private set; }

    /// <summary>The grip was pressed: the dock manager takes over the drag.</summary>
    public event Action<Toolbar>? DragStarted;

    /// <param name="layout">Command ids in order; <see cref="Separator"/> inserts a gap.</param>
    /// <param name="lines">0 lays the buttons out in one line; 2 makes SketchUp's two-column Large Tool Set.</param>
    public static Toolbar Create(string name, CommandRegistry registry, IReadOnlyDictionary<int, string> icons, int[] layout, int lines = 0, bool vertical = false)
    {
        var bar = new Toolbar { Name = name.Replace(" ", ""), Title = name, _registry = registry, _icons = icons, _layout = layout, _lines = lines };
        bar.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground, 2, 2));
        bar.Build(lines > 0 || vertical);
        return bar;
    }

    /// <summary>Lays the toolbar out again along the other direction (docking it on a side or on top).</summary>
    public void SetVertical(bool vertical)
    {
        if (vertical != Vertical)
            Build(vertical);
    }

    private void Build(bool vertical)
    {
        Vertical = vertical;
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        _buttons.Clear();

        var outer = vertical ? (BoxContainer)new VBoxContainer() : new HBoxContainer();
        outer.AddThemeConstantOverride("separation", 2);
        AddChild(outer);

        // SketchUp's dotted grip at the start of the toolbar.
        _grip = new Grip { Vertical = vertical, CustomMinimumSize = vertical ? new Vector2(0, 8) : new Vector2(8, 0), MouseDefaultCursorShape = CursorShape.Move, TooltipText = Title };
        _grip.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _grip.AcceptEvent();
                DragStarted?.Invoke(this);
            }
        };
        outer.AddChild(_grip);

        if (_lines > 0)
        {
            // Large Tool Set: two columns standing, two rows lying down.
            var count = _layout.Count(id => id != Separator);
            _box = new GridContainer { Columns = vertical ? _lines : (count + _lines - 1) / _lines };
        }
        else
        {
            _box = vertical ? new VBoxContainer() : new HBoxContainer();
        }
        _box.AddThemeConstantOverride("separation", 1);
        _box.AddThemeConstantOverride("h_separation", 1);
        _box.AddThemeConstantOverride("v_separation", 1);
        outer.AddChild(_box);

        var ids = _layout;
        if (_lines > 0 && !vertical)
        {
            // The layout is read in pairs (left/right column); lying down, the pairs become the two rows.
            var plain = _layout.Where(id => id != Separator).ToList();
            ids = plain.Where((_, i) => i % 2 == 0).Concat(plain.Where((_, i) => i % 2 == 1)).ToArray();
        }
        foreach (var id in ids)
        {
            if (id == Separator)
            {
                if (_lines == 0)
                    _box.AddChild(vertical ? new HSeparator() : new VSeparator());
                continue;
            }
            if (Widgets.TryGetValue(id, out var widget))
            {
                _box.AddChild(widget());
                continue;
            }
            var cmd = _registry.Get(id);
            var button = new Button
            {
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(30, 30),
                ExpandIcon = false,
                IconAlignment = HorizontalAlignment.Center,
                TooltipText = Tooltip(cmd),
            };
            if (_icons.TryGetValue(id, out var icon) && ResourceLoader.Exists($"res://icons/{icon}.svg"))
                button.Icon = GD.Load<Texture2D>($"res://icons/{icon}.svg");
            button.AddThemeStyleboxOverride("hover", HoverBox);
            button.AddThemeStyleboxOverride("pressed", OnBox);
            button.AddThemeStyleboxOverride("hover_pressed", OnBox);
            button.AddThemeStyleboxOverride("disabled", Rest);
            button.AddThemeStyleboxOverride("focus", Rest);
            button.Pressed += () => _registry.Execute(id);
            _box.AddChild(button);
            _buttons.Add((button, cmd));
        }
        Refresh();
    }

    // SketchUp's toolbar buttons: bare at rest, a blue-edged box under the mouse, a deeper one while active.
    private static readonly StyleBox Rest = new StyleBoxEmpty();
    private static readonly StyleBox HoverBox = Edged(LightTheme.Hover);
    private static readonly StyleBox OnBox = Edged(Color.Color8(179, 215, 243));

    private static StyleBoxFlat Edged(Color fill)
    {
        var box = LightTheme.Box(fill);
        box.BorderColor = Color.Color8(0, 120, 215);
        box.SetBorderWidthAll(1);
        return box;
    }

    /// <summary>Re-reads enabled and checked state (active tool, projection, toggles).</summary>
    public void Refresh()
    {
        foreach (var (button, cmd) in _buttons)
        {
            button.Disabled = !cmd.IsImplemented;
            button.Modulate = cmd.IsImplemented ? Colors.White : new Color(1, 1, 1, 0.35f);
            var on = cmd.IsChecked?.Invoke() == true;
            button.AddThemeStyleboxOverride("normal", on ? OnBox : Rest);
            button.TooltipText = Tooltip(cmd);
        }
    }

    private static string Tooltip(Command cmd)
    {
        var shortcut = CommandRegistry.ShortcutText(cmd.Shortcut);
        var title = cmd.Label.Length > 0 ? cmd.Label.TrimEnd('.') : cmd.Description;
        return shortcut.Length > 0 ? $"{title} ({shortcut})" : title;
    }

    /// <summary>The grip: two rows of dots along the toolbar's start, as Windows toolbars draw it.</summary>
    private sealed partial class Grip : Control
    {
        public bool Vertical { get; set; }

        public override void _Draw()
        {
            var dot = new Color(0.55f, 0.55f, 0.55f);
            var length = Vertical ? Size.X : Size.Y;
            for (var t = 4f; t < length - 3; t += 4)
            {
                for (var k = 0; k < 2; k++)
                {
                    var p = Vertical ? new Vector2(t, 2 + k * 3) : new Vector2(2 + k * 3, t);
                    DrawRect(new Rect2(p, new Vector2(1.5f, 1.5f)), dot);
                }
            }
        }
    }
}

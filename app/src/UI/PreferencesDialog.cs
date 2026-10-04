using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's Preferences › Shortcuts: filter the commands, pick one, press Add and type the keys (or Remove).
/// Shortcuts are saved for the next session; the menus show them at once.
/// </summary>
public partial class PreferencesDialog : AcceptDialog
{
    private CommandRegistry _commands = null!;
    private Action _changed = null!;
    private ItemList _list = null!;
    private Label _assigned = null!;
    private Button _add = null!;
    private bool _capturing;
    private List<Command> _shown = [];

    public static void Show(Node parent, CommandRegistry commands, Action changed)
    {
        var d = new PreferencesDialog { Title = "Preferences", OkButtonText = "OK", _commands = commands, _changed = changed };
        var root = new HBoxContainer { CustomMinimumSize = new Vector2(640, 400) };
        var sections = new ItemList { CustomMinimumSize = new Vector2(130, 0) };
        sections.AddItem("Shortcuts");
        sections.Select(0);
        root.AddChild(sections);

        var pane = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var filter = new LineEdit { PlaceholderText = "Filter" };
        pane.AddChild(filter);
        d._list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        pane.AddChild(d._list);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Assigned:" });
        d._assigned = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(d._assigned);
        d._add = new Button { Text = "Add" };
        var remove = new Button { Text = "Remove" };
        row.AddChild(d._add);
        row.AddChild(remove);
        pane.AddChild(row);
        root.AddChild(pane);
        d.AddChild(root);

        filter.TextChanged += t => d.Fill(t);
        d._list.ItemSelected += _ => d.ShowAssigned();
        d._add.Pressed += () =>
        {
            if (d.Selected() == null)
                return;
            d._capturing = true;
            d._assigned.Text = "Type the keys…";
        };
        remove.Pressed += () =>
        {
            if (d.Selected() is { } cmd)
            {
                commands.SetShortcut(cmd.Id, Key.None);
                d.Saved();
            }
        };
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.Fill("");
        d.PopupCentered();
        filter.GrabFocus();
    }

    private void Fill(string filter)
    {
        _list.Clear();
        _shown = _commands.All.Where(c => c.IsImplemented && c.MenuPath.Length > 0)
            .Where(c => filter.Length == 0 || c.MenuPath.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.MenuPath, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var c in _shown)
            _list.AddItem(c.MenuPath);
        _assigned.Text = "";
    }

    private Command? Selected() => _list.GetSelectedItems() is [var i] && i < _shown.Count ? _shown[i] : null;

    private void ShowAssigned() => _assigned.Text = Selected() is { } c ? CommandRegistry.ShortcutText(c.Shortcut) : "";

    private void Saved()
    {
        _commands.SaveUserShortcuts();
        _changed();
        ShowAssigned();
    }

    public override void _Input(InputEvent e)
    {
        if (!_capturing || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        // Modifier keys alone wait for the real key.
        if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta)
            return;
        GetViewport().SetInputAsHandled();
        _capturing = false;
        if (key.Keycode == Key.Escape || Selected() is not { } cmd)
        {
            ShowAssigned();
            return;
        }
        _commands.SetShortcut(cmd.Id, key.GetKeycodeWithModifiers());
        Saved();
    }
}

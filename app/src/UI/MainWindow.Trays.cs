using Dogeometric.App.Commands;
using Dogeometric.App.UI.Tray;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's trays: Window › New Tray and Manage Trays, and the trays listed in the Window menu.</summary>
public partial class MainWindow
{
    private const string DefaultTray = "Default Tray";
    private readonly Dictionary<string, TraySection> _panels = [];

    /// <summary>The trays, with every panel in one of them: the Default Tray holds, in their usual order, those no
    /// other tray has.</summary>
    private List<TrayLayout> Trays()
    {
        var trays = AppPreferences.Current.Trays;
        if (!trays.Any(t => t.Name == DefaultTray))
            trays.Insert(0, new TrayLayout { Name = DefaultTray });
        foreach (var t in trays)
            t.Panels.RemoveAll(p => !_panels.ContainsKey(p));
        var elsewhere = trays.Where(t => t.Name != DefaultTray).SelectMany(t => t.Panels).ToHashSet();
        trays.First(t => t.Name == DefaultTray).Panels = [.. _panels.Keys.Where(p => !elsewhere.Contains(p))];
        return trays;
    }

    /// <summary>Shows the visible trays on the right: one as a plain column, several as tabs.</summary>
    private void LayoutTrays()
    {
        foreach (var section in _panels.Values)
            section.GetParent()?.RemoveChild(section);
        foreach (var c in _tray.GetChildren())
        {
            _tray.RemoveChild(c);
            c.QueueFree();
        }
        var visible = Trays().Where(t => t.Visible).ToList();
        _tray.Visible = visible.Count > 0;
        Control Column(TrayLayout tray, bool titled)
        {
            var scroll = new ScrollContainer { Name = tray.Name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            scroll.AddChild(list);
            if (titled)
            {
                var title = new Label { Text = tray.Name, MouseFilter = MouseFilterEnum.Stop };
                title.GuiInput += e => TrayMenuOn(e, tray, title);
                list.AddChild(title);
            }
            foreach (var p in tray.Panels)
                list.AddChild(_panels[p]);
            return scroll;
        }
        if (visible.Count == 1)
            _tray.AddChild(Column(visible[0], true));
        else if (visible.Count > 1)
        {
            var tabs = new TabContainer();
            tabs.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground));
            foreach (var t in visible)
                tabs.AddChild(Column(t, false));
            tabs.GetTabBar().GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb
                    && tabs.GetTabBar().GetTabIdxAtPoint(mb.Position) is var i and >= 0)
                    TrayMenuOn(e, visible[i], tabs.GetTabBar());
            };
            _tray.AddChild(tabs);
        }
    }

    /// <summary>The tray header's right-click menu: hide, rename or delete the tray, and add or remove its panels.</summary>
    private void TrayMenuOn(InputEvent e, TrayLayout tray, Control at)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb)
            return;
        var menu = new PopupMenu();
        var actions = new List<Action>();
        void Item(string label, Action run, bool enabled = true)
        {
            menu.AddItem(label, actions.Count);
            menu.SetItemDisabled(menu.ItemCount - 1, !enabled);
            actions.Add(run);
        }
        Item("Hide Tray", () =>
        {
            tray.Visible = false;
            SaveTrays();
        });
        Item("Rename Tray", () => ShowTrayDialog(tray), tray.Name != DefaultTray);
        Item("Delete Tray", () =>
        {
            AppPreferences.Current.Trays.Remove(tray);
            SaveTrays();
        }, tray.Name != DefaultTray);
        menu.AddSeparator();
        actions.Add(() => { });
        foreach (var panel in _panels.Keys)
        {
            var id = actions.Count;
            menu.AddCheckItem(panel, id);
            menu.SetItemChecked(menu.ItemCount - 1, tray.Panels.Contains(panel));
            // The Default Tray takes back what others drop, so it can't drop panels itself.
            menu.SetItemDisabled(menu.ItemCount - 1, tray.Name == DefaultTray && tray.Panels.Contains(panel));
            actions.Add(() =>
            {
                if (!tray.Panels.Remove(panel))
                {
                    foreach (var other in Trays().Where(t => t != tray))
                        other.Panels.Remove(panel);
                    tray.Panels.Add(panel);
                }
                SaveTrays();
            });
        }
        menu.IdPressed += id => actions[(int)id]();
        menu.PopupHide += menu.QueueFree;
        AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)(at.GetScreenPosition() + mb.Position), Vector2I.Zero));
    }

    private void SaveTrays()
    {
        AppPreferences.Save();
        LayoutTrays();
    }

    private void RegisterTrays()
    {
        _commands.DynamicItems[CommandIds.TrayPlaceholder] = () => Trays().Select(t => new DynamicItem(t.Name, () =>
        {
            t.Visible = !t.Visible;
            SaveTrays();
        }, t.Visible));
        _commands.Register(CommandIds.NewTray, () => ShowTrayDialog(null));
        _commands.Register(CommandIds.ManageTrays, ShowManageTrays);
    }

    /// <summary>New Tray (or Rename for an existing one): a name and the panels it holds.</summary>
    private void ShowTrayDialog(TrayLayout? existing)
    {
        var d = new ConfirmationDialog { Title = existing == null ? "New Tray" : "Rename Tray", OkButtonText = "OK", Theme = LightTheme.Create() };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        box.AddChild(new Label { Text = "Name:" });
        var name = new LineEdit { Text = existing?.Name ?? $"Tray {Trays().Count}" };
        box.AddChild(name);
        box.AddChild(new Label { Text = "Panels:" });
        var checks = _panels.Keys.Select(p => new CheckBox { Text = p, ButtonPressed = existing?.Panels.Contains(p) ?? false }).ToList();
        foreach (var c in checks)
            box.AddChild(c);
        d.AddChild(box);
        d.RegisterTextEnter(name);
        d.Confirmed += () =>
        {
            var title = name.Text.Trim();
            if (title.Length > 0 && (existing?.Name == title || Trays().All(t => t.Name != title)))
            {
                var tray = existing ?? new TrayLayout();
                if (existing == null)
                    AppPreferences.Current.Trays.Add(tray);
                tray.Name = existing?.Name == DefaultTray ? DefaultTray : title;
                var chosen = checks.Where(c => c.ButtonPressed).Select(c => c.Text).ToList();
                foreach (var other in Trays().Where(t => t != tray))
                    other.Panels.RemoveAll(chosen.Contains);
                tray.Panels = [.. tray.Panels.Where(chosen.Contains), .. chosen.Where(p => !tray.Panels.Contains(p))];
                SaveTrays();
            }
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
        name.GrabFocus();
    }

    /// <summary>Window › Manage Trays: show or hide trays, add, rename or delete them.</summary>
    private void ShowManageTrays()
    {
        var d = new AcceptDialog { Title = "Manage Trays", OkButtonText = "Close", Theme = LightTheme.Create() };
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(420, 220) };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(list);
        var buttons = new VBoxContainer();
        row.AddChild(buttons);
        TrayLayout? selected = null;
        void Fill()
        {
            foreach (var c in list.GetChildren())
            {
                list.RemoveChild(c);
                c.QueueFree();
            }
            var group = new ButtonGroup();
            foreach (var t in Trays())
            {
                var line = new HBoxContainer();
                var visible = new CheckBox { ButtonPressed = t.Visible, TooltipText = "Show this tray" };
                visible.Toggled += on =>
                {
                    t.Visible = on;
                    SaveTrays();
                };
                line.AddChild(visible);
                var pick = new Button { Text = t.Name, ToggleMode = true, ButtonGroup = group, Flat = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left };
                pick.Toggled += on =>
                {
                    if (on)
                        selected = t;
                };
                line.AddChild(pick);
                list.AddChild(line);
            }
        }
        Button Action(string text, Action run)
        {
            var b = new Button { Text = text };
            b.Pressed += run;
            buttons.AddChild(b);
            return b;
        }
        Action("New...", () => ShowTrayDialog(null));
        Action("Rename...", () =>
        {
            if (selected != null)
                ShowTrayDialog(selected);
        });
        Action("Delete", () =>
        {
            if (selected == null || selected.Name == DefaultTray)
                return;
            // Its panels go back to the Default Tray.
            AppPreferences.Current.Trays.Remove(selected);
            selected = null;
            SaveTrays();
            Fill();
        });
        d.AddChild(row);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        d.VisibilityChanged += () =>
        {
            if (d.Visible)
                Fill();
        };
        AddChild(d);
        Fill();
        d.PopupCentered();
    }
}

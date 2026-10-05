using Dogeometric.App.Commands;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// Window › Preferences, laid out as SketchUp 2021's: sections on the left, the chosen one on the right. Changes
/// apply at once and are kept for the next session.
/// </summary>
public partial class PreferencesDialog : AcceptDialog
{
    private CommandRegistry _commands = null!;
    private Action _menusChanged = null!;
    private Action _resetWorkspace = null!;
    private Control _pane = null!;

    private Tree _shortcutTree = null!;
    private LineEdit _filter = null!;
    private Label _assigned = null!;
    private bool _capturing;
    private List<Command> _shown = [];

    private static readonly string[] Sections = ["Accessibility", "Applications", "Compatibility", "Drawing", "Extensions", "Files", "General", "Graphics", "Shortcuts", "Template", "Workspace"];

    public static readonly string[] FileKinds = ["Models", "Components", "Materials", "Styles", "Texture images", "Watermark images", "Export", "Classifications", "Templates"];

    /// <summary>The section shown last, opened again next time.</summary>
    private static string _lastSection = "General";

    public static void Show(Node parent, CommandRegistry commands, Action menusChanged, Action resetWorkspace, string? section = null)
    {
        if (section != null)
            _lastSection = section;
        var d = new PreferencesDialog
        {
            Title = "Preferences",
            OkButtonText = "OK",
            _commands = commands,
            _menusChanged = menusChanged,
            _resetWorkspace = resetWorkspace,
        };
        var root = new HBoxContainer { CustomMinimumSize = new Vector2(720, 470) };
        var sections = new ItemList { CustomMinimumSize = new Vector2(140, 0) };
        foreach (var s in Sections)
            sections.AddItem(s);
        root.AddChild(sections);
        root.AddChild(new VSeparator());
        d._pane = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddChild(d._pane);
        d.AddChild(root);

        sections.ItemSelected += i => d.ShowSection(Sections[i]);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        var first = Array.IndexOf(Sections, _lastSection);
        sections.Select(first);
        d.ShowSection(Sections[first]);
        d.PopupCentered();
    }

    private void ShowSection(string name)
    {
        _lastSection = name;
        _capturing = false;
        foreach (var c in _pane.GetChildren())
        {
            _pane.RemoveChild(c);
            c.QueueFree();
        }
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _pane.AddChild(box);
        var p = AppPreferences.Current;
        switch (name)
        {
            case "General":
                Heading(box, "Saving");
                Check(box, "Create backup", "Saving over a model keeps its previous version beside it as a .dogb file.",
                    p.CreateBackup, v => p.CreateBackup = v);
                var autoRow = new HBoxContainer();
                var auto = new CheckBox { Text = "Auto-backup every", ButtonPressed = p.AutoBackup, TooltipText =
                    "While the model has unsaved changes, a copy is made in the backups folder. Your own file is never written." };
                var minutes = new SpinBox { MinValue = 1, MaxValue = 120, Value = p.AutoBackupMinutes, Editable = p.AutoBackup };
                auto.Toggled += v =>
                {
                    p.AutoBackup = v;
                    minutes.Editable = v;
                    AppPreferences.Save();
                };
                minutes.ValueChanged += v =>
                {
                    p.AutoBackupMinutes = (int)v;
                    AppPreferences.Save();
                };
                autoRow.AddChild(auto);
                autoRow.AddChild(minutes);
                autoRow.AddChild(new Label { Text = "minutes" });
                box.AddChild(autoRow);
                var keepRow = new HBoxContainer();
                keepRow.AddChild(new Label { Text = "Backups kept per model" });
                var keep = new SpinBox { MinValue = 1, MaxValue = 500, Value = p.BackupsToKeep };
                keep.ValueChanged += v =>
                {
                    p.BackupsToKeep = (int)v;
                    AppPreferences.Save();
                };
                keepRow.AddChild(keep);
                box.AddChild(keepRow);
                var folderRow = new HBoxContainer();
                var folder = new Button { Text = "Open Backups Folder" };
                folder.Pressed += () =>
                {
                    System.IO.Directory.CreateDirectory(Backups.Folder);
                    OS.ShellOpen(Backups.Folder);
                };
                folderRow.AddChild(folder);
                box.AddChild(folderRow);
                Heading(box, "Startup");
                Check(box, "Offer to recover the model after Dogeometric closes unexpectedly",
                    "At start-up, if the last session crashed, its latest backup is offered.", p.CheckForCrashRecovery, v => p.CheckForCrashRecovery = v);
                Heading(box, "");
                var reset = new Button { Text = "Reset All Preferences", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                reset.Pressed += () =>
                {
                    AppPreferences.Reset();
                    ShowSection("General");
                };
                box.AddChild(reset);
                break;

            case "Drawing":
                Heading(box, "Miscellaneous");
                Check(box, "Continue line drawing", "The Line tool keeps drawing from the end of the last line until Esc or a closed face.",
                    p.ContinueLineDrawing, v => p.ContinueLineDrawing = v);
                Check(box, "Display crosshairs", "Drawing tools show lines along the red, green and blue axes through the cursor.",
                    p.DisplayCrosshairs, v => p.DisplayCrosshairs = v);
                Check(box, "Disable pre-pick on Push/Pull Tool", "Push/Pull always works on the face clicked, never on one selected beforehand.",
                    p.DisablePushPullPrePick, v => p.DisablePushPullPrePick = v);
                break;

            case "Compatibility":
                Heading(box, "Component/Group Highlighting");
                Check(box, "Bounding box only", "A selected group or component shows only its box, not its edges in blue.",
                    p.BoundingBoxOnly, v => p.BoundingBoxOnly = v);
                Heading(box, "Mouse Wheel Style");
                Check(box, "Invert", "Rolling the wheel forward zooms out instead of in.", p.InvertWheelZoom, v => p.InvertWheelZoom = v);
                break;

            case "Graphics":
                Heading(box, "Anti-aliasing");
                var aa = new OptionButton();
                int[] levels = [0, 2, 4, 8];
                foreach (var l in levels)
                    aa.AddItem(l == 0 ? "Off" : $"{l}x");
                aa.Select(Math.Max(0, Array.IndexOf(levels, p.Antialiasing)));
                aa.ItemSelected += i =>
                {
                    p.Antialiasing = levels[i];
                    AppPreferences.Save();
                };
                box.AddChild(aa);
                break;

            case "Accessibility":
            {
                Heading(box, "Axis and Direction Colors");
                var grid = new GridContainer { Columns = 2 };
                var pickers = new List<(ColorPickerButton Button, string Default)>();
                void Row(string label, string value, string fallback, Action<string> set)
                {
                    grid.AddChild(new Label { Text = label });
                    var pick = new ColorPickerButton { Color = new Color(value), CustomMinimumSize = new Vector2(80, 24), EditAlpha = false };
                    pick.ColorChanged += c =>
                    {
                        set("#" + c.ToHtml(false));
                        AppPreferences.Save();
                    };
                    grid.AddChild(pick);
                    pickers.Add((pick, fallback));
                }
                Row("Red Axis", p.RedAxisColor, AppPreferences.DefaultColors.Red, v => p.RedAxisColor = v);
                Row("Green Axis", p.GreenAxisColor, AppPreferences.DefaultColors.Green, v => p.GreenAxisColor = v);
                Row("Blue Axis", p.BlueAxisColor, AppPreferences.DefaultColors.Blue, v => p.BlueAxisColor = v);
                Row("Magenta Parallel / Perpendicular", p.ParallelColor, AppPreferences.DefaultColors.Parallel, v => p.ParallelColor = v);
                Row("Cyan Tangent", p.TangentColor, AppPreferences.DefaultColors.Tangent, v => p.TangentColor = v);
                box.AddChild(grid);
                var resetColors = new Button { Text = "Reset All", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                resetColors.Pressed += () =>
                {
                    (p.RedAxisColor, p.GreenAxisColor, p.BlueAxisColor, p.ParallelColor, p.TangentColor) = (AppPreferences.DefaultColors.Red,
                        AppPreferences.DefaultColors.Green, AppPreferences.DefaultColors.Blue, AppPreferences.DefaultColors.Parallel, AppPreferences.DefaultColors.Tangent);
                    AppPreferences.Save();
                    foreach (var (button, fallback) in pickers)
                        button.Color = new Color(fallback);
                };
                box.AddChild(resetColors);
                break;
            }

            case "Applications":
            {
                Heading(box, "Default Image Editor");
                var row = new HBoxContainer();
                var path = new LineEdit { Text = p.ImageEditor, Editable = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddChild(path);
                var choose = new Button { Text = "Choose..." };
                choose.Pressed += () => PickPath(FileDialog.FileModeEnum.OpenFile, v =>
                {
                    p.ImageEditor = v;
                    path.Text = v;
                    AppPreferences.Save();
                });
                row.AddChild(choose);
                box.AddChild(row);
                break;
            }

            case "Files":
            {
                Heading(box, "File Locations");
                var grid = new GridContainer { Columns = 3 };
                foreach (var kind in FileKinds)
                {
                    grid.AddChild(new Label { Text = kind + ":" });
                    var path = new LineEdit { Text = p.FileLocations.GetValueOrDefault(kind, ""), PlaceholderText = "(default)", Editable = false, CustomMinimumSize = new Vector2(330, 0) };
                    grid.AddChild(path);
                    var edit = new Button { Text = "Edit...", TooltipText = "Choose the folder" };
                    edit.Pressed += () => PickPath(FileDialog.FileModeEnum.OpenDir, v =>
                    {
                        p.FileLocations[kind] = v;
                        path.Text = v;
                        AppPreferences.Save();
                    });
                    grid.AddChild(edit);
                }
                box.AddChild(grid);
                break;
            }

            case "Template":
                Heading(box, "Default Drawing Template");
                var templates = Templates.All();
                var list = new ItemList { CustomMinimumSize = new Vector2(0, 300) };
                foreach (var t in templates)
                    list.AddItem(t.Description.Length > 0 ? $"{t.Name}  —  {t.Description}" : t.Name);
                list.Select(Math.Max(0, templates.IndexOf(Templates.Default)));
                list.ItemSelected += i =>
                {
                    p.DefaultTemplate = templates[(int)i].Name;
                    AppPreferences.Save();
                };
                box.AddChild(list);
                break;

            case "Workspace":
                Heading(box, "Workspace");
                var resetWs = new Button { Text = "Reset Workspace", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    TooltipText = "Toolbars back to their default places and visibility." };
                resetWs.Pressed += _resetWorkspace;
                box.AddChild(resetWs);
                break;

            case "Extensions":
                Heading(box, "Extensions (the reference SketchUp's, rebuilt natively)");
                var tree = new Tree { Columns = 3, HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ColumnTitlesVisible = true };
                tree.SetColumnTitle(0, "Extension");
                tree.SetColumnTitle(1, "By");
                tree.SetColumnTitle(2, "Status");
                tree.SetColumnExpand(1, false);
                tree.SetColumnCustomMinimumWidth(1, 150);
                tree.SetColumnExpand(2, false);
                tree.SetColumnCustomMinimumWidth(2, 90);
                var root = tree.CreateItem();
                foreach (var ext in ExtensionCatalog.All)
                {
                    var item = tree.CreateItem(root);
                    item.SetText(0, ext.Name);
                    item.SetTooltipText(0, ext.Description);
                    item.SetText(1, ext.Creator);
                    item.SetText(2, ext.Available ? "Available" : "Coming");
                    item.SetCustomColor(2, ext.Available ? Color.Color8(0, 140, 60) : LightTheme.TextDisabled);
                }
                box.AddChild(tree);
                break;

            case "Shortcuts":
                BuildShortcuts(box);
                break;
        }
    }

    private void PickPath(FileDialog.FileModeEnum mode, Action<string> picked)
    {
        var dialog = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = OS.GetEnvironment("DOGEOMETRIC_NO_NATIVE_DIALOGS") == "" };
        dialog.FileSelected += v =>
        {
            picked(v);
            dialog.QueueFree();
        };
        dialog.DirSelected += v =>
        {
            picked(v);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(800, 560));
    }

    private static void Heading(VBoxContainer box, string text)
    {
        if (text.Length == 0)
        {
            box.AddChild(new HSeparator());
            return;
        }
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Color.Color8(90, 90, 90));
        box.AddChild(label);
    }

    private static void Check(VBoxContainer box, string text, string tooltip, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, ButtonPressed = value, TooltipText = tooltip };
        c.Toggled += v =>
        {
            set(v);
            AppPreferences.Save();
        };
        box.AddChild(c);
    }

    // ------------------------------------------------------------------ shortcuts

    private void BuildShortcuts(VBoxContainer box)
    {
        _filter = new LineEdit { PlaceholderText = "Filter" };
        box.AddChild(_filter);
        _shortcutTree = new Tree { Columns = 2, HideRoot = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ColumnTitlesVisible = true };
        _shortcutTree.SetColumnTitle(0, "Function");
        _shortcutTree.SetColumnTitle(1, "Shortcut");
        _shortcutTree.SetColumnExpand(1, false);
        _shortcutTree.SetColumnCustomMinimumWidth(1, 150);
        box.AddChild(_shortcutTree);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Assigned:" });
        _assigned = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(_assigned);
        var add = new Button { Text = "Add" };
        var remove = new Button { Text = "Remove" };
        var resetAll = new Button { Text = "Reset All", TooltipText = "SketchUp's default shortcuts again." };
        row.AddChild(add);
        row.AddChild(remove);
        row.AddChild(resetAll);
        box.AddChild(row);

        _filter.TextChanged += FillShortcuts;
        _shortcutTree.ItemSelected += ShowAssigned;
        add.Pressed += () =>
        {
            if (Selected() == null)
                return;
            _capturing = true;
            _assigned.Text = "Type the keys…";
        };
        remove.Pressed += () =>
        {
            if (Selected() is { } cmd)
            {
                _commands.SetShortcut(cmd.Id, Key.None);
                ShortcutsSaved();
            }
        };
        resetAll.Pressed += () =>
        {
            _commands.ResetShortcuts();
            _menusChanged();
            FillShortcuts(_filter.Text);
        };
        FillShortcuts("");
    }

    private void FillShortcuts(string filter)
    {
        _shortcutTree.Clear();
        var root = _shortcutTree.CreateItem();
        _shown = _commands.All.Where(c => c.IsImplemented && c.MenuPath.Length > 0)
            .Where(c => filter.Length == 0 || c.MenuPath.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.MenuPath, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var i = 0; i < _shown.Count; i++)
        {
            var item = _shortcutTree.CreateItem(root);
            item.SetText(0, _shown[i].MenuPath);
            item.SetText(1, AllKeys(_shown[i]));
            item.SetMetadata(0, i);
        }
        _assigned.Text = "";
    }

    /// <summary>The command's shortcut and any secondary ones (SketchUp lists them all).</summary>
    private string AllKeys(Command c) => string.Join(", ",
        new[] { c.Shortcut }.Concat(_commands.Aliases.Where(a => a.Id == c.Id).Select(a => a.Keys))
            .Where(k => k != Key.None).Select(CommandRegistry.ShortcutText));

    private Command? Selected() => _shortcutTree.GetSelected() is { } item && (int)item.GetMetadata(0) is var i && i < _shown.Count ? _shown[i] : null;

    private void ShowAssigned() => _assigned.Text = Selected() is { } c ? AllKeys(c) : "";

    private void ShortcutsSaved()
    {
        _commands.SaveUserShortcuts();
        _menusChanged();
        if (_shortcutTree.GetSelected() is { } item && Selected() is { } c)
            item.SetText(1, AllKeys(c));
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
        ShortcutsSaved();
        // Another command may have lost these keys: refresh the column.
        FillShortcuts(_filter.Text);
    }
}

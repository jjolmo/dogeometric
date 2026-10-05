using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Components panel, In Model: the model's component definitions with their instance counts. Clicking
/// one places a copy with the cursor; the panel also selects a definition's instances and purges unused ones. The
/// chosen one's Edit tab (name, description, gluing and facing) and Statistics (what it is made of) follow.
/// Local collections (folders of models) are browsed and searched here too; clicking a model places it.
/// </summary>
public partial class ComponentsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action<ComponentDefinition> _place = null!;
    private Action<string> _placeModel = null!;
    private ComponentDefinition? _current;
    private string? _folder;
    private string _search = "";
    private readonly Dictionary<string, Texture2D?> _thumbnails = [];

    public static ComponentsPanel Create(Func<Document> doc, Action<ComponentDefinition> place, Action<string> placeModel) =>
        new() { _doc = doc, _place = place, _placeModel = placeModel };

    /// <summary>Save As was pressed for this definition (the main window asks where).</summary>
    public event Action<ComponentDefinition>? SaveAsRequested;

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var doc = _doc();
        AddChild(CollectionPicker());
        var collection = AppPreferences.Current.ComponentCollection;
        if (collection.Length > 0)
        {
            ShowCollection(collection);
            return;
        }
        var definitions = doc.Model.Definitions.Where(d => !d.IsGroup && !d.IsImage).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_current != null && !definitions.Contains(_current))
            _current = null;

        if (definitions.Count == 0)
            AddChild(new Label { Text = "No components", Modulate = new Color(1, 1, 1, 0.6f) });
        // One pass for every count: big models have hundreds of definitions.
        var counts = doc.Model.AllEntities.SelectMany(e => e.Instances).GroupBy(i => i.Definition).ToDictionary(g => g.Key, g => g.Count());
        foreach (var def in definitions)
        {
            var count = counts.GetValueOrDefault(def);
            var button = new Button
            {
                Text = $"{def.Name}  ({count})",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                ToggleMode = true,
                ButtonPressed = def == _current,
                TooltipText = def.Description.Length > 0 ? def.Description : "Click to place a copy",
            };
            button.Pressed += () =>
            {
                _current = def;
                _place(def);
                Refresh();
            };
            AddChild(button);
        }

        var actions = new HBoxContainer();
        var select = new Button { Text = "Select Instances", FocusMode = FocusModeEnum.None, Disabled = _current == null };
        select.Pressed += () =>
        {
            if (_current == null)
                return;
            // Only instances in the context being edited can be selected, as in SketchUp.
            doc.Selection.Set(doc.Context.Entities.Instances.Where(i => i.Definition == _current).Cast<object>().ToList());
        };
        actions.AddChild(select);
        var purge = new Button { Text = "Purge Unused", FocusMode = FocusModeEnum.None };
        purge.Pressed += () =>
        {
            doc.Operation("Purge Unused", _ => Grouping.PurgeUnused(doc.Model));
            Refresh();
        };
        actions.AddChild(purge);
        AddChild(actions);
        var more = new HBoxContainer();
        var replace = new Button { Text = "Replace Selected", FocusMode = FocusModeEnum.None, Disabled = _current == null,
            TooltipText = "The selected components become copies of this one" };
        replace.Pressed += () =>
        {
            if (_current is not { } with)
                return;
            var picked = doc.Selection.Items.OfType<ComponentInstance>().ToList();
            doc.Operation("Replace Selected", _ => Grouping.ReplaceDefinition(picked, with));
        };
        more.AddChild(replace);
        var save = new Button { Text = "Save As...", FocusMode = FocusModeEnum.None, Disabled = _current == null,
            TooltipText = "Save this component as a model of its own" };
        save.Pressed += () =>
        {
            if (_current is { } def)
                SaveAsRequested?.Invoke(def);
        };
        more.AddChild(save);
        AddChild(more);

        if (_current is { } current)
        {
            Edit(doc, current);
            Statistics(current);
        }
    }

    /// <summary>In Model, the local collections, and Open or create a local collection.</summary>
    private OptionButton CollectionPicker()
    {
        var prefs = AppPreferences.Current;
        var picker = new OptionButton { FocusMode = FocusModeEnum.None, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picker.AddItem("In Model");
        foreach (var folder in prefs.ComponentCollections)
        {
            picker.AddItem(System.IO.Path.GetFileName(folder.TrimEnd('/', '\\')));
            picker.SetItemTooltip(picker.ItemCount - 1, folder);
        }
        picker.AddSeparator();
        picker.AddItem("Open or create a local collection...");
        var openIndex = picker.ItemCount - 1;
        picker.Selected = prefs.ComponentCollection.Length == 0 ? 0 : prefs.ComponentCollections.IndexOf(prefs.ComponentCollection) + 1;
        picker.ItemSelected += i =>
        {
            if (i == openIndex)
            {
                PickFolder();
                picker.Selected = prefs.ComponentCollection.Length == 0 ? 0 : prefs.ComponentCollections.IndexOf(prefs.ComponentCollection) + 1;
                return;
            }
            ShowFolder(i == 0 ? "" : prefs.ComponentCollections[(int)i - 1]);
        };
        return picker;
    }

    private void PickFolder()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Open or create a local collection",
            UseNativeDialog = OS.GetEnvironment("DOGEOMETRIC_NO_NATIVE_DIALOGS") == "",
            CurrentDir = AppPreferences.Current.ComponentCollections.LastOrDefault() is { } last && System.IO.Directory.Exists(last)
                ? System.IO.Path.GetDirectoryName(last) : OS.GetSystemDir(OS.SystemDir.Documents),
        };
        dialog.DirSelected += dir =>
        {
            if (!AppPreferences.Current.ComponentCollections.Contains(dir))
                AppPreferences.Current.ComponentCollections.Add(dir);
            ShowFolder(dir);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(800, 520));
    }

    private void ShowFolder(string collection)
    {
        AppPreferences.Current.ComponentCollection = collection;
        AppPreferences.Save();
        (_folder, _search) = (null, "");
        Refresh();
    }

    /// <summary>A collection's folder (or the search results under it) as thumbnails; folders open, models place.</summary>
    private void ShowCollection(string root)
    {
        if (_folder == null || !_folder.StartsWith(root))
            _folder = root;
        var search = new LineEdit { PlaceholderText = "Search this collection", Text = _search, ClearButtonEnabled = true };
        search.TextSubmitted += text =>
        {
            _search = text;
            Refresh();
        };
        AddChild(search);

        var nav = new HBoxContainer();
        var up = new Button { Text = "Up", FocusMode = FocusModeEnum.None, Disabled = _folder == root || _search.Length > 0 };
        up.Pressed += () =>
        {
            _folder = System.IO.Path.GetDirectoryName(_folder);
            Refresh();
        };
        nav.AddChild(up);
        var where = _search.Length > 0 ? $"Results for \"{_search}\"" : System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(root) ?? root, _folder);
        nav.AddChild(new Label { Text = where, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = _folder });
        var remove = new Button { Text = "Remove", FocusMode = FocusModeEnum.None, TooltipText = "Remove this collection from the list (the folder stays)" };
        remove.Pressed += () =>
        {
            AppPreferences.Current.ComponentCollections.Remove(root);
            ShowFolder("");
        };
        nav.AddChild(remove);
        AddChild(nav);

        var entries = _search.Length > 0 ? ComponentCollection.Search(root, _search) : ComponentCollection.Browse(_folder);
        if (!System.IO.Directory.Exists(root))
            AddChild(new Label { Text = "The folder is missing", Modulate = new Color(1, 1, 1, 0.6f) });
        else if (entries.Count == 0)
            AddChild(new Label { Text = _search.Length > 0 ? "No models found" : "No models in this folder", Modulate = new Color(1, 1, 1, 0.6f) });
        var list = new ItemList
        {
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(72, 72),
            FixedColumnWidth = 92,
            MaxColumns = 0,
            SameColumnWidth = true,
            MaxTextLines = 2,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 320),
        };
        foreach (var e in entries)
        {
            var i = list.AddItem(e.Name, e.IsFolder ? Icon("res://icons/open.svg") : Thumbnail(e.Path) ?? Icon("res://icons/make_component.svg"));
            list.SetItemTooltip(i, e.IsFolder ? "Open the folder" : $"{e.Path}\nClick to place it in the model");
        }
        list.ItemClicked += (index, _, button) =>
        {
            if (button != (long)MouseButton.Left)
                return;
            var e = entries[(int)index];
            if (e.IsFolder)
            {
                _folder = e.Path;
                Refresh();
            }
            else
                _placeModel(e.Path);
        };
        AddChild(list);
    }

    private static Texture2D? Icon(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    private Texture2D? Thumbnail(string path)
    {
        if (_thumbnails.TryGetValue(path, out var cached))
            return cached;
        Texture2D? texture = null;
        if (ComponentCollection.Thumbnail(path) is { } png)
        {
            var image = new Image();
            if (image.LoadPngFromBuffer(png) == Error.Ok)
                texture = ImageTexture.CreateFromImage(image);
        }
        return _thumbnails[path] = texture;
    }

    /// <summary>The Edit tab: what the definition is called and how its copies sit in the model.</summary>
    private void Edit(Document doc, ComponentDefinition def)
    {
        void Change(string name, Action change)
        {
            doc.Operation(name, _ => change());
            Refresh();
        }
        AddChild(new Label { Text = "Edit", ThemeTypeVariation = "HeaderSmall" });
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = def.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.TextSubmitted += t =>
        {
            if (t.Trim() is { Length: > 0 } n && n != def.Name)
                Change("Rename Component", () => def.Name = n);
        };
        grid.AddChild(name);
        grid.AddChild(new Label { Text = "Description" });
        var description = new LineEdit { Text = def.Description, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        description.TextSubmitted += t => Change("Component Description", () => def.Description = t);
        grid.AddChild(description);
        grid.AddChild(new Label { Text = "Glue to" });
        var glue = new OptionButton();
        foreach (var g in Enum.GetValues<GlueTo>())
            glue.AddItem(g.ToString(), (int)g);
        glue.Select((int)def.GlueTo);
        glue.ItemSelected += i => Change("Glue to", () => def.GlueTo = (GlueTo)i);
        grid.AddChild(glue);
        AddChild(grid);
        var cut = new CheckBox { Text = "Cut opening", ButtonPressed = def.CutsOpening, Disabled = def.GlueTo == GlueTo.None };
        cut.Toggled += on => Change("Cut opening", () => def.CutsOpening = on);
        AddChild(cut);
        var face = new CheckBox { Text = "Always face camera", ButtonPressed = def.AlwaysFaceCamera };
        face.Toggled += on => Change("Always face camera", () => def.AlwaysFaceCamera = on);
        AddChild(face);
        var sun = new CheckBox { Text = "Shadows face sun", ButtonPressed = def.ShadowsFaceSun, Disabled = !def.AlwaysFaceCamera };
        sun.Toggled += on => Change("Shadows face sun", () => def.ShadowsFaceSun = on);
        AddChild(sun);
    }

    /// <summary>The Statistics tab: everything inside the definition, nested components included.</summary>
    private void Statistics(ComponentDefinition def)
    {
        AddChild(new Label { Text = "Statistics", ThemeTypeVariation = "HeaderSmall" });
        int edges = 0, faces = 0, instances = 0, guides = 0;
        var materials = new HashSet<Dogeometric.Core.Modeling.Material>();
        var definitions = new HashSet<ComponentDefinition>();
        void Count(Entities e)
        {
            edges += e.Edges.Count;
            faces += e.Faces.Count;
            guides += e.GuideLines.Count + e.GuidePoints.Count;
            foreach (var f in e.Faces)
            {
                if (f.FrontMaterial is { } fm)
                    materials.Add(fm);
                if (f.BackMaterial is { } bm)
                    materials.Add(bm);
            }
            foreach (var i in e.Instances)
            {
                instances++;
                definitions.Add(i.Definition);
                Count(i.Definition.Entities);
            }
        }
        Count(def.Entities);
        var grid = new GridContainer { Columns = 2 };
        foreach (var (label, value) in new[] { ("Edges", edges), ("Faces", faces), ("Component Instances", instances),
            ("Component Definitions", definitions.Count), ("Guides", guides), ("Materials", materials.Count) })
        {
            grid.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            grid.AddChild(new Label { Text = value.ToString(), HorizontalAlignment = HorizontalAlignment.Right });
        }
        AddChild(grid);
    }
}

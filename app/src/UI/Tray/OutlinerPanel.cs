using Dogeometric.Core.Modeling;
using Godot;
using Transform = Dogeometric.Core.Geometry.Transform;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Outliner: the model's groups and components as a tree (section planes too). Selecting an entry opens
/// the group that holds it and selects it; double-clicking opens it for editing; the eye hides and shows, the lock
/// unlocks. Right-click renames, locks or hides; dragging an entry onto another moves it inside. The details menu
/// expands or collapses everything and sorts by name. A filter keeps the entries whose name matches.
/// </summary>
public partial class OutlinerPanel : VBoxContainer
{
    private const int EyeButton = 0, LockButton = 1;

    private Func<Document> _doc = null!;
    private OutlinerTree _tree = null!;
    private LineEdit _filter = null!;
    private Action _visibilityChanged = null!;
    private bool _syncing;
    private bool _sortByName = true;
    private readonly HashSet<ComponentInstance> _expanded = [];
    private ComponentInstance? _renaming;

    // Each tree item knows its entity and the instance path that leads to its parent.
    private readonly Dictionary<TreeItem, (object Item, ComponentInstance[] Path)> _items = [];

    public static OutlinerPanel Create(Func<Document> doc, Action visibilityChanged)
    {
        var panel = new OutlinerPanel { _doc = doc, _visibilityChanged = visibilityChanged };
        var bar = new HBoxContainer();
        panel._filter = new LineEdit { PlaceholderText = "Filter", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel._filter.TextChanged += _ => panel.Refresh();
        bar.AddChild(panel._filter);
        var details = new MenuButton { Text = "≡", TooltipText = "Details", Flat = false };
        var menu = details.GetPopup();
        menu.AddItem("Expand All", 0);
        menu.AddItem("Collapse All", 1);
        menu.AddCheckItem("Sort by Name", 2);
        menu.SetItemChecked(2, true);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0:
                    panel.ExpandAll(true);
                    break;
                case 1:
                    panel.ExpandAll(false);
                    break;
                case 2:
                    panel._sortByName = !panel._sortByName;
                    menu.SetItemChecked(2, panel._sortByName);
                    panel.Refresh();
                    break;
            }
        };
        bar.AddChild(details);
        panel.AddChild(bar);
        panel._tree = new OutlinerTree
        {
            Panel = panel,
            Columns = 2,
            HideRoot = false,
            CustomMinimumSize = new Vector2(0, 220),
            SelectMode = Tree.SelectModeEnum.Multi,
            AllowRmbSelect = true,
        };
        panel._tree.SetColumnExpand(1, false);
        panel._tree.SetColumnCustomMinimumWidth(1, 52);
        panel._tree.MultiSelected += (item, _, selected) => panel.OnSelected(item, selected);
        panel._tree.ItemActivated += panel.OnActivated;
        panel._tree.ButtonClicked += (item, _, id, _) => panel.OnButton(item, (int)id);
        panel._tree.ItemCollapsed += panel.OnCollapsed;
        panel._tree.ItemMouseSelected += (at, button) =>
        {
            if (button == (long)MouseButton.Right)
                panel.ShowMenu(panel._tree.GetGlobalTransform() * at);
        };
        panel._tree.ItemEdited += panel.OnEdited;
        panel.AddChild(panel._tree);
        return panel;
    }

    public void Refresh()
    {
        var doc = _doc();
        _tree.Clear();
        _items.Clear();
        var root = _tree.CreateItem();
        root.SetText(0, "Model");
        var filter = _filter.Text.Trim();
        Add(root, doc.Model.Entities, [], filter);
        SyncSelection();
    }

    /// <summary>Adds the instances and section planes of <paramref name="e"/>; returns whether any matched.</summary>
    private bool Add(TreeItem parent, Entities e, ComponentInstance[] path, string filter)
    {
        var any = false;
        var instances = _sortByName ? e.Instances.OrderBy(Label, StringComparer.CurrentCultureIgnoreCase) : e.Instances.AsEnumerable();
        foreach (var inst in instances)
        {
            var item = _tree.CreateItem(parent);
            item.SetText(0, Label(inst));
            if (inst.Hidden)
                item.SetCustomColor(0, new Color(0.55f, 0.55f, 0.55f));
            item.AddButton(1, EyeIcon(!inst.Hidden), EyeButton, tooltipText: inst.Hidden ? "Show" : "Hide");
            if (inst.Locked && Icon("res://icons/lock.svg") is { } lockIcon)
                item.AddButton(1, lockIcon, LockButton, tooltipText: "Unlock");
            item.Collapsed = !_expanded.Contains(inst);
            _items[item] = (inst, path);
            var childMatched = Add(item, inst.Definition.Entities, [.. path, inst], filter);
            var matches = filter.Length == 0 || Label(inst).Contains(filter, StringComparison.CurrentCultureIgnoreCase);
            if (!matches && !childMatched)
            {
                _items.Remove(item);
                parent.RemoveChild(item);
                item.Free();
                continue;
            }
            if (childMatched && filter.Length > 0)
                item.Collapsed = false;
            any = true;
        }
        foreach (var s in e.SectionPlanes)
        {
            var label = s.Name.Length > 0 ? s.Name : "Section Plane";
            if (filter.Length > 0 && !label.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                continue;
            var item = _tree.CreateItem(parent);
            item.SetText(0, label + (s == e.ActiveSection ? "  (active)" : ""));
            _items[item] = (s, path);
            any = true;
        }
        return any;
    }

    /// <summary>SketchUp's Outliner naming: groups as "Group" (or their name), components as "name &lt;Definition&gt;".</summary>
    private static string Label(ComponentInstance inst)
    {
        if (inst.IsGroup)
            return inst.Name.Length > 0 ? inst.Name : "Group";
        var def = $"<{inst.Definition.Name}>";
        return inst.Name.Length > 0 ? $"{inst.Name} {def}" : def;
    }

    private static Texture2D? Icon(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    private static Texture2D? EyeIcon(bool visible) => Icon(visible ? "res://icons/eye.svg" : "res://icons/eye_closed.svg");

    private void ExpandAll(bool expand)
    {
        void Walk(Entities e)
        {
            foreach (var inst in e.Instances)
            {
                if (expand)
                    _expanded.Add(inst);
                else
                    _expanded.Remove(inst);
                Walk(inst.Definition.Entities);
            }
        }
        Walk(_doc().Model.Entities);
        Refresh();
    }

    private void OnCollapsed(TreeItem item)
    {
        if (_items.TryGetValue(item, out var entry) && entry.Item is ComponentInstance inst)
        {
            if (item.Collapsed)
                _expanded.Remove(inst);
            else
                _expanded.Add(inst);
        }
    }

    private void OnSelected(TreeItem item, bool selected)
    {
        if (_syncing || !selected || !_items.TryGetValue(item, out var entry))
            return;
        var doc = _doc();
        // Open the group that holds the entry, then select it.
        if (!doc.Context.Path.SequenceEqual(entry.Path))
        {
            doc.Context.Reset();
            foreach (var inst in entry.Path)
                doc.Context.Enter(inst);
        }
        var chosen = _tree.GetSelected() is null ? [] : SelectedEntries().Where(x => x.Path.SequenceEqual(entry.Path)).Select(x => x.Item).ToList();
        doc.Selection.Set(chosen.Count > 0 ? chosen : [entry.Item]);
    }

    private IEnumerable<(object Item, ComponentInstance[] Path)> SelectedEntries()
    {
        for (var item = _tree.GetNextSelected(null); item != null; item = _tree.GetNextSelected(item))
            if (_items.TryGetValue(item, out var entry))
                yield return entry;
    }

    private void OnActivated()
    {
        if (_tree.GetSelected() is not { } item || !_items.TryGetValue(item, out var entry) || entry.Item is not ComponentInstance inst)
            return;
        var doc = _doc();
        doc.Selection.Clear();
        doc.Edit(inst);
    }

    private void OnButton(TreeItem item, int id)
    {
        if (!_items.TryGetValue(item, out var entry) || entry.Item is not ComponentInstance inst)
            return;
        if (id == LockButton)
            Change(entry, "Unlock", () => inst.Locked = false);
        else
            Change(entry, inst.Hidden ? "Unhide" : "Hide", () => inst.Hidden = !inst.Hidden);
    }

    /// <summary>One undoable change to an entry, in the collection that holds it.</summary>
    private void Change((object Item, ComponentInstance[] Path) entry, string name, Action change)
    {
        var doc = _doc();
        doc.Undo.Begin(name, Owner(doc, entry.Path));
        change();
        doc.Undo.Commit();
        _visibilityChanged();
        Refresh();
    }

    private void ShowMenu(Vector2 at)
    {
        if (_tree.GetSelected() is not { } item || !_items.TryGetValue(item, out var entry) || entry.Item is not ComponentInstance inst)
            return;
        var menu = new PopupMenu();
        menu.AddItem("Rename", 0);
        menu.AddItem(inst.Locked ? "Unlock" : "Lock", 1);
        menu.AddItem(inst.Hidden ? "Unhide" : "Hide", 2);
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0:
                    StartRename(item, inst);
                    break;
                case 1:
                    Change(entry, inst.Locked ? "Unlock" : "Lock", () => inst.Locked = !inst.Locked);
                    break;
                case 2:
                    Change(entry, inst.Hidden ? "Unhide" : "Hide", () => inst.Hidden = !inst.Hidden);
                    break;
            }
        };
        menu.PopupHide += menu.QueueFree;
        AddChild(menu);
        menu.Position = (Vector2I)at;
        menu.Popup();
    }

    /// <summary>Rename in place: the entry shows just the instance's name until the edit ends.</summary>
    private void StartRename(TreeItem item, ComponentInstance inst)
    {
        _renaming = inst;
        item.SetText(0, inst.Name);
        item.SetEditable(0, true);
        item.Select(0);
        _tree.EditSelected(true);
    }

    private void OnEdited()
    {
        if (_renaming is not { } inst || _tree.GetEdited() is not { } item || !_items.TryGetValue(item, out var entry))
            return;
        _renaming = null;
        var name = item.GetText(0).Trim();
        if (name == inst.Name)
        {
            Refresh();
            return;
        }
        Change(entry, "Rename", () => inst.Name = name);
    }

    private static Entities Owner(Document doc, ComponentInstance[] path) =>
        path.Length == 0 ? doc.Model.Entities : path[^1].Definition.Entities;

    private static Transform World(ComponentInstance[] path) =>
        path.Aggregate(Transform.Identity, (acc, i) => i.Transform.Then(acc));

    /// <summary>Outliner drag and drop: the dragged entry moves into the group (or the model) it is dropped on.</summary>
    internal bool CanMove(TreeItem dragged, TreeItem? target) =>
        _items.TryGetValue(dragged, out var entry) && entry.Item is ComponentInstance && target != null
        && (target == _tree.GetRoot() || _items.TryGetValue(target, out var t) && t.Item is ComponentInstance);

    internal void Move(TreeItem dragged, TreeItem target)
    {
        if (!_items.TryGetValue(dragged, out var entry) || entry.Item is not ComponentInstance inst)
            return;
        var doc = _doc();
        var (to, toPath) = target == _tree.GetRoot() ? (doc.Model.Entities, Array.Empty<ComponentInstance>())
            : _items[target] is { Item: ComponentInstance into } t ? (into.Definition.Entities, [.. t.Path, into]) : (null!, null!);
        var from = Owner(doc, entry.Path);
        if (to == null || to == from)
            return;
        doc.Context.Reset();
        doc.Selection.Clear();
        doc.Undo.Begin("Move to Group", from, to);
        var moved = Grouping.MoveInto(inst, from, World(entry.Path), to, World(toPath));
        doc.Undo.Commit();
        if (moved)
        {
            _expanded.UnionWith(toPath);
            _visibilityChanged();
        }
        Refresh();
    }

    /// <summary>Highlights the entries of the current selection.</summary>
    public void SyncSelection()
    {
        var doc = _doc();
        _syncing = true;
        _tree.DeselectAll();
        foreach (var (item, entry) in _items)
            if (doc.Selection.Contains(entry.Item) && entry.Path.SequenceEqual(doc.Context.Path))
                item.Select(0);
        _syncing = false;
    }
}

/// <summary>The Outliner's tree, which lets its entries be dragged onto others.</summary>
public partial class OutlinerTree : Tree
{
    public OutlinerPanel Panel { get; set; } = null!;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (GetItemAtPosition(atPosition) is not { } item)
            return default;
        SetDragPreview(new Label { Text = item.GetText(0) });
        DropModeFlags = (int)DropModeFlagsEnum.OnItem;
        return item;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.Obj is TreeItem dragged && Panel.CanMove(dragged, GetItemAtPosition(atPosition)) && GetItemAtPosition(atPosition) != dragged;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        DropModeFlags = (int)DropModeFlagsEnum.Disabled;
        if (data.Obj is TreeItem dragged && GetItemAtPosition(atPosition) is { } target && target != dragged)
            Panel.Move(dragged, target);
    }
}

using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Outliner: the model's groups and components as a tree (section planes too). Selecting an entry opens
/// the group that holds it and selects it; double-clicking opens it for editing; the eye column hides and shows.
/// A filter keeps the entries whose name matches.
/// </summary>
public partial class OutlinerPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Tree _tree = null!;
    private LineEdit _filter = null!;
    private Action _visibilityChanged = null!;
    private bool _syncing;

    // Each tree item knows its entity and the instance path that leads to its parent.
    private readonly Dictionary<TreeItem, (object Item, ComponentInstance[] Path)> _items = [];

    public static OutlinerPanel Create(Func<Document> doc, Action visibilityChanged)
    {
        var panel = new OutlinerPanel { _doc = doc, _visibilityChanged = visibilityChanged };
        panel._filter = new LineEdit { PlaceholderText = "Filter", ClearButtonEnabled = true };
        panel._filter.TextChanged += _ => panel.Refresh();
        panel.AddChild(panel._filter);
        panel._tree = new Tree
        {
            Columns = 2,
            HideRoot = false,
            CustomMinimumSize = new Vector2(0, 220),
            SelectMode = Tree.SelectModeEnum.Multi,
        };
        panel._tree.SetColumnExpand(1, false);
        panel._tree.SetColumnCustomMinimumWidth(1, 28);
        panel._tree.MultiSelected += (item, _, selected) => panel.OnSelected(item, selected);
        panel._tree.ItemActivated += panel.OnActivated;
        panel._tree.ButtonClicked += (item, _, _, _) => panel.OnEye(item);
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
        foreach (var inst in e.Instances.OrderBy(Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = _tree.CreateItem(parent);
            item.SetText(0, Label(inst));
            if (inst.Hidden)
                item.SetCustomColor(0, new Color(0.55f, 0.55f, 0.55f));
            item.AddButton(1, EyeIcon(!inst.Hidden), tooltipText: inst.Hidden ? "Show" : "Hide");
            item.Collapsed = true;
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

    private static Texture2D? EyeIcon(bool visible)
    {
        var path = visible ? "res://icons/eye.svg" : "res://icons/eye_closed.svg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
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

    private void OnEye(TreeItem item)
    {
        if (!_items.TryGetValue(item, out var entry) || entry.Item is not ComponentInstance inst)
            return;
        var doc = _doc();
        doc.Undo.Begin(inst.Hidden ? "Unhide" : "Hide", Owner(doc, entry.Path));
        inst.Hidden = !inst.Hidden;
        doc.Undo.Commit();
        _visibilityChanged();
    }

    private static Entities Owner(Document doc, ComponentInstance[] path) =>
        path.Length == 0 ? doc.Model.Entities : path[^1].Definition.Entities;

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

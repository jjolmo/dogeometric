using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Components panel, In Model: the model's component definitions with their instance counts. Clicking
/// one places a copy with the cursor; the panel also selects a definition's instances and purges unused ones.
/// </summary>
public partial class ComponentsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action<ComponentDefinition> _place = null!;
    private ComponentDefinition? _current;

    public static ComponentsPanel Create(Func<Document> doc, Action<ComponentDefinition> place) => new() { _doc = doc, _place = place };

    public void Refresh()
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        var doc = _doc();
        var definitions = doc.Model.Definitions.Where(d => !d.IsGroup && !d.IsImage).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_current != null && !definitions.Contains(_current))
            _current = null;

        AddChild(new Label { Text = "In Model" });
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
    }
}

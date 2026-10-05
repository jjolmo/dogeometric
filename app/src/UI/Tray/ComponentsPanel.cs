using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI.Tray;

/// <summary>
/// SketchUp's Components panel, In Model: the model's component definitions with their instance counts. Clicking
/// one places a copy with the cursor; the panel also selects a definition's instances and purges unused ones. The
/// chosen one's Edit tab (name, description, gluing and facing) and Statistics (what it is made of) follow.
/// </summary>
public partial class ComponentsPanel : VBoxContainer
{
    private Func<Document> _doc = null!;
    private Action<ComponentDefinition> _place = null!;
    private ComponentDefinition? _current;

    public static ComponentsPanel Create(Func<Document> doc, Action<ComponentDefinition> place) => new() { _doc = doc, _place = place };

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

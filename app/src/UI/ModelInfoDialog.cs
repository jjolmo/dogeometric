using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's Model Info window: a list of panes on the left (Animation, Dimensions, File, Statistics, Text, Units)
/// and the pane on the right.
/// Changes apply at once, as in SketchUp.
/// </summary>
public partial class ModelInfoDialog : AcceptDialog
{
    private Document _doc = null!;
    private string? _path;
    private Action _changed = null!;
    private Control _pane = null!;

    public static void Show(Node parent, Document doc, string? path, Action changed)
    {
        var d = new ModelInfoDialog { Title = "Model Info", OkButtonText = "Close", _doc = doc, _path = path, _changed = changed };
        var split = new HBoxContainer { CustomMinimumSize = new Vector2(560, 340) };
        var list = new ItemList { CustomMinimumSize = new Vector2(140, 0) };
        foreach (var name in new[] { "Animation", "Dimensions", "File", "Statistics", "Text", "Units" })
            list.AddItem(name);
        split.AddChild(list);
        d._pane = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(d._pane);
        list.ItemSelected += i => d.ShowPane(list.GetItemText((int)i));
        d.AddChild(split);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        list.Select(5);
        d.ShowPane("Units");
        d.PopupCentered();
    }

    private void ShowPane(string name)
    {
        foreach (var c in _pane.GetChildren())
        {
            _pane.RemoveChild(c);
            c.QueueFree();
        }
        _pane.AddChild(new Label { Text = name });
        _pane.AddChild(new HSeparator());
        switch (name)
        {
            case "File":
                File();
                break;
            case "Statistics":
                Statistics();
                break;
            case "Animation":
                Animation();
                break;
            case "Dimensions":
                Dimensions();
                break;
            case "Text":
                Text();
                break;
            default:
                Units();
                break;
        }
    }

    private GridContainer Grid()
    {
        var grid = new GridContainer { Columns = 2 };
        _pane.AddChild(grid);
        return grid;
    }

    private void File()
    {
        var model = _doc.Model;
        var grid = Grid();
        Row(grid, "Location", _path ?? "(not saved)");
        Row(grid, "Version", model.SourceVersion.Length > 0 ? model.SourceVersion : "Dogeometric");
        if (_path != null && System.IO.File.Exists(_path))
            Row(grid, "Size", $"{new System.IO.FileInfo(_path).Length / 1024.0:0.#} KB");
    }

    private void Statistics()
    {
        var model = _doc.Model;
        var all = model.AllEntities.ToList();
        var grid = Grid();
        Row(grid, "Edges", all.Sum(e => e.Edges.Count).ToString());
        Row(grid, "Faces", all.Sum(e => e.Faces.Count).ToString());
        Row(grid, "Component Instances", all.Sum(e => e.Instances.Count(i => !i.IsGroup)).ToString());
        Row(grid, "Groups", all.Sum(e => e.Instances.Count(i => i.IsGroup)).ToString());
        Row(grid, "Guides", all.Sum(e => e.GuideLines.Count + e.GuidePoints.Count).ToString());
        Row(grid, "Dimensions", all.Sum(e => e.Dimensions.Count).ToString());
        Row(grid, "Texts", all.Sum(e => e.Texts.Count).ToString());
        Row(grid, "Section Planes", all.Sum(e => e.SectionPlanes.Count).ToString());
        Row(grid, "Component Definitions", model.Definitions.Count(d => !d.IsGroup && !d.IsImage).ToString());
        Row(grid, "Materials", model.Materials.Count.ToString());
        Row(grid, "Tags", model.Tags.Count.ToString());
        var purge = new Button { Text = "Purge Unused" };
        purge.Pressed += () =>
        {
            _doc.Operation("Purge Unused", _ => Grouping.PurgeUnused(model));
            _changed();
            ShowPane("Statistics");
        };
        _pane.AddChild(purge);
    }

    /// <summary>A model setting changed as one step, so it counts as a change and undoes.</summary>
    private void Set(string name, Action change)
    {
        _doc.Undo.Begin(name);
        change();
        _doc.Undo.Commit();
        _changed();
    }

    private SpinBox Number(GridContainer grid, string label, double value, double min, double max, double step, string suffix)
    {
        grid.AddChild(new Label { Text = label });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix };
        grid.AddChild(spin);
        return spin;
    }

    private void Animation()
    {
        var model = _doc.Model;
        var enable = new CheckBox { Text = "Enable scene transitions", ButtonPressed = model.SceneTransitions };
        enable.Toggled += on => Set("Animation", () => model.SceneTransitions = on);
        _pane.AddChild(enable);
        var grid = Grid();
        Number(grid, "Transition", model.SceneTransitionSeconds, 0, 60, 0.1, "seconds").ValueChanged += v => Set("Animation", () => model.SceneTransitionSeconds = v);
        Number(grid, "Scene delay", model.SceneDelaySeconds, 0, 60, 0.1, "seconds").ValueChanged += v => Set("Animation", () => model.SceneDelaySeconds = v);
    }

    private void Dimensions()
    {
        var model = _doc.Model;
        var grid = Grid();
        Number(grid, "Text size", model.DimensionFontSize, 6, 72, 1, "pt").ValueChanged += v => Set("Dimensions", () => model.DimensionFontSize = (int)v);
        grid.AddChild(new Label { Text = "Endpoints" });
        var ends = new OptionButton();
        foreach (var e in Enum.GetValues<DimensionEndpoint>())
            ends.AddItem(e switch { DimensionEndpoint.ClosedArrow => "Closed Arrow", DimensionEndpoint.OpenArrow => "Open Arrow", _ => e.ToString() }, (int)e);
        ends.Select((int)model.DimensionEndpoints);
        ends.ItemSelected += i => Set("Dimensions", () => model.DimensionEndpoints = (DimensionEndpoint)ends.GetItemId((int)i));
        grid.AddChild(ends);
    }

    private void Text()
    {
        var model = _doc.Model;
        var grid = Grid();
        Number(grid, "Text size", model.TextFontSize, 6, 72, 1, "pt").ValueChanged += v => Set("Text", () => model.TextFontSize = (int)v);
    }

    private void Units()
    {
        var model = _doc.Model;
        var grid = Grid();
        grid.AddChild(new Label { Text = "Format" });
        grid.AddChild(new Label { Text = "Decimal" });

        grid.AddChild(new Label { Text = "Length" });
        var unit = new OptionButton();
        foreach (var u in Enum.GetValues<LengthUnit>())
            unit.AddItem(u.ToString(), (int)u);
        unit.Select((int)model.Units);
        grid.AddChild(unit);

        grid.AddChild(new Label { Text = "Precision" });
        var precision = new OptionButton();
        for (var i = 0; i <= 6; i++)
            precision.AddItem(i == 0 ? "0" : "0." + new string('0', i), i);
        precision.Select(Math.Clamp(model.UnitPrecision, 0, 6));
        grid.AddChild(precision);

        var sample = new Label();
        void Update() => sample.Text = "Example: " + Length.Format(1234.5678, model.Units, model.UnitPrecision);
        Update();
        unit.ItemSelected += i =>
        {
            _doc.Operation("Units", _ => model.Units = (LengthUnit)unit.GetItemId((int)i));
            Update();
            _changed();
        };
        precision.ItemSelected += i =>
        {
            _doc.Operation("Units", _ => model.UnitPrecision = (int)i);
            Update();
            _changed();
        };
        _pane.AddChild(sample);
    }

    private static void Row(GridContainer grid, string label, string value)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(new Label { Text = value });
    }
}

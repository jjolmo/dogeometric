using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>SketchUp's Model Info window: its panes listed on the left, the chosen one on the right; changes apply at
/// once, as in SketchUp.</summary>
public partial class ModelInfoDialog : AcceptDialog
{
    private Document _doc = null!;
    private string? _path;
    private Action _changed = null!;
    private Control _pane = null!;
    private DocumentController? _controller;

    private static readonly string[] Panes = ["Animation", "Components", "Credits", "Dimensions", "File", "Geo-location", "Rendering", "Statistics", "Text", "Units"];

    public static void Show(Node parent, Document doc, string? path, Action changed, DocumentController? controller = null, string pane = "Units")
    {
        var d = new ModelInfoDialog { Title = "Model Info", OkButtonText = "Close", _doc = doc, _path = path, _changed = changed, _controller = controller };
        var split = new HBoxContainer { CustomMinimumSize = new Vector2(560, 340) };
        var list = new ItemList { CustomMinimumSize = new Vector2(140, 0) };
        foreach (var name in Panes)
            list.AddItem(name);
        split.AddChild(list);
        d._pane = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(d._pane);
        list.ItemSelected += i => d.ShowPane(list.GetItemText((int)i));
        d.AddChild(split);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        list.Select(Array.IndexOf(Panes, pane));
        d.ShowPane(pane);
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
            case "Components":
                Components();
                break;
            case "Credits":
                Credits();
                break;
            case "Geo-location":
                GeoLocation();
                break;
            case "Rendering":
                Rendering();
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
        void Text(string label, string value, Func<ModelOptions, string, ModelOptions> set)
        {
            grid.AddChild(new Label { Text = label });
            var edit = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(260, 0) };
            edit.FocusExited += () =>
            {
                if (edit.Text != value)
                {
                    value = edit.Text;
                    Options("Model Info", m => set(m, edit.Text));
                }
            };
            edit.TextSubmitted += _ => edit.ReleaseFocus();
            grid.AddChild(edit);
        }
        Text("Name", model.Options.Name, (m, v) => m with { Name = v });
        Text("Description", model.Options.Description, (m, v) => m with { Description = v });
        grid.AddChild(new Label { Text = "Glue to" });
        var glue = new OptionButton();
        foreach (var g in Enum.GetValues<GlueTo>())
            glue.AddItem(g.ToString(), (int)g);
        glue.Select((int)model.Options.GlueTo);
        glue.ItemSelected += i => Options("Model Info", m => m with { GlueTo = (GlueTo)glue.GetItemId((int)i) });
        grid.AddChild(glue);
        void Flag(string label, bool value, Func<ModelOptions, bool, ModelOptions> set)
        {
            grid.AddChild(new Control());
            var check = new CheckBox { Text = label, ButtonPressed = value };
            check.Toggled += on => Options("Model Info", m => set(m, on));
            grid.AddChild(check);
        }
        Flag("Cut opening", model.Options.CutsOpening, (m, v) => m with { CutsOpening = v });
        Flag("Always face camera", model.Options.AlwaysFaceCamera, (m, v) => m with { AlwaysFaceCamera = v });
        Flag("Shadows face sun", model.Options.ShadowsFaceSun, (m, v) => m with { ShadowsFaceSun = v });
        Row(grid, "Version", model.SourceVersion.Length > 0 ? model.SourceVersion : "Dogeometric");
        if (_path != null && System.IO.File.Exists(_path))
            Row(grid, "Size", $"{new System.IO.FileInfo(_path).Length / 1024.0:0.#} KB");
    }

    private static bool _nested = true;

    private void Statistics()
    {
        var model = _doc.Model;
        // Show nested components off counts only the model's top level, as SketchUp's Statistics does.
        var all = _nested ? model.AllEntities.ToList() : [model.Entities];
        var nested = new CheckBox { Text = "Show nested components", ButtonPressed = _nested };
        nested.Toggled += on =>
        {
            _nested = on;
            ShowPane("Statistics");
        };
        _pane.AddChild(nested);
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
        var fix = new Button { Text = "Fix Problems", TooltipText = "Check the model and repair what it can (edges split along a line, stray vertices)." };
        fix.Pressed += () =>
        {
            var report = "";
            _doc.Operation("Fix Problems", _ =>
            {
                var options = new CleanUpOptions { Purge = false, MergeFaces = false, RepairSplitEdges = true, EraseStrayEdges = false };
                var result = CleanUp.Run(model, model.Entities, [], options);
                report = string.Join("\n", result.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}: {kv.Value}"));
            });
            _changed();
            var d = new AcceptDialog { Title = "Validity Check", DialogText = report.Length > 0 ? report : "No problems found." };
            d.Confirmed += d.QueueFree;
            AddChild(d);
            d.PopupCentered();
            ShowPane("Statistics");
        };
        _pane.AddChild(fix);
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
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, CustomMinimumSize = new Vector2(130, 0) };
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
        var row = new HBoxContainer();
        Button Action(string text, Action run)
        {
            var b = new Button { Text = text };
            b.Pressed += run;
            row.AddChild(b);
            return b;
        }
        var texts = _doc.Context.Entities.Texts;
        Action("Select all screen text", () => _doc.Selection.Set(texts.Where(t => t.ScreenPosition != null)));
        Action("Select all leader text", () => _doc.Selection.Set(texts.Where(t => t.ScreenPosition == null)));
        _pane.AddChild(row);
    }

    private void Options(string name, Func<ModelOptions, ModelOptions> change) => Set(name, () => _doc.Model.Options = change(_doc.Model.Options));

    private void Components()
    {
        var o = _doc.Model.Options;
        _pane.AddChild(new Label { Text = "Component/Group Editing" });
        void Fade(string label, double value, Func<ModelOptions, double, ModelOptions> set, bool hidden, Action<bool>? hide)
        {
            _pane.AddChild(new Label { Text = label });
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = "Light" });
            var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            slider.DragEnded += changed =>
            {
                if (changed)
                    Options("Component Edit", m => set(m, slider.Value));
            };
            row.AddChild(slider);
            row.AddChild(new Label { Text = "Dark" });
            if (hide != null)
            {
                var check = new CheckBox { Text = "Hide", ButtonPressed = hidden };
                check.Toggled += on =>
                {
                    hide(on);
                    _changed();
                };
                row.AddChild(check);
            }
            _pane.AddChild(row);
        }
        Fade("Fade similar components:", o.FadeSimilar, (m, v) => m with { FadeSimilar = v },
            _controller?.HideSimilarComponents ?? false, _controller == null ? null : on => _controller.HideSimilarComponents = on);
        Fade("Fade rest of model:", o.FadeRest, (m, v) => m with { FadeRest = v },
            _controller?.HideRestOfModel ?? false, _controller == null ? null : on => _controller.HideRestOfModel = on);
        _pane.AddChild(new HSeparator());
        _pane.AddChild(new Label { Text = "Component Axes" });
        var axes = new CheckBox { Text = "Show component axes", ButtonPressed = o.ShowComponentAxes };
        axes.Toggled += on => Options("Component Axes", m => m with { ShowComponentAxes = on });
        _pane.AddChild(axes);
    }

    private void Credits()
    {
        var o = _doc.Model.Options;
        var grid = Grid();
        grid.AddChild(new Label { Text = "Model Author" });
        var author = new LineEdit { Text = o.Author, PlaceholderText = "(none)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        author.TextSubmitted += t => Options("Credits", m => m with { Author = t.Trim() });
        author.FocusExited += () =>
        {
            if (author.Text.Trim() != _doc.Model.Options.Author)
                Options("Credits", m => m with { Author = author.Text.Trim() });
        };
        grid.AddChild(author);
        var claim = new Button { Text = "Claim Credit", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        claim.Pressed += () =>
        {
            var name = System.Environment.UserName;
            author.Text = name;
            Options("Credits", m => m with { Author = name });
        };
        _pane.AddChild(claim);
        var authors = _doc.Model.Definitions.Where(d => !d.IsGroup && !d.IsImage).Select(d => d.Name).Order().ToList();
        _pane.AddChild(new Label { Text = authors.Count == 0 ? "No components." : $"Components: {authors.Count}" });
    }

    private void GeoLocation()
    {
        var s = _doc.Model.Shadows;
        _pane.AddChild(new Label { Text = "Manual location (it places the sun for shadows)." });
        var grid = Grid();
        Number(grid, "Latitude", s.Latitude, -90, 90, 0.001, "°").ValueChanged += v => SetShadows(x => x with { Latitude = v });
        Number(grid, "Longitude", s.Longitude, -180, 180, 0.001, "°").ValueChanged += v => SetShadows(x => x with { Longitude = v });
        var clear = new Button { Text = "Clear Location", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        clear.Pressed += () =>
        {
            var defaults = new ShadowSettings();
            SetShadows(x => x with { Latitude = defaults.Latitude, Longitude = defaults.Longitude });
            ShowPane("Geo-location");
        };
        _pane.AddChild(clear);
    }

    private void SetShadows(Func<ShadowSettings, ShadowSettings> change)
    {
        Set("Geo-location", () => _doc.Model.Shadows = change(_doc.Model.Shadows));
        _controller?.ApplyShadows();
    }

    private void Rendering()
    {
        var smooth = new CheckBox { Text = "Use anti-aliased textures", ButtonPressed = _doc.Model.Options.SmoothTextures };
        smooth.Toggled += on => Options("Rendering", m => m with { SmoothTextures = on });
        _pane.AddChild(smooth);
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

        var o = model.Options;
        var snapRow = new HBoxContainer();
        var lengthSnap = new CheckBox { Text = "Enable length snapping", ButtonPressed = o.LengthSnapping };
        var lengthStep = new SpinBox { MinValue = 0.01, MaxValue = 10000, Step = 0.01, Value = o.LengthSnap, Suffix = "mm", Editable = o.LengthSnapping, CustomMinimumSize = new Vector2(130, 0) };
        lengthSnap.Toggled += on =>
        {
            lengthStep.Editable = on;
            Options("Units", m => m with { LengthSnapping = on });
        };
        lengthStep.ValueChanged += v => Options("Units", m => m with { LengthSnap = v });
        snapRow.AddChild(lengthSnap);
        snapRow.AddChild(lengthStep);
        _pane.AddChild(snapRow);

        _pane.AddChild(new HSeparator());
        _pane.AddChild(new Label { Text = "Angle Units" });
        var angles = Grid();
        angles.AddChild(new Label { Text = "Precision" });
        var anglePrecision = new OptionButton();
        for (var i = 0; i <= 3; i++)
            anglePrecision.AddItem(i == 0 ? "0" : "0." + new string('0', i), i);
        anglePrecision.Select(Math.Clamp(o.AnglePrecision, 0, 3));
        anglePrecision.ItemSelected += i => Options("Units", m => m with { AnglePrecision = (int)i });
        angles.AddChild(anglePrecision);
        var angleSnap = new CheckBox { Text = "Enable angle snapping", ButtonPressed = o.AngleSnapping };
        var angleStep = new OptionButton { Disabled = !o.AngleSnapping };
        double[] steps = [0.1, 0.5, 1, 5, 10, 15, 30, 45, 90];
        foreach (var s in steps)
            angleStep.AddItem(s.ToString(System.Globalization.CultureInfo.InvariantCulture));
        angleStep.Select(Math.Max(0, Array.IndexOf(steps, o.AngleSnap)));
        angleSnap.Toggled += on =>
        {
            angleStep.Disabled = !on;
            Options("Units", m => m with { AngleSnapping = on });
        };
        angleStep.ItemSelected += i => Options("Units", m => m with { AngleSnap = steps[i] });
        angles.AddChild(angleSnap);
        angles.AddChild(angleStep);
    }

    private static void Row(GridContainer grid, string label, string value)
    {
        grid.AddChild(new Label { Text = label });
        // Long values (a file's path) are cut short with an ellipsis rather than widening the window.
        grid.AddChild(new Label { Text = value, TooltipText = value, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass });
    }
}

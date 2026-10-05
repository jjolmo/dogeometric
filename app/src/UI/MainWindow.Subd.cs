using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>SUbD's dialogs and toggles (Entity Info, Preferences, Getting Started, Display Edges, Quad Push/Pull), and other
/// small extension commands.</summary>
public partial class MainWindow
{
    private Window? _subdInfo;
    private Action _refreshSubdInfo = () => { };

    private void RegisterSubdExtras()
    {
        _commands.Register(ExtensionIds.SubdQuadPushPull, () => _viewport.Tools.Activate(new JointPushPullTool(JointPushPullMode.Joint, quad: true)),
            () => _viewport.Tools.Active.CommandId == ExtensionIds.SubdQuadPushPull);
        _commands.Register(ExtensionIds.SubdDisplayEdges, () => _document.ShowEdges = !_document.ShowEdges, () => _document.ShowEdges);
        _commands.Register(ExtensionIds.SubdEntityInfo, ToggleSubdInfo, () => _subdInfo != null);
        _commands.Register(ExtensionIds.SubdGettingStarted, () => Alert("Welcome to SUbD",
            "Select a group or component holding only edges and faces and press Subdivided: it shows as a smooth surface, " +
            "and its edges and faces become the control mesh. Open it to edit the control mesh; the surface follows.\n\n" +
            "Increase and Decrease Subdivisions change how smooth it is. The Crease Tool sharpens edges and vertices: " +
            "drag their labels, or double-click for infinitely sharp. Quad Push/Pull extrudes faces keeping quads.\n\n" +
            "Convert to Plain Mesh turns the surface into ordinary geometry."));
        _commands.Register(ExtensionIds.SubdPreferences, ShowSubdPreferences);
        _commands.Register(ExtensionIds.SelectEdgeLoops, () =>
        {
            var doc = _document.Document;
            doc.Selection.Set(SelectionToys.EdgeLoops(doc.Context.Entities, doc.Selection.Items.ToList()));
        });
        _commands.Register(ExtensionIds.FredoScaleMakeUnique, () =>
        {
            var doc = _document.Document;
            var shared = doc.Selection.Items.OfType<ComponentInstance>().Where(i => !i.IsGroup).ToList();
            if (shared.Count > 0)
                doc.Operation("Make Unique", _ => shared.ForEach(i => Grouping.MakeUnique(doc.Model, i)));
        });
    }

    private void Alert(string title, string text)
    {
        var d = new AcceptDialog { Title = title, DialogText = text, Theme = LightTheme.Create() };
        d.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        d.GetLabel().CustomMinimumSize = new Vector2(460, 0);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
    }

    /// <summary>The Push/Pull command, which SUbD's preference swaps for Quad Push/Pull inside subdivided groups.</summary>
    private Tool PushPullTool() => AppPreferences.Current.SubdReplacePushPull && _document.Document.Context.Entities.Subdivision > 0
        ? new JointPushPullTool(JointPushPullMode.Joint, quad: true)
        : new PushPullTool();

    private void ShowSubdPreferences()
    {
        var p = AppPreferences.Current;
        var d = new ConfirmationDialog { Title = "SUbD Preferences", OkButtonText = "Save", Theme = LightTheme.Create() };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        CheckBox Option(string header, string label, string description, bool value)
        {
            box.AddChild(new Label { Text = header, ThemeTypeVariation = "HeaderSmall" });
            var check = new CheckBox { Text = label, ButtonPressed = value };
            box.AddChild(check);
            box.AddChild(new Label { Text = description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0), Modulate = new Color(0.4f, 0.4f, 0.4f) });
            return check;
        }
        var manifolds = Option("Modelling", "Fix Manifolds", "Make SUbD check if a mesh is manifold before subdividing. Prompts to try to remove internal faces.", p.SubdFixManifolds);
        var pushPull = Option("Tools", "Replace Push/Pull Tool", "Intercept and replace SketchUp's native Push/Pull tool with SUbD's custom tool designed to preserve quads better.", p.SubdReplacePushPull);
        d.AddChild(box);
        d.Confirmed += () =>
        {
            p.SubdFixManifolds = manifolds.ButtonPressed;
            p.SubdReplacePushPull = pushPull.ButtonPressed;
            AppPreferences.Save();
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
    }

    /// <summary>SUbD's Entity Info: what is selected, and for subdivided groups their subdivisions and boundary corners,
    /// for edges and vertices of the open group their sharpness.</summary>
    private void ToggleSubdInfo()
    {
        if (_subdInfo != null)
        {
            _subdInfo.QueueFree();
            _subdInfo = null;
            _refreshSubdInfo = () => { };
            return;
        }
        var w = new Window { Title = "SUbD Entity Info", Transient = true, Unresizable = true, Theme = LightTheme.Create(), WrapControls = true };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", LightTheme.Box(Colors.White, 8, 6));
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        panel.AddChild(content);
        w.AddChild(panel);
        w.CloseRequested += ToggleSubdInfo;
        _refreshSubdInfo = () => FillSubdInfo(content, w);
        GetTree().Root.AddChild(w);
        w.Position = GetWindow().Position + new Vector2I(Math.Max(GetWindow().Size.X - 640, 0), 260);
        w.Show();
        _subdInfo = w;
        _refreshSubdInfo();
    }

    private bool _fillingSubdInfo;

    private void FillSubdInfo(VBoxContainer content, Window w)
    {
        if (_fillingSubdInfo)
            return;
        foreach (var c in content.GetChildren())
        {
            content.RemoveChild(c);
            c.QueueFree();
        }
        var doc = _document.Document;
        var sel = doc.Selection.Items.ToList();
        var groups = sel.OfType<ComponentInstance>().Select(i => i.Definition.Entities).Distinct().ToList();
        var creasable = sel.Where(x => x is Edge or Vertex).ToList();
        string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        content.AddChild(new Label
        {
            Text = sel.Count == 0 ? "Nothing Selected"
                : groups.Count == sel.Count ? Count(groups.Count, "Component/Group", "Components/Groups")
                : sel.All(x => x is Edge) ? Count(sel.Count, "Edge", "Edges")
                : sel.All(x => x is Face) ? Count(sel.Count, "Face", "Faces")
                : Count(sel.Count, "Entity", "Entities"),
        });
        void Change(string name, IEnumerable<Entities> touched, Action change)
        {
            _fillingSubdInfo = true;
            try
            {
                doc.Undo.Begin(name, touched.ToArray());
                change();
                doc.Undo.Commit();
            }
            finally
            {
                _fillingSubdInfo = false;
            }
        }
        var subdivided = groups.Where(e => e.Subdivision > 0).ToList();
        if (subdivided.Count > 0)
        {
            var grid = new GridContainer { Columns = 2 };
            grid.AddChild(new Label { Text = "Subdivisions" });
            var levels = new SpinBox { MinValue = 1, MaxValue = 5, Value = subdivided[0].Subdivision };
            levels.ValueChanged += v => Change("Edit Subdivision", subdivided, () => subdivided.ForEach(e => e.Subdivision = (int)v));
            grid.AddChild(levels);
            grid.AddChild(new Label { Text = "Boundary Corners" });
            var corners = new OptionButton();
            corners.AddItem("Smooth");
            corners.AddItem("Sharp");
            corners.Select(subdivided[0].SubdivisionSmoothCorners ? 0 : 1);
            corners.ItemSelected += i => Change("Edit Subdivision Option", subdivided, () => subdivided.ForEach(e => e.SubdivisionSmoothCorners = i == 0));
            grid.AddChild(corners);
            content.AddChild(grid);
        }
        if (creasable.Count > 0)
        {
            double Sharpness(object o) => o is Edge e ? e.Crease : ((Vertex)o).Crease;
            void Set(object o, double s)
            {
                if (o is Edge e)
                    e.Crease = s;
                else
                    ((Vertex)o).Crease = s;
            }
            var first = Sharpness(creasable[0]);
            var grid = new GridContainer { Columns = 2 };
            grid.AddChild(new Label { Text = "Sharpness" });
            var value = new SpinBox { MinValue = 0, MaxValue = 10, Step = 0.1, Value = double.IsPositiveInfinity(first) ? 10 : first, Editable = !double.IsPositiveInfinity(first) };
            value.ValueChanged += v => Change("Adjust Crease", [doc.Context.Entities], () => creasable.ForEach(o => Set(o, v)));
            grid.AddChild(value);
            var infinite = new CheckBox { Text = "Infinitely Sharp", ButtonPressed = double.IsPositiveInfinity(first) };
            infinite.Toggled += on => Change("Adjust Crease", [doc.Context.Entities], () => creasable.ForEach(o => Set(o, on ? double.PositiveInfinity : 0)));
            grid.AddChild(new Control());
            grid.AddChild(infinite);
            content.AddChild(grid);
        }
        w.ResetSize();
    }
}

using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Dogeometric.App.Viewport;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>Root of the application window: menu bar, drawing area and status bar.</summary>
public partial class MainWindow : Control
{
    private CommandRegistry _commands = null!;
    private ModelViewport _viewport = null!;
    private StatusBar _status = null!;
    private DocumentController _document = null!;
    private PanelContainer _leftTools = null!;
    private readonly List<Toolbar> _toolbars = [];

    public override void _Ready()
    {
        GetWindow().Title = "Untitled - Dogeometric";
        _commands = new CommandRegistry("res://data/sketchup_commands.json");
        Theme = LightTheme.Create();

        var layout = new VBoxContainer();
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 0);
        AddChild(layout);

        _viewport = new ModelViewport { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _status = new StatusBar();

        // Drawing area with the Large Tool Set docked on its left, as in SketchUp's default layout.
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 0);
        layout.AddChild(middle);
        _leftTools = new PanelContainer();
        _leftTools.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground));
        middle.AddChild(_leftTools);
        middle.AddChild(_viewport);
        layout.AddChild(_status);

        _document = new DocumentController(this, _viewport, _status);
        _document.Changed += () => GetWindow().Title = _document.Title;
        _document.New();

        // Commands must be registered before the menu is built: item kinds (check/radio) depend on them.
        RegisterCommands();

        var menuPanel = new PanelContainer();
        menuPanel.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.MenuBackground));
        menuPanel.AddChild(MenuBuilder.Build(_commands, text => _status.SetHint(text), UpdateToolStatus));
        layout.AddChild(menuPanel);
        layout.MoveChild(menuPanel, 0);

        var topBars = new HFlowContainer();
        topBars.AddThemeConstantOverride("h_separation", 6);
        var topPanel = new PanelContainer();
        topPanel.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground, 2, 1));
        topPanel.AddChild(topBars);
        layout.AddChild(topPanel);
        layout.MoveChild(topPanel, 1);
        _toolbars.Add(Toolbar.Create("Standard", _commands, Toolbars.Icons, Toolbars.Standard));
        _toolbars.Add(Toolbar.Create("Views", _commands, Toolbars.Icons, Toolbars.Views));
        _toolbars.Add(Toolbar.Create("Styles", _commands, Toolbars.Icons, Toolbars.Styles));
        foreach (var bar in _toolbars)
            topBars.AddChild(bar);
        var largeToolSet = Toolbar.Create("Large Tool Set", _commands, Toolbars.Icons, Toolbars.LargeToolSet, columns: 2);
        _toolbars.Add(largeToolSet);
        _leftTools.AddChild(largeToolSet);
        _viewport.CameraChanged += RefreshToolbars;

        _viewport.Tools.Changed += UpdateToolStatus;
        _viewport.VcbTextChanged += text => _status.Vcb.Text = text;
        UpdateToolStatus();
        _viewport.GrabFocus();

        // `dogeometric model.skp`: open files given on the command line (after Godot's own "--").
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => File.Exists(a)) is { } file)
            CallDeferred(MethodName.OpenFromCommandLine, file);
    }

    private void RegisterCommands()
    {
        var v = _viewport;
        _commands.Register(CommandIds.New, _document.New);
        _commands.Register(CommandIds.Open, _document.ShowOpen);
        _commands.Register(CommandIds.Save, _document.Save);
        _commands.Register(CommandIds.SaveAs, _document.ShowSaveAs);
        _commands.Register(CommandIds.SaveCopyAs, _document.ShowSaveCopyAs);
        _commands.Register(CommandIds.Import, _document.ShowImport);
        _commands.Register(CommandIds.Export3DModel, _document.ShowExport3D);
        _commands.Register(CommandIds.Exit, () => GetTree().Quit());

        Document Doc() => _document.Document;
        _commands.Register(CommandIds.Undo, () => Doc().Undo.Undo());
        _commands.Register(CommandIds.Redo, () => Doc().Undo.Redo());
        _commands.Register(CommandIds.Delete, () => Doc().EraseSelection());
        _commands.Register(CommandIds.SelectAll, () =>
        {
            var e = Doc().Context.Entities;
            Doc().Selection.Set(e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances.Where(i => !i.Hidden)));
        });
        _commands.Register(CommandIds.SelectNone, () => Doc().Selection.Clear());
        _commands.Register(CommandIds.InvertSelection, () =>
        {
            var e = Doc().Context.Entities;
            var all = e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances.Where(i => !i.Hidden));
            Doc().Selection.Set(all.Where(x => !Doc().Selection.Contains(x)).ToList());
        });
        void MakeGroup(bool asGroup)
        {
            var doc = Doc();
            if (doc.Selection.IsEmpty)
                return;
            var items = doc.Selection.Items.ToList();
            ComponentInstance? created = null;
            doc.Operation(asGroup ? "Make Group" : "Make Component", e => created = Grouping.Make(doc.Model, e, items, asGroup));
            if (created != null)
                doc.Selection.Set([created]);
        }
        _commands.Register(CommandIds.MakeGroup, () => MakeGroup(true));
        _commands.Register(CommandIds.MakeComponent, () => MakeGroup(false));
        _commands.Register(CommandIds.CloseGroup, () =>
        {
            Doc().Selection.Clear();
            Doc().Context.Exit();
        });
        _commands.Register(CommandIds.About, ShowAbout);

        _commands.Register(CommandIds.ToggleAxes, () => v.Axes.Visible = !v.Axes.Visible, () => v.Axes.Visible);

        _commands.Register(CommandIds.PreviousCamera, v.PreviousCamera);
        _commands.Register(CommandIds.NextCamera, v.NextCamera);
        _commands.Register(CommandIds.ViewTop, () => v.SetStandardView(StandardView.Top));
        _commands.Register(CommandIds.ViewBottom, () => v.SetStandardView(StandardView.Bottom));
        _commands.Register(CommandIds.ViewFront, () => v.SetStandardView(StandardView.Front));
        _commands.Register(CommandIds.ViewBack, () => v.SetStandardView(StandardView.Back));
        _commands.Register(CommandIds.ViewLeft, () => v.SetStandardView(StandardView.Left));
        _commands.Register(CommandIds.ViewRight, () => v.SetStandardView(StandardView.Right));
        _commands.Register(CommandIds.ViewIso, () => v.SetStandardView(StandardView.Iso));
        _commands.Register(CommandIds.ParallelProjection, () => v.SetPerspective(false), () => !v.Camera.Perspective, radio: true);
        _commands.Register(CommandIds.Perspective, () => v.SetPerspective(true), () => v.Camera.Perspective, radio: true);
        _commands.Register(CommandIds.ZoomExtents, v.ZoomExtents);

        RegisterTool(CommandIds.Orbit, () => new OrbitTool());
        RegisterTool(CommandIds.Pan, () => new PanTool());
        RegisterTool(CommandIds.Zoom, () => new ZoomTool());
        RegisterTool(CommandIds.Select, () => new SelectTool());
        RegisterTool(CommandIds.Line, () => new LineTool());
        RegisterTool(CommandIds.Rectangle, () => new RectangleTool());
        RegisterTool(CommandIds.PushPull, () => new PushPullTool());
        RegisterTool(CommandIds.Circle, () => new CircleTool());
        RegisterTool(CommandIds.Polygon, () => new PolygonTool());
        RegisterTool(CommandIds.Arc2Point, () => new ArcTool());
        RegisterTool(CommandIds.Move, () => new MoveTool());
        RegisterTool(CommandIds.Eraser, () => new EraserTool());
    }

    private void RegisterTool(int id, Func<Tool> create)
    {
        _commands.Register(id, () => _viewport.Tools.Activate(create()), () => _viewport.Tools.Active.CommandId == id);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // Secondary SketchUp shortcuts (e.g. Ctrl+Shift+E for Zoom Extents); primary ones are menu accelerators.
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var pressed = key.GetKeycodeWithModifiers();
        foreach (var (keys, id) in _commands.Aliases)
        {
            if (keys == pressed && _commands.Execute(id))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    private void OpenFromCommandLine(string path) => _document.Open(path);

    private void RefreshToolbars()
    {
        foreach (var bar in _toolbars)
            bar.Refresh();
    }

    private void UpdateToolStatus()
    {
        RefreshToolbars();
        _status.SetHint(_viewport.Tools.Active.StatusText);
        var label = _viewport.Tools.Active.VcbLabel;
        _status.SetVcbLabel(label.Length > 0 ? label : "Measurements", editable: label.Length > 0);
    }

    private void ShowAbout()
    {
        var dialog = new AcceptDialog { Title = "About Dogeometric", DialogText = "Dogeometric\nA SketchUp-style modeller built with Godot." };
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
    }
}

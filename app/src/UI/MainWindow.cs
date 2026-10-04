using Dogeometric.App.Commands;
using Dogeometric.App.Tools;
using Dogeometric.App.UI.Tray;
using Dogeometric.App.Viewport;
using Dogeometric.Core.Modeling;
using Dogeometric.Solids;
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
    private PanelContainer _tray = null!;
    private EntityInfoPanel _entityInfo = null!;
    private MaterialsPanel _materials = null!;
    private TagsPanel _tags = null!;
    private ComponentsPanel _components = null!;
    private OutlinerPanel _outliner = null!;
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
        _tray = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        _tray.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground));
        middle.AddChild(_tray);
        layout.AddChild(_status);

        _document = new DocumentController(this, _viewport, _status);
        _document.Changed += () => GetWindow().Title = _document.Title;
        BuildTray();
        _document.DocumentReplaced += HookDocument;
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
        _toolbars.Add(Toolbar.Create("Solid Tools", _commands, Toolbars.Icons, Toolbars.SolidTools));
        foreach (var bar in _toolbars)
            topBars.AddChild(bar);
        var largeToolSet = Toolbar.Create("Large Tool Set", _commands, Toolbars.Icons, Toolbars.LargeToolSet, columns: 2);
        _toolbars.Add(largeToolSet);
        _leftTools.AddChild(largeToolSet);
        _viewport.CameraChanged += RefreshToolbars;
        SelectTool.EditAnnotationText = EditAnnotationText;

        _viewport.Tools.Changed += UpdateToolStatus;
        _viewport.VcbTextChanged += text => _status.Vcb.Text = text;
        _viewport.ContextMenuRequested += pos => ContextMenu.Show(_viewport, pos, _document.Document, _viewport, id => _commands.Execute(id));
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
        _commands.Register(CommandIds.DisplaySectionPlanes, () =>
        {
            _viewport.ShowSectionPlanes = !_viewport.ShowSectionPlanes;
            _viewport.QueueOverlayRedraw();
        }, () => _viewport.ShowSectionPlanes);
        _commands.Register(CommandIds.DisplaySectionCuts, () =>
        {
            _viewport.ShowSectionCuts = !_viewport.ShowSectionCuts;
            _viewport.UpdateSection();
        }, () => _viewport.ShowSectionCuts);
        _commands.Register(CommandIds.ReverseSection, () =>
        {
            var planes = Doc().Selection.Items.OfType<SectionPlane>().ToList();
            if (planes.Count > 0)
                Doc().Operation("Reverse Section", _ => planes.ForEach(s => s.Normal = -s.Normal));
        });
        _commands.Register(CommandIds.ActiveSectionCut, () =>
        {
            if (Doc().Selection.Items.OfType<SectionPlane>().FirstOrDefault() is { } plane)
                Doc().Operation("Active Cut", e => e.ActiveSection = e.ActiveSection == plane ? null : plane);
        });
        _commands.Register(CommandIds.IntersectWithModel, () => Intersect.WithModel(Doc()));
        _commands.Register(CommandIds.IntersectWithSelection, () => Intersect.WithSelection(Doc()));
        _commands.Register(CommandIds.Cut, () => Clipboard.Cut(Doc()));
        _commands.Register(CommandIds.Copy, () => Clipboard.Copy(Doc()));
        _commands.Register(CommandIds.Paste, () =>
        {
            if (!Clipboard.IsEmpty)
                _viewport.Tools.Activate(new PasteTool());
        });
        _commands.Register(CommandIds.PasteInPlace, () => Clipboard.Paste(Doc(), Dogeometric.Core.Geometry.Vec3.Zero));
        _commands.Register(CommandIds.SelectAll, () =>
        {
            var e = Doc().Context.Entities;
            Doc().Selection.Set(e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances.Where(i => !i.Hidden)));
        });
        _commands.Register(CommandIds.SelectNone, () => Doc().Selection.Clear());
        _commands.Register(CommandIds.DeleteGuides, () => Doc().Operation("Delete Guides", e =>
        {
            e.GuideLines.Clear();
            e.GuidePoints.Clear();
        }));
        _commands.Register(CommandIds.ToggleGuides, () => _document.ShowGuides = !_document.ShowGuides, () => _document.ShowGuides);
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
        _commands.Register(CommandIds.MakeComponent, () =>
        {
            var doc = Doc();
            if (doc.Selection.IsEmpty)
                return;
            var items = doc.Selection.Items.ToList();
            var index = doc.Model.Definitions.Count(d => !d.IsGroup && !d.IsImage) + 1;
            MakeComponentDialog.Show(this, $"Component#{index}", r =>
            {
                ComponentInstance? created = null;
                doc.Operation("Make Component", e =>
                {
                    created = Grouping.Make(doc.Model, e, items, asGroup: false, r.Name);
                    var def = created.Definition;
                    def.Description = r.Description;
                    def.AlwaysFaceCamera = r.AlwaysFaceCamera;
                    def.ShadowsFaceSun = r.ShadowsFaceSun;
                    // Unchecked "Replace selection": the component goes to the model's library only.
                    if (!r.ReplaceSelection)
                    {
                        Grouping.Explode(e, created);
                        created = null;
                    }
                });
                doc.Selection.Set(created != null ? [created] : []);
                _components.Refresh();
            });
        });
        // View › Face Style (radio items, mirrored by the Styles toolbar).
        void Style(int id, FaceStyle style) =>
            _commands.Register(id, () => { _document.FaceStyle = style; RefreshToolbars(); }, () => _document.FaceStyle == style, radio: true);
        Style(CommandIds.StyleXRay, FaceStyle.XRay);
        Style(CommandIds.StyleWireframe, FaceStyle.Wireframe);
        Style(CommandIds.StyleHiddenLine, FaceStyle.HiddenLine);
        Style(CommandIds.StyleShaded, FaceStyle.Shaded);
        Style(CommandIds.StyleShadedTextures, FaceStyle.ShadedWithTextures);
        Style(CommandIds.StyleMonochrome, FaceStyle.Monochrome);

        // Edit › Hide / Unhide / Lock.
        List<object> lastHidden = [];
        _commands.Register(CommandIds.Hide, () =>
        {
            var items = Doc().Selection.Items.ToList();
            if (items.Count == 0)
                return;
            Doc().Operation("Hide", _ => SetHidden(items, true));
            lastHidden = items;
            Doc().Selection.Clear();
        });
        _commands.Register(CommandIds.UnhideSelected, () => Doc().Operation("Unhide", _ => SetHidden(Doc().Selection.Items, false)));
        _commands.Register(CommandIds.UnhideLast, () => Doc().Operation("Unhide", _ => SetHidden(lastHidden, false)));
        _commands.Register(CommandIds.UnhideAll, () => Doc().Operation("Unhide All", e =>
            SetHidden(e.Faces.Cast<object>().Concat(e.Edges).Concat(e.Instances).ToList(), false)));
        _commands.Register(CommandIds.Lock, () => Doc().Operation("Lock", _ =>
        {
            foreach (var i in Doc().Selection.Items.OfType<ComponentInstance>())
                i.Locked = true;
        }));
        _commands.Register(CommandIds.UnlockSelected, () => Doc().Operation("Unlock", _ =>
        {
            foreach (var i in Doc().Selection.Items.OfType<ComponentInstance>())
                i.Locked = false;
        }));
        _commands.Register(CommandIds.UnlockAll, () => Doc().Operation("Unlock", e =>
        {
            foreach (var i in e.Instances)
                i.Locked = false;
        }));

        // Tools › Solid Tools: act on the selected solid groups/components (first selected = first operand).
        SolidsNative.AddSearchDirectory(ProjectSettings.GlobalizePath($"res://native/{NativeRid()}"));
        SolidsNative.AddSearchDirectory(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "native"));
        void Solid(int id, string name, Func<Document, List<ComponentInstance>, List<ComponentInstance>> op)
        {
            _commands.Register(id, () =>
            {
                var doc = Doc();
                var solids = doc.Selection.Items.OfType<ComponentInstance>().ToList();
                if (solids.Count < 2)
                {
                    _status.SetHint($"{name}: select at least two solid groups or components first.");
                    return;
                }
                try
                {
                    List<ComponentInstance> results = [];
                    doc.Operation(name, _ => results = op(doc, solids));
                    doc.Selection.Set(results);
                    _status.SetHint($"{name} done.");
                }
                catch (Exception ex)
                {
                    _status.SetHint($"{name}: {ex.Message}");
                }
            });
        }
        Solid(CommandIds.OuterShell, "Outer Shell", (d, s) => [SolidTools.OuterShell(d.Model, d.Context.Entities, s)]);
        Solid(CommandIds.SolidUnion, "Union", (d, s) => [SolidTools.Union(d.Model, d.Context.Entities, s)]);
        Solid(CommandIds.SolidIntersect, "Intersect", (d, s) => [SolidTools.Intersect(d.Model, d.Context.Entities, s)]);
        Solid(CommandIds.SolidSubtract, "Subtract", (d, s) => SolidTools.Subtract(d.Model, d.Context.Entities, s[0], s[1]) is { } r ? [r] : []);
        Solid(CommandIds.SolidTrim, "Trim", (d, s) => SolidTools.Trim(d.Model, d.Context.Entities, s[0], s[1]) is { } r ? [r] : []);
        Solid(CommandIds.SolidSplit, "Split", (d, s) => SolidTools.Split(d.Model, d.Context.Entities, s[0], s[1]));

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
        RegisterTool(CommandIds.TapeMeasure, () => new TapeMeasureTool());
        RegisterTool(CommandIds.Rotate, () => new RotateTool());
        RegisterTool(CommandIds.Scale, () => new ScaleTool());
        RegisterTool(CommandIds.FollowMe, () => new FollowMeTool());
        RegisterTool(CommandIds.Dimension, () => new DimensionTool());
        RegisterTool(CommandIds.Text, () => new TextTool());
        RegisterTool(CommandIds.ZoomWindow, () => new ZoomWindowTool());
        RegisterTool(CommandIds.SectionPlane, () => new SectionPlaneTool());
        RegisterTool(CommandIds.Protractor, () => new ProtractorTool());
        RegisterTool(CommandIds.Axes, () => new AxesTool());
        RegisterTool(CommandIds.Freehand, () => new FreehandTool());
        RegisterTool(CommandIds.Arc, () => new CenterArcTool(false));
        RegisterTool(CommandIds.Pie, () => new PieTool());
        RegisterTool(CommandIds.Arc3Point, () => new ThreePointArcTool());
        RegisterTool(CommandIds.RotatedRectangle, () => new RotatedRectangleTool());
        RegisterTool(CommandIds.PositionCamera, () => new PositionCameraTool());
        RegisterTool(CommandIds.LookAround, () => new LookAroundTool());
        RegisterTool(CommandIds.Walk, () => new WalkTool());
        RegisterTool(CommandIds.Offset, () => new OffsetTool());
        RegisterTool(CommandIds.PaintBucket, () => new PaintBucketTool(() => _materials.CurrentMaterial, m => _materials.SetCurrent(m)));
    }

    /// <summary>SketchUp's Default Tray on the right: Entity Info, Materials, Tags.</summary>
    private void BuildTray()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tray.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);
        list.AddChild(new Label { Text = "Default Tray" });
        _entityInfo = EntityInfoPanel.Create(() => _document.Document);
        _materials = MaterialsPanel.Create(() => _document.Document);
        _tags = TagsPanel.Create(() => _document.Document, () => _document.RebuildAll());
        list.AddChild(TraySection.Create("Entity Info", _entityInfo));
        list.AddChild(TraySection.Create("Materials", _materials));
        _components = ComponentsPanel.Create(() => _document.Document, def => _viewport.Tools.Activate(new ComponentPlaceTool(def)));
        list.AddChild(TraySection.Create("Components", _components, expanded: false));
        list.AddChild(TraySection.Create("Tags", _tags, expanded: false));
        _outliner = OutlinerPanel.Create(() => _document.Document, () => _document.RebuildAll());
        list.AddChild(TraySection.Create("Outliner", _outliner, expanded: false));
    }

    /// <summary>Panels follow the current document's selection and geometry.</summary>
    private void HookDocument()
    {
        var doc = _document.Document;
        doc.Selection.Changed += _entityInfo.Refresh;
        // Deferred: the Outliner's own clicks change the model, and its tree can't be rebuilt mid-signal.
        doc.Selection.Changed += () => Callable.From(_outliner.SyncSelection).CallDeferred();
        doc.Context.Changed += () => Callable.From(_outliner.SyncSelection).CallDeferred();
        doc.GeometryChanged += _ =>
        {
            _entityInfo.Refresh();
            _tags.Refresh();
            _components.Refresh();
            Callable.From(_outliner.Refresh).CallDeferred();
        };
        _entityInfo.Refresh();
        _materials.Refresh();
        _tags.Refresh();
        _components.Refresh();
        _outliner.Refresh();
    }

    /// <summary>Double-click on a dimension or text: edit its text in place ("&lt;&gt;" keeps a dimension's length).</summary>
    private void EditAnnotationText(object item)
    {
        var doc = _document.Document;
        var current = item switch
        {
            LinearDimension d => d.Text.Length == 0 ? "<>" : d.Text,
            TextLabel t => t.Text,
            _ => null,
        };
        if (current == null)
            return;
        InlineTextEditor.Show(_viewport, _viewport.GetLocalMousePosition(), current, text => doc.Operation("Edit Text", _ =>
        {
            if (item is LinearDimension d)
                d.Text = text == "<>" ? "" : text;
            else if (item is TextLabel t)
                t.Text = text;
        }));
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

    private static string NativeRid() =>
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx" :
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64";

    private static void SetHidden(IEnumerable<object> items, bool hidden)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case Face f:
                    f.Hidden = hidden;
                    break;
                case Edge e:
                    e.Flags = hidden ? e.Flags | EdgeFlags.Hidden : e.Flags & ~EdgeFlags.Hidden;
                    break;
                case ComponentInstance i:
                    i.Hidden = hidden;
                    break;
            }
        }
    }

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

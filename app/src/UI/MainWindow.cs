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
    private Backups _backups = null!;
    private PanelContainer _leftTools = null!;
    private Container _leftDock = null!, _rightDock = null!, _bottomDock = null!;
    private Control _drawingArea = null!;
    private ToolbarDocks _docks = null!;
    private PanelContainer _tray = null!;
    private EntityInfoPanel _entityInfo = null!;
    private MaterialsPanel _materials = null!;
    private TagsPanel _tags = null!;
    private ComponentsPanel _components = null!;
    private OutlinerPanel _outliner = null!;
    private SceneTabs _scenes = null!;
    private Action _rebuildMenus = () => { };
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

        // Drawing area between the left and right toolbar docks (the Large Tool Set starts on the left, as in
        // SketchUp's default layout); the bottom dock sits under it.
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 0);
        layout.AddChild(middle);
        (_leftTools, _leftDock) = DockStrip(vertical: true);
        middle.AddChild(_leftTools);
        // Scene tabs sit above the drawing area, shown once the model has scenes.
        var drawing = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        drawing.AddThemeConstantOverride("separation", 0);
        _scenes = SceneTabs.Create(() => _document.Document, _viewport, () => _document.RebuildAll());
        var tabsBar = new PanelContainer();
        tabsBar.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground, 2, 1));
        tabsBar.AddChild(_scenes);
        _scenes.VisibilityChanged += () => tabsBar.Visible = _scenes.Visible;
        drawing.AddChild(tabsBar);
        drawing.AddChild(_viewport);
        middle.AddChild(drawing);
        var (rightStrip, rightDock) = DockStrip(vertical: true);
        middle.AddChild(rightStrip);
        _rightDock = rightDock;
        _tray = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        _tray.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground));
        middle.AddChild(_tray);
        var (bottomStrip, bottomDock) = DockStrip(vertical: false);
        layout.AddChild(bottomStrip);
        _bottomDock = bottomDock;
        _drawingArea = drawing;
        layout.AddChild(_status);

        _document = new DocumentController(this, _viewport, _status);
        _document.Changed += () => GetWindow().Title = _document.Title;
        _document.ImageImportRequested += path =>
        {
            var data = File.ReadAllBytes(path);
            if (TextureImages.Decode(data) is not { } image)
            {
                _status.SetHint($"Could not read {System.IO.Path.GetFileName(path)} as an image.");
                return;
            }
            _viewport.Tools.Activate(new TexturePlaceTool(System.IO.Path.GetFileName(path), data, image.GetWidth(), image.GetHeight(),
                UI.Tray.CreateMaterialDialog.AverageColor(image)));
        };
        BuildTray();
        _document.DocumentReplaced += HookDocument;
        _document.New();

        // Read the crashed session (if any) before this one writes its own session file.
        var crashed = AppPreferences.Current.CheckForCrashRecovery ? Backups.CrashedSession() : null;
        _backups = Backups.Create(() => _document.Document, () => _document.ModelPath);
        _document.DocumentReplaced += _backups.DocumentReplaced;
        _document.Changed += _backups.UpdateSession;
        AddChild(_backups);
        _backups.DocumentReplaced();
        if (crashed is { } lost)
            Callable.From(() => OfferRecovery(lost.Model, lost.Backup)).CallDeferred();
        // Closing the window asks to save changes first.
        GetTree().AutoAcceptQuit = false;

        // Commands must be registered before the menu is built: item kinds (check/radio) depend on them.
        RegisterCommands();
        RegisterExtensions();

        // Shortcuts the reference SketchUp install has beyond its built-in tables.
        _commands.AddDefaultShortcut("Shift+S", CommandIds.HideRestOfModel);
        _commands.LoadUserShortcuts();
        var menuPanel = new PanelContainer();
        menuPanel.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.MenuBackground));
        menuPanel.AddChild(MenuBuilder.Build(_commands, text => _status.SetHint(text), UpdateToolStatus));
        // Preferences › Shortcuts rebuilds the menus so they show the new keys.
        _rebuildMenus = () =>
        {
            foreach (var c in menuPanel.GetChildren())
            {
                menuPanel.RemoveChild(c);
                c.QueueFree();
            }
            menuPanel.AddChild(MenuBuilder.Build(_commands, text => _status.SetHint(text), UpdateToolStatus));
        };
        layout.AddChild(menuPanel);
        layout.MoveChild(menuPanel, 0);

        var (topPanel, topBars) = DockStrip(vertical: false);
        topPanel.Visible = true;
        layout.AddChild(topPanel);
        layout.MoveChild(topPanel, 1);

        // SketchUp's toolbars, docked as in its default layout; the others start hidden (View › Toolbars).
        _docks = ToolbarDocks.Create(topBars, _bottomDock, _leftDock, _rightDock, _drawingArea);
        AddChild(_docks);
        void Bar(string name, int[] ids, ToolbarDocks.Dock dock, bool visible = true, int lines = 0)
        {
            var bar = Toolbar.Create(name, _commands, Toolbars.Icons, ids, lines);
            _toolbars.Add(bar);
            _docks.Add(bar, dock, visible);
        }
        Bar("Standard", Toolbars.Standard, ToolbarDocks.Dock.Top);
        Bar("Views", Toolbars.Views, ToolbarDocks.Dock.Top);
        Bar("Styles", Toolbars.Styles, ToolbarDocks.Dock.Top);
        Bar("Solid Tools", Toolbars.SolidTools, ToolbarDocks.Dock.Top);
        Bar("Getting Started", Toolbars.GettingStarted, ToolbarDocks.Dock.Top, visible: false);
        Bar("Principal", Toolbars.Principal, ToolbarDocks.Dock.Top, visible: false);
        Bar("Drawing", Toolbars.Drawing, ToolbarDocks.Dock.Top, visible: false);
        Bar("Edit", Toolbars.Edit, ToolbarDocks.Dock.Top, visible: false);
        Bar("Construction", Toolbars.Construction, ToolbarDocks.Dock.Top, visible: false);
        Bar("Camera", Toolbars.Camera, ToolbarDocks.Dock.Top, visible: false);
        Bar("Large Tool Set", Toolbars.LargeToolSet, ToolbarDocks.Dock.Left, lines: 2);
        // Extension toolbars, as each extension adds its own.
        Bar("Solid Inspector²", Toolbars.SolidInspector, ToolbarDocks.Dock.Top);
        Bar("Round Corner", Toolbars.RoundCorner, ToolbarDocks.Dock.Top);
        _docks.Load();
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

    /// <summary>The cloned SketchUp extensions: their menu items (where each extension puts them) and commands.</summary>
    private void RegisterExtensions()
    {
        _commands.AddToMenu("File", OwnIds.RecoverBackup, "Recover Backup...",
            "Open one of the automatic backups as an unsaved copy.", after: "Revert");
        _commands.AddToMenu("View", OwnIds.CenterPoints, "Center Points",
            "Show the centres of groups, components, faces and the selection, and snap to them.", after: "Guides");
        _commands.Register(OwnIds.CenterPoints, () => _document.ShowCenterPoints = !_document.ShowCenterPoints, () => _document.ShowCenterPoints);
        _commands.Register(OwnIds.RecoverBackup, () =>
            RecoverBackupDialog.Show(this, (file, original) => _document.ConfirmDiscard(() => _document.OpenRecovered(file, original))));

        _commands.AddToMenu("Tools", ExtensionIds.SolidInspector, "Solid Inspector²",
            "Inspect and repair solid groups and components.");
        _commands.Register(ExtensionIds.SolidInspector, () => _viewport.Tools.Activate(new SolidInspectorTool()),
            () => _viewport.Tools.Active is SolidInspectorTool);
        foreach (var (id, label, tip, m) in new[]
        {
            (ExtensionIds.RoundCornerRound, "Round Corner", "Round corners in 3D", Dogeometric.Core.Modeling.RoundCornerMode.Round),
            (ExtensionIds.RoundCornerSharp, "Sharp Corner", "Sharp corners in 3D", Dogeometric.Core.Modeling.RoundCornerMode.Sharp),
            (ExtensionIds.RoundCornerBevel, "Bevel", "Bevel edges and corners", Dogeometric.Core.Modeling.RoundCornerMode.Bevel),
        })
        {
            _commands.AddToMenu("Tools", id, label, tip + ".", submenu: "Fredo6 Collection");
            _commands.Register(id, () => _viewport.Tools.Activate(new RoundCornerTool(m)), () => _viewport.Tools.Active.CommandId == id);
        }
        EntityInfoPanel.InspectSolid = instance =>
        {
            _document.Document.Selection.Set([instance]);
            _viewport.Tools.Activate(new SolidInspectorTool());
        };
    }

    /// <summary>File › Exit and the window's close button: "Save changes?" first, then a clean exit.</summary>
    private void Quit() => _document.ConfirmDiscard(() =>
    {
        Backups.EndSession();
        GetTree().Quit();
    });

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Quit();
    }

    /// <summary>After a crash: offer the last session's latest backup.</summary>
    private void OfferRecovery(string? model, string backup)
    {
        var name = model != null ? System.IO.Path.GetFileName(model) : "Untitled";
        var when = File.GetLastWriteTime(backup);
        var d = new ConfirmationDialog
        {
            Title = "Recover Backup",
            DialogText = $"Dogeometric did not close properly last time.\n\nA backup of \"{name}\" from {when:g} is available. Open it?\n" +
                "It opens as an unsaved copy; your file is left as it was.",
            OkButtonText = "Open Backup",
            CancelButtonText = "Not Now",
            DialogAutowrap = true,
            Size = new Vector2I(480, 0),
        };
        d.AddButton("All Backups…", false, "all");
        d.Confirmed += () =>
        {
            d.QueueFree();
            _document.OpenRecovered(backup, model);
        };
        d.CustomAction += action =>
        {
            d.QueueFree();
            if (action == "all")
                RecoverBackupDialog.Show(this, (file, original) => _document.ConfirmDiscard(() => _document.OpenRecovered(file, original)));
        };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
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
        _commands.Register(CommandIds.Exit, Quit);

        Document Doc() => _document.Document;
        _commands.Register(CommandIds.Undo, () => Doc().Undo.Undo());
        _commands.Register(CommandIds.Redo, () => Doc().Undo.Redo());
        _commands.Register(CommandIds.Delete, () => Doc().EraseSelection());
        _commands.Register(CommandIds.AddScene, () => _scenes.Add());
        _commands.Register(CommandIds.SceneTabs, () =>
        {
            _scenes.Enabled = !_scenes.Enabled;
            _scenes.Refresh();
        }, () => _scenes.Enabled);
        _commands.Register(CommandIds.UpdateScene, () => _scenes.UpdateCurrent());
        _commands.Register(CommandIds.DeleteScene, () => _scenes.DeleteCurrent());
        _commands.Register(CommandIds.NextScene, () => _scenes.Step(1));
        _commands.Register(CommandIds.PreviousScene, () => _scenes.Step(-1));
        _commands.Register(CommandIds.Toolbars, ShowToolbarsDialog);
        _commands.Register(CommandIds.Preferences, () => PreferencesDialog.Show(this, _commands, () => _rebuildMenus(), _docks.Reset));
        _commands.Register(CommandIds.ModelInfo, () => ModelInfoDialog.Show(this, Doc(), _document.Path, () =>
        {
            _viewport.QueueOverlayRedraw();
            _components.Refresh();
            _entityInfo.Refresh();
        }));
        _commands.Register(CommandIds.Text3D, () => Text3DDialog.Show(this, r =>
        {
            var doc = Doc();
            var name = r.Text.Split('\n')[0];
            var def = new ComponentDefinition { Name = name.Length > 24 ? name[..24] : name, Description = "3D Text" };
            Text3D.Build(def.Entities, r.Contours.Select(c => (IReadOnlyList<Dogeometric.Core.Geometry.Vec3>)c).ToList(), r.Filled, r.Extrude);
            doc.Operation("Place 3D Text", _ => doc.Model.Definitions.Add(def));
            _components.Refresh();
            _viewport.Tools.Activate(new ComponentPlaceTool(def));
        }));
        _commands.Register(CommandIds.HideRestOfModel, () =>
        {
            _document.HideRestOfModel = !_document.HideRestOfModel;
            _document.RefreshComponentEdit();
        }, () => _document.HideRestOfModel);
        _commands.Register(CommandIds.HideSimilarComponents, () =>
        {
            _document.HideSimilarComponents = !_document.HideSimilarComponents;
            _document.RefreshComponentEdit();
        }, () => _document.HideSimilarComponents);
        _commands.Register(CommandIds.HiddenObjects, () => _document.ShowHiddenObjects = !_document.ShowHiddenObjects, () => _document.ShowHiddenObjects);
        _commands.Register(CommandIds.FieldOfView, () => _viewport.Tools.Activate(new ZoomTool()));
        _commands.Register(CommandIds.Edges, () => _document.ShowEdges = !_document.ShowEdges, () => _document.ShowEdges);
        _commands.Register(CommandIds.Fog, () => _viewport.ShowFog = !_viewport.ShowFog, () => _viewport.ShowFog);
        _commands.Register(CommandIds.HiddenGeometry, () => _document.ShowHiddenGeometry = !_document.ShowHiddenGeometry, () => _document.ShowHiddenGeometry);
        _commands.Register(CommandIds.BackEdges, () =>
        {
            _document.ShowBackEdges = !_document.ShowBackEdges;
            RefreshToolbars();
        }, () => _document.ShowBackEdges);
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
        _commands.Register(CommandIds.IntersectWithContext, () => Intersect.WithContext(Doc()));
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
                var solids = Doc().Selection.Items.OfType<ComponentInstance>().ToList();
                // Without two solids selected the command becomes a tool: click the first, then the second.
                if (solids.Count < 2)
                    _viewport.Tools.Activate(new SolidPickTool(id, name, pair => Run(pair)));
                else
                    Run(solids);
            }, () => _viewport.Tools.Active.CommandId == id);

            void Run(List<ComponentInstance> solids)
            {
                var doc = Doc();
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
            }
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
        _scenes.Refresh();
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

    /// <summary>A toolbar dock strip: side docks hold standing toolbars side by side, top and bottom wrap lying ones.</summary>
    private static (PanelContainer Panel, Container Dock) DockStrip(bool vertical)
    {
        var panel = new PanelContainer { Visible = false };
        panel.AddThemeStyleboxOverride("panel", LightTheme.Box(LightTheme.BarBackground, 2, 1));
        Container dock = vertical ? new HBoxContainer() : new HFlowContainer();
        dock.AddThemeConstantOverride("separation", 2);
        dock.AddThemeConstantOverride("h_separation", 6);
        panel.AddChild(dock);
        return (panel, dock);
    }

    /// <summary>View › Toolbars: SketchUp's list of toolbars to show, with Reset.</summary>
    private void ShowToolbarsDialog()
    {
        var dialog = new AcceptDialog { Title = "Toolbars", OkButtonText = "Close" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        foreach (var bar in _docks.Toolbars.OrderBy(b => b.Title))
        {
            var check = new CheckBox { Text = bar.Title, ButtonPressed = _docks.IsVisible(bar) };
            check.Toggled += on => _docks.SetVisible(bar, on);
            box.AddChild(check);
        }
        var reset = new Button { Text = "Reset" };
        reset.Pressed += () =>
        {
            _docks.Reset();
            dialog.QueueFree();
        };
        box.AddChild(reset);
        dialog.AddChild(box);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
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

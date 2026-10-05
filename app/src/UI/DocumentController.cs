using Dogeometric.App.Viewport;
using Dogeometric.Core.IO;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Formats.Skp;
using Godot;
using Model = Dogeometric.Core.Modeling.Model;

namespace Dogeometric.App.UI;

/// <summary>
/// The open document: File › New/Open/Save/Save As/Save A Copy As/Import/Export. Native format is .dog; .skp opens
/// and can be written as a copy or export.
/// </summary>
public sealed class DocumentController(Control host, ModelViewport viewport, StatusBar status)
{
    private readonly ModelRenderer _renderer = new();
    private readonly SelectionRenderer _selectionRenderer = new();

    public Document Document { get; private set; } = new(new Model());

    /// <summary>View › Face Style.</summary>
    public FaceStyle FaceStyle
    {
        get => Model.Style.FaceStyle;
        set => SetView(s => s with { FaceStyle = value });
    }

    /// <summary>A View menu switch: changes the model's style without an undo step, as SketchUp does.</summary>
    private void SetView(Func<StyleSettings, StyleSettings> change)
    {
        Model.Style = change(Model.Style);
        ApplyModelStyle();
    }

    private void ApplyModelStyle()
    {
        var rebuild = _renderer.SetStyle(Model.Style, Model.Axes);
        viewport.ApplyStyle(Model.Style);
        UpdateDepthRange();
        StyleChanged?.Invoke();
        if (rebuild)
            Rebuild();
        viewport.UpdateSection();
    }

    /// <summary>Redraws the openings of glued instances being dragged at <paramref name="placement"/> (null when the drag ends).</summary>
    public void PreviewOpenings(Entities entities, IReadOnlyDictionary<ComponentInstance, Dogeometric.Core.Geometry.Transform>? placement)
    {
        _renderer.MovingInstances = placement;
        _renderer.Build(Model, viewport.ModelRoot, [entities]);
        RebuildSelection();
    }

    /// <summary>Shows the model's shadow settings (Shadows window, View › Shadows, a model loaded).</summary>
    public void ApplyShadows()
    {
        if (_renderer.SetShadows(Model.Shadows))
            Rebuild();
        viewport.ApplyShadows(Model.Shadows);
        ShadowsChanged?.Invoke();
    }

    public event Action? ShadowsChanged;

    /// <summary>View › Component Edit › Hide Rest of Model / Hide Similar Components.</summary>
    public bool HideRestOfModel { get; set; }
    public bool HideSimilarComponents { get; set; }

    /// <summary>Re-applies the component-edit fading (after a View › Component Edit toggle).</summary>
    public void RefreshComponentEdit() => RebuildSelection();

    /// <summary>View › Edge Style › Edges.</summary>
    public bool ShowEdges
    {
        get => Model.Style.Edges;
        set => SetView(s => s with { Edges = value });
    }

    /// <summary>View › Edge Style › Extension.</summary>
    public bool ShowExtension
    {
        get => Model.Style.Extension;
        set => SetView(s => s with { Extension = value });
    }

    /// <summary>View › Edge Style › Depth Cue.</summary>
    public bool ShowDepthCue
    {
        get => Model.Style.DepthCue;
        set => SetView(s => s with { DepthCue = value });
    }

    /// <summary>Depth Cue follows the camera: the nearest and farthest corners of the model's box.</summary>
    public void UpdateDepthRange()
    {
        if (!_renderer.ShowDepthCue)
            return;
        var b = Model.Entities.Bounds();
        if (b.IsEmpty)
            return;
        var eye = viewport.Camera.Eye;
        var distances = Enumerable.Range(0, 8).Select(i => eye.DistanceTo(new Dogeometric.Core.Geometry.Vec3(
            (i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z))).ToList();
        _renderer.SetDepthRange(Math.Max(0, eye.DistanceTo(b.Center) - b.Diagonal / 2), distances.Max());
    }

    /// <summary>View › Edge Style › Profiles.</summary>
    public bool ShowProfiles
    {
        get => Model.Style.Profiles;
        set => SetView(s => s with { Profiles = value });
    }

    /// <summary>View › Hidden Objects.</summary>
    public bool ShowHiddenObjects
    {
        get => _renderer.ShowHiddenObjects;
        set
        {
            _renderer.ShowHiddenObjects = value;
            Rebuild();
        }
    }

    /// <summary>View › Hidden Geometry.</summary>
    public bool ShowHiddenGeometry
    {
        get => Model.Style.HiddenGeometry;
        set => SetView(s => s with { HiddenGeometry = value });
    }

    /// <summary>View › Edge Style › Back Edges.</summary>
    public bool ShowBackEdges
    {
        get => Model.Style.BackEdges;
        set => SetView(s => s with { BackEdges = value });
    }

    private List<CenterPoint>? _centers;

    /// <summary>View › Center Points (Dogeometric's own): centres of groups, faces and the selection, shown and snapped to.</summary>
    public bool ShowCenterPoints
    {
        get => AppPreferences.Current.ShowCenterPoints;
        set
        {
            AppPreferences.Current.ShowCenterPoints = value;
            AppPreferences.Save();
            viewport.QueueOverlayRedraw();
        }
    }

    private IReadOnlyList<CenterPoint> Centers()
    {
        if (!ShowCenterPoints)
            return [];
        return _centers ??= CenterPoints.Of(Document.Context.Entities, Document.Context.ToWorld, Document.Selection.Items.ToList());
    }

    /// <summary>View › Guides.</summary>
    public bool ShowGuides
    {
        get => Model.Style.Guides;
        set => SetView(s => s with { Guides = value });
    }

    /// <summary>View › Section Planes, Section Cuts and Axes.</summary>
    public bool ShowSectionPlanes
    {
        get => Model.Style.SectionPlanes;
        set => SetView(s => s with { SectionPlanes = value });
    }

    public bool ShowSectionCuts
    {
        get => Model.Style.SectionCuts;
        set => SetView(s => s with { SectionCuts = value });
    }

    public bool ShowModelAxes
    {
        get => Model.Style.ModelAxes;
        set => SetView(s => s with { ModelAxes = value });
    }
    public Model Model => Document.Model;
    public string? Path { get; private set; }

    public string Title => (Path != null ? System.IO.Path.GetFileName(Path)
        : _recoveredFrom != null ? $"{System.IO.Path.GetFileName(_recoveredFrom)} (recovered)" : "Untitled") + " - Dogeometric";

    private int _savedRevision;

    /// <summary>A recovered backup remembers the model it was a copy of, so Save As suggests it.</summary>
    private string? _recoveredFrom;

    /// <summary>The file the model is (or was a copy of, when recovered), naming its backups.</summary>
    public string? ModelPath => Path ?? _recoveredFrom;

    /// <summary>The model changed since it was opened or last saved.</summary>
    public bool IsModified => Document.Undo.Revision != _savedRevision;

    public event Action? Changed;

    /// <summary>File › New: an empty model; the camera keeps SketchUp's new-model view.</summary>
    /// <summary>File › Recent File: opens one of the recent files (asking to save changes first).</summary>
    public void OpenRecent(string path) => ConfirmDiscard(() =>
    {
        if (System.IO.File.Exists(path))
            Open(path);
        else
            Alert("Open", $"{path} no longer exists.");
    });

    /// <summary>File › Revert: back to the file as last saved, after confirming.</summary>
    public void Revert()
    {
        if (Path is not { } path || !System.IO.File.Exists(path))
            return;
        var d = new ConfirmationDialog { Title = "Revert", DialogText = "Revert to the last saved version? Changes since then will be lost." };
        d.Confirmed += () =>
        {
            d.QueueFree();
            Open(path);
        };
        d.Canceled += d.QueueFree;
        host.AddChild(d);
        d.PopupCentered();
    }

    public void New() => ConfirmDiscard(() => NewFrom(Templates.Create(Templates.Default)));

    /// <summary>An untitled document starting as <paramref name="model"/> (File › New, New From Template).</summary>
    public void NewFrom(Model model) => SetModel(model, null, zoomExtents: false);

    public void ShowOpen() => ConfirmDiscard(() => ShowDialog(FileDialog.FileModeEnum.OpenFile, "Open",
        ["*.skp, *.dog ; Models", "*.dog ; Dogeometric", "*.skp ; SketchUp"], Open));

    /// <summary>Before the model goes away (New, Open, quit): SketchUp's "Save changes?" when it has unsaved changes.</summary>
    public void ConfirmDiscard(Action proceed)
    {
        if (!IsModified)
        {
            proceed();
            return;
        }
        var name = Path != null ? System.IO.Path.GetFileName(Path) : _recoveredFrom != null ? System.IO.Path.GetFileName(_recoveredFrom) : "Untitled";
        var d = new ConfirmationDialog { Title = "Dogeometric", DialogText = $"Save changes to \"{name}\"?", OkButtonText = "Yes", CancelButtonText = "Cancel" };
        d.AddButton("No", true, "discard");
        d.Confirmed += () =>
        {
            d.QueueFree();
            Save(proceed);
        };
        d.CustomAction += action =>
        {
            if (action != "discard")
                return;
            d.QueueFree();
            proceed();
        };
        d.Canceled += d.QueueFree;
        host.AddChild(d);
        d.PopupCentered();
    }

    /// <summary>Opens a backup as an unsaved copy, so saving asks where instead of writing over anything.</summary>
    public void OpenRecovered(string backup, string? original)
    {
        try
        {
            SetModel(DogFile.Load(backup), null);
            // A backup without a known original is named after its model's backups folder ("Untitled").
            _recoveredFrom = original ?? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(backup)) + ".dog";
            _savedRevision = -1;
            Changed?.Invoke();
            status.SetHint($"Recovered {System.IO.Path.GetFileName(backup)} — use Save As to keep it");
        }
        catch (Exception ex)
        {
            Alert("Recover", $"Could not open {System.IO.Path.GetFileName(backup)}:\n{ex.Message}");
        }
    }

    public void ShowImport() => ShowDialog(FileDialog.FileModeEnum.OpenFile, "Import",
        ["*.skp ; SketchUp", "*.dog ; Dogeometric", "*.stl ; STL", "*.obj ; OBJ", "*.dxf ; AutoCAD DXF", "*.png, *.jpg, *.jpeg, *.bmp, *.webp ; Images"], Import,
        dialog =>
        {
            // STL and OBJ carry no unit: the importer asks, as SketchUp's does.
            dialog.AddOption("Units (STL, OBJ)", [.. MeshUnits.Select(u => u.Name)], 0);
            dialog.AddOption("Use image as", ["Image", "Texture", "New Matched Photo"], 0);
            _importOptionsDialog = dialog;
        });

    public enum ImageUse { Image, Texture, MatchedPhoto }

    private static readonly (string Name, double Mm)[] MeshUnits = [("Millimeters", 1), ("Centimeters", 10), ("Meters", 1000), ("Inches", 25.4)];
    private FileDialog? _importOptionsDialog;

    /// <summary>A model imported as a component, to be placed with the cursor.</summary>
    public event Action<ComponentDefinition>? ComponentImportRequested;

    /// <summary>File › Import of a picture, used as an image, a texture or a new matched photo (SketchUp's "Use image as").</summary>
    public event Action<string, ImageUse>? ImageImportRequested;

    public void Save() => Save(null);

    /// <summary>File › Save; <paramref name="saved"/> runs once the model is on disk (not if Save As is cancelled).</summary>
    public void Save(Action? saved)
    {
        if (Path == null || !Path.EndsWith(".dog", StringComparison.OrdinalIgnoreCase))
            ShowSaveAs(saved);
        else if (SaveTo(Path))
            saved?.Invoke();
    }

    public void ShowSaveAs() => ShowSaveAs(null);

    private void ShowSaveAs(Action? saved) => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Save As", ["*.dog ; Dogeometric"], path =>
    {
        path = WithExtension(path, ".dog");
        if (SaveTo(path))
        {
            Path = path;
            _recoveredFrom = null;
            Changed?.Invoke();
            saved?.Invoke();
        }
    });

    /// <summary>Saves the model as its file: the previous version is kept as a .dogb first (Preferences › Create backup).</summary>
    private bool SaveTo(string path)
    {
        Backups.KeepPreviousVersion(path);
        if (!Write(path, DogFile.Save))
            return false;
        _savedRevision = Document.Undo.Revision;
        Changed?.Invoke();
        AppPreferences.AddRecent(path);
        return true;
    }

    public void ShowSaveCopyAs() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Save A Copy As",
        ["*.dog ; Dogeometric", "*.skp ; SketchUp (2017 format)"], path =>
        {
            if (path.EndsWith(".skp", StringComparison.OrdinalIgnoreCase))
                WriteSkp(path);
            else
                Write(WithExtension(path, ".dog"), DogFile.Save);
        });

    public void ShowExport3D() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Export 3D Model",
        ["*.stl ; STL (binary)", "*.obj ; Wavefront OBJ", "*.glb ; glTF binary", "*.dae ; COLLADA", "*.step, *.stp ; STEP (AP214)", "*.skp ; SketchUp (2017 format)"],
        Export, dialog =>
        {
            // SketchUp's "Export selection only" option, on when something is selected.
            dialog.AddOption("Export selection only", [], Document.Selection.IsEmpty ? 0 : 1);
            _exportOptionsDialog = dialog;
        });

    /// <summary>File › Export › 2D Graphic: the view as it is drawn (PNG, JPEG) or as a hidden-line drawing (SVG, PDF).</summary>
    public void ShowExport2D() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Export 2D Graphic",
        ["*.png ; PNG image", "*.jpg, *.jpeg ; JPEG image", "*.svg ; SVG drawing", "*.pdf ; PDF drawing", "*.dxf ; DXF drawing (full scale in parallel views)"], path =>
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".svg" or ".pdf" or ".dxf")
            {
                var segments = viewport.HiddenLineDrawing();
                var (w, h) = (viewport.Size.X, viewport.Size.Y);
                ExportVectorDialog.Show(host, (Vector2I)viewport.Size, viewport.MillimetresPerPixel, !viewport.Camera.Perspective, lines =>
                {
                    try
                    {
                        if (ext == ".svg")
                            System.IO.File.WriteAllText(path, Dogeometric.Core.IO.HiddenLine.ToSvg(segments, w, h, lines));
                        else if (ext == ".dxf")
                            System.IO.File.WriteAllText(path, Dogeometric.Core.IO.HiddenLine.ToDxf(segments, h, viewport.MillimetresPerPixel, lines));
                        else
                            System.IO.File.WriteAllBytes(path, Dogeometric.Core.IO.HiddenLine.ToPdf(segments, w, h, lines));
                        status.SetHint($"Exported {System.IO.Path.GetFileName(path)}");
                    }
                    catch (System.IO.IOException e)
                    {
                        status.SetHint($"Could not export: {e.Message}");
                    }
                });
                return;
            }
            var png = ext is not (".jpg" or ".jpeg");
            ExportImageDialog.Show(host, (Vector2I)viewport.Size, png, () => ExportImage(png ? (ext == ".png" ? path : path + ".png") : path, png));
        });

    private async void ExportImage(string path, bool png)
    {
        var p = AppPreferences.Current;
        var size = p.ExportUseViewSize ? (Vector2I)viewport.Size : new Vector2I(p.ExportWidth, p.ExportHeight);
        var image = await viewport.RenderImage(size, p.ExportAntialias, png && p.ExportTransparent);
        var error = png ? image.SavePng(path) : image.SaveJpg(path, (float)p.ExportJpegQuality);
        status.SetHint(error == Error.Ok ? $"Exported {System.IO.Path.GetFileName(path)}" : $"Could not export: {error}");
    }

    /// <summary>File › Export › Section Slice: the active section's cut as a full-scale DXF.</summary>
    public void ShowExportSectionSlice()
    {
        if (Dogeometric.Core.IO.HiddenLine.SectionSliceDxf(Model) is null)
        {
            Alert("Export Section Slice", "There is no active section cut: place a section plane and make it active first.");
            return;
        }
        ShowDialog(FileDialog.FileModeEnum.SaveFile, "Export Section Slice", ["*.dxf ; DXF drawing"], path =>
        {
            System.IO.File.WriteAllText(path, Dogeometric.Core.IO.HiddenLine.SectionSliceDxf(Model)!);
            status.SetHint($"Exported {System.IO.Path.GetFileName(path)}");
        });
    }

    private FileDialog? _exportOptionsDialog;

    public void Open(string path)
    {
        try
        {
            var model = Load(path);
            SetModel(model, path.EndsWith(".dog", StringComparison.OrdinalIgnoreCase) ? path : null);
            // A .skp opens as an untitled-in-.dog document that remembers where it came from in the title bar.
            if (Path == null)
                Path = path;
            Changed?.Invoke();
            AppPreferences.AddRecent(path);
            status.SetHint($"Opened {System.IO.Path.GetFileName(path)} ({model.SourceVersion})");
        }
        catch (Exception ex)
        {
            Alert("Open", $"Could not open {System.IO.Path.GetFileName(path)}:\n{ex.Message}");
        }
    }

    /// <summary>A model file as one component placed with the cursor, as SketchUp imports a model or takes one from a collection.</summary>
    public void PlaceModel(string path)
    {
        Model other;
        try
        {
            other = Load(path);
        }
        catch (Exception ex)
        {
            Alert("Import", $"Could not import {System.IO.Path.GetFileName(path)}:\n{ex.Message}");
            return;
        }
        ComponentDefinition? def = null;
        Document.Operation("Import", _ => def = Grouping.ImportAsComponent(Model, other, System.IO.Path.GetFileNameWithoutExtension(path)));
        Rebuild();
        Changed?.Invoke();
        ComponentImportRequested?.Invoke(def!);
    }

    private void Import(string path)
    {
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
        {
            var use = _importOptionsDialog?.GetSelectedOptions() is { } options && options.TryGetValue("Use image as", out var chosen) ? (ImageUse)(int)chosen : ImageUse.Image;
            ImageImportRequested?.Invoke(path, use);
            return;
        }
        try
        {
            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".skp" or ".dog")
            {
                PlaceModel(path);
                return;
            }
            var imported = System.IO.Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                ? Dogeometric.Core.IO.DxfImport.Load(path)
                : System.IO.Path.GetExtension(path).ToLowerInvariant() is ".stl" or ".obj"
                ? Dogeometric.Core.IO.MeshImport.Load(path, MeshUnits[_importOptionsDialog?.GetSelectedOptions() is { } o && o.TryGetValue("Units (STL, OBJ)", out var u) ? (int)u : 0].Mm)
                : Load(path);
            Merge(imported);
            Document.Undo.Clear();
            Rebuild();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Alert("Import", $"Could not import {System.IO.Path.GetFileName(path)}:\n{ex.Message}");
        }
    }

    private void Export(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".skp")
        {
            WriteSkp(path);
            return;
        }
        var selectionOnly = _exportOptionsDialog?.GetSelectedOptions() is { } opts && opts.TryGetValue("Export selection only", out var v) && (bool)v
            && !Document.Selection.IsEmpty;
        var options = selectionOnly
            ? new ExportOptions
            {
                Selection = Document.Selection.Items.ToHashSet(),
                SelectionContext = Document.Context.Entities,
                SelectionContextTransform = Document.Context.ToWorld,
            }
            : null;
        if (ext is ".step" or ".stp")
        {
            StepWriter.Result? step = null;
            Write(path, (m, p) => step = StepWriter.Write(m, p, System.IO.Path.GetFileNameWithoutExtension(p), options));
            if (step != null)
                status.SetHint($"Exported {step.Solids} solids and {step.Surfaces} open surfaces");
            return;
        }
        var triangles = MeshExtractor.Extract(Model, options);
        Write(path, (m, p) =>
        {
            switch (ext)
            {
                case ".obj":
                    ObjWriter.Write(triangles, p);
                    break;
                case ".glb":
                    using (var s = File.Create(p)) GltfWriter.WriteGlb(triangles, s);
                    break;
                case ".dae":
                    using (var s = File.Create(p)) DaeWriter.Write(triangles, s);
                    break;
                default:
                    using (var s = File.Create(WithExtension(p, ".stl"))) StlWriter.WriteBinary(triangles, s);
                    break;
            }
        });
        if (ext is ".stl" or "")
        {
            var check = MeshCheck.Analyze(triangles);
            status.SetHint(check.IsWatertight
                ? $"Exported {triangles.Count} triangles, watertight, volume {check.Volume / 1000:0.###} cm³"
                : $"Exported {triangles.Count} triangles — not watertight ({check.BoundaryEdges} open edges, {check.NonManifoldEdges} non-manifold)");
        }
    }

    private void WriteSkp(string path)
    {
        try
        {
            var warnings = SkpExporter.Export(Model, path);
            status.SetHint(warnings.Count == 0 ? $"Saved {System.IO.Path.GetFileName(path)}" : $"Saved with {warnings.Count} warning(s): {warnings[0]}");
        }
        catch (Exception ex)
        {
            Alert("Save", ex.Message);
        }
    }

    private static Model Load(string path) =>
        path.EndsWith(".skp", StringComparison.OrdinalIgnoreCase) ? SkpImporter.Import(path) : DogFile.Load(path);

    private void SetModel(Model model, string? path, bool zoomExtents = true)
    {
        Document = new Document(model);
        _savedRevision = Document.Undo.Revision;
        _recoveredFrom = null;
        Document.GeometryChanged += changed =>
        {
            // Undo and redo can bring back other shadow settings.
            if (Model.Shadows != _renderer.Shadows)
            {
                if (_renderer.SetShadows(Model.Shadows))
                    changed = null;
                viewport.ApplyShadows(Model.Shadows);
                ShadowsChanged?.Invoke();
            }
            if (Model.Style != _renderer.Style)
            {
                if (_renderer.SetStyle(Model.Style, Model.Axes))
                    changed = null;
                viewport.ApplyStyle(Model.Style);
                StyleChanged?.Invoke();
            }
            // Undo and redo can change a material's look, which the renderer caches.
            var looks = MaterialLooks();
            if (looks != _materialLooks)
            {
                _materialLooks = looks;
                _renderer.ForgetMaterials();
                changed = null;
                MaterialsChanged?.Invoke();
            }
            _renderer.Build(Model, viewport.ModelRoot, changed);
            RebuildSelection();
            viewport.UpdateSection();
            viewport.UpdateAxes();
        };
        _materialLooks = MaterialLooks();
        Document.Selection.Changed += RebuildSelection;
        Document.Context.Changed += RebuildSelection;
        Document.Context.Changed += ShowCage;
        _renderer.Edited = Document.Context.Entities;
        Document.GeometryChanged += _ => _centers = null;
        Document.Selection.Changed += () => _centers = null;
        Document.Context.Changed += () => _centers = null;
        _centers = null;
        viewport.CenterPoints = Centers;
        Path = path;
        viewport.Document = Document;
        viewport.ModelBounds = () => Model.Entities.Bounds();
        viewport.PreviewOpenings = PreviewOpenings;
        _renderer.SetShadows(Model.Shadows);
        _renderer.SetStyle(Model.Style, Model.Axes);
        viewport.ApplyStyle(Model.Style);
        StyleChanged?.Invoke();
        Rebuild();
        viewport.UpdateSection();
        viewport.UpdateAxes();
        viewport.ApplyShadows(Model.Shadows);
        if (zoomExtents)
            viewport.ZoomExtents();
        DocumentReplaced?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Raised when a different document is loaded (tools must drop references to the old one).</summary>
    public event Action? DocumentReplaced;

    /// <summary>A subdivided group shows its control mesh while open, and its smooth surface once closed.</summary>
    private void ShowCage()
    {
        var (before, now) = (_renderer.Edited, Document.Context.Entities);
        _renderer.Edited = now;
        if (before != now && (before?.Subdivision > 0 || now.Subdivision > 0))
            _renderer.Build(Model, viewport.ModelRoot, before == null ? [now] : [before, now]);
    }

    /// <summary>SUbD › Subdivision On/Off.</summary>
    public bool Subdivide
    {
        get => _renderer.Subdivide;
        set
        {
            _renderer.Subdivide = value;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Document.Picker.Invalidate();
        _renderer.Build(Model, viewport.ModelRoot);
        RebuildSelection();
    }

    /// <summary>Redraws what model settings change: fading, texture smoothing, the overlay.</summary>
    public void RefreshView() => RebuildSelection();

    private void RebuildSelection()
    {
        _selectionRenderer.Build(Document, viewport.SelectionRoot);
        ModelRenderer.FadeOutside(viewport.ModelRoot, Document.Context.Path, HideRestOfModel, HideSimilarComponents,
            (float)Model.Options.FadeRest, (float)Model.Options.FadeSimilar);
        if (_renderer.SmoothTextures != Model.Options.SmoothTextures)
            _renderer.SmoothTextures = Model.Options.SmoothTextures;
        viewport.QueueOverlayRedraw(); // dimensions and texts show selection and edits on the overlay
    }

    /// <summary>Redraws everything (after a visibility change such as a tag toggled).</summary>
    public void RebuildAll() => Rebuild();

    /// <summary>After a scene is shown: its style, shadows, axes, section cut and tag visibility take effect.</summary>
    public void ApplySceneState()
    {
        _renderer.SetStyle(Model.Style, Model.Axes);
        viewport.ApplyStyle(Model.Style);
        StyleChanged?.Invoke();
        _renderer.SetShadows(Model.Shadows);
        viewport.ApplyShadows(Model.Shadows);
        ShadowsChanged?.Invoke();
        UpdateDepthRange();
        Rebuild();
        viewport.UpdateAxes();
        viewport.UpdateSection();
    }

    /// <summary>Hides the camera being looked through (null shows it again).</summary>
    public void LookThrough(ComponentInstance? camera)
    {
        if (_renderer.LookingThrough == camera)
            return;
        _renderer.LookingThrough = camera;
        Rebuild();
    }

    /// <summary>Raised when the model's style changes (an edit, undo, another model).</summary>
    public event Action? StyleChanged;

    /// <summary>Styles › Edit: changes the model's style as one undoable step.</summary>
    public void SetStyle(Func<StyleSettings, StyleSettings> change)
    {
        Document.Undo.Begin("Edit Style");
        Model.Style = change(Model.Style);
        Document.Undo.Commit();
    }

    /// <summary>A material's colour, opacity or picture changed: cached looks are dropped and the view redrawn.</summary>
    public void MaterialsEdited()
    {
        _materialLooks = MaterialLooks();
        _renderer.ForgetMaterials();
        Rebuild();
    }

    private string _materialLooks = "";

    private string MaterialLooks() => string.Join("|", Model.Materials.Select(m =>
        $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m)}:{m.Color}:{m.Opacity}:{m.Colorize}:{(m.Texture == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m.Texture))}"));

    /// <summary>Raised when a material's picture changed outside an operation's own refresh (Edit Texture Image).</summary>
    public event Action? MaterialsChanged;

    /// <summary>Texture › Edit Texture Image: the picture opens in the image editor (Preferences › Applications) and is
    /// loaded back each time it is saved there, as SketchUp does.</summary>
    public void EditTextureImage(Dogeometric.Core.Modeling.Material material)
    {
        if (material.Texture is not { } texture)
            return;
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dogeometric-textures");
        System.IO.Directory.CreateDirectory(folder);
        var extension = System.IO.Path.GetExtension(texture.FileName) is { Length: > 0 } e ? e : ".png";
        var name = string.Concat((material.Name.Length > 0 ? material.Name : "texture").Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var file = System.IO.Path.Combine(folder, name + extension);
        System.IO.File.WriteAllBytes(file, texture.Data);
        if (AppPreferences.Current.ImageEditor is { Length: > 0 } editor)
            OS.CreateProcess(editor, [file]);
        else
            OS.ShellOpen(file);
        var stamp = System.IO.File.GetLastWriteTimeUtc(file);
        var timer = new Godot.Timer { WaitTime = 1, Autostart = true };
        var ticks = 0;
        timer.Timeout += () =>
        {
            // Watched for an hour, as long as an editing session reasonably lasts.
            if (++ticks > 3600 || !System.IO.File.Exists(file))
            {
                timer.QueueFree();
                return;
            }
            var now = System.IO.File.GetLastWriteTimeUtc(file);
            if (now == stamp)
                return;
            stamp = now;
            try
            {
                var data = System.IO.File.ReadAllBytes(file);
                if (TextureImages.Decode(data) == null)
                    return;
                Document.Undo.Begin("Edit Texture Image");
                material.Texture = new TextureImage { FileName = texture.FileName, Data = data, WidthMm = texture.WidthMm, HeightMm = texture.HeightMm };
                Document.Undo.Commit();
                _renderer.ForgetMaterials();
                Rebuild();
                MaterialsChanged?.Invoke();
            }
            catch (System.IO.IOException)
            {
                // Still being written: next tick.
            }
        };
        host.AddChild(timer);
    }

    /// <summary>Import places the other file's contents into this model (materials, tags and definitions merged).</summary>
    private void Merge(Model other)
    {
        Model.Materials.AddRange(other.Materials);
        foreach (var t in other.Tags.Skip(1))
            Model.Tags.Add(t);
        Model.Definitions.AddRange(other.Definitions);
        Model.Entities.Vertices.AddRange(other.Entities.Vertices);
        Model.Entities.Edges.AddRange(other.Entities.Edges);
        Model.Entities.Faces.AddRange(other.Entities.Faces);
        Model.Entities.Instances.AddRange(other.Entities.Instances);
    }

    private bool Write(string path, Action<Model, string> writer)
    {
        try
        {
            writer(Model, path);
            status.SetHint($"Saved {System.IO.Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            Alert("Save", $"Could not save {System.IO.Path.GetFileName(path)}:\n{ex.Message}");
            return false;
        }
    }

    private static string WithExtension(string path, string ext) =>
        System.IO.Path.GetExtension(path).Length == 0 ? path + ext : path;

    /// <summary>Components › Save As: the definition written as a .dog of its own.</summary>
    public void SaveComponentAs(ComponentDefinition def) => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Save As", ["*.dog ; Dogeometric"], path =>
    {
        path = WithExtension(path, ".dog");
        if (Write(path, (_, p) => DogFile.Save(Grouping.DefinitionAsModel(Model, def), p)))
            AppPreferences.AddRecent(path);
    }, d => d.CurrentFile = def.Name + ".dog");

    /// <summary>A save-file picker for exports (File › Export › Animation).</summary>
    public void PickExport(string title, string[] filters, Action<string> onPicked) =>
        ShowDialog(FileDialog.FileModeEnum.SaveFile, title, filters, onPicked);

    /// <summary>Camera › Match New Photo's file picker.</summary>
    public void PickPhoto(Action<string> onPicked) =>
        ShowDialog(FileDialog.FileModeEnum.OpenFile, "Select Photo", ["*.jpg, *.jpeg, *.png ; Photos"], onPicked);

    private void ShowDialog(FileDialog.FileModeEnum mode, string title, string[] filters, Action<string> onPicked, Action<FileDialog>? configure = null)
    {
        var dialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
            Filters = filters,
            // The system's file picker; test sessions (no desktop portal) set DOGEOMETRIC_NO_NATIVE_DIALOGS.
            UseNativeDialog = OS.GetEnvironment("DOGEOMETRIC_NO_NATIVE_DIALOGS") == "",
            CurrentDir = (Path ?? _recoveredFrom) is { } basis && System.IO.Path.GetDirectoryName(basis) is { Length: > 0 } dir
                && !dir.StartsWith(Backups.Folder) ? dir
                : AppPreferences.Current.Location(title.StartsWith("Export") ? "Export" : "Models") ?? OS.GetSystemDir(OS.SystemDir.Documents),
        };
        if (mode == FileDialog.FileModeEnum.SaveFile && (Path ?? _recoveredFrom) is { } suggested)
            dialog.CurrentFile = System.IO.Path.GetFileNameWithoutExtension(suggested) + System.IO.Path.GetExtension(filters[0].Split(';', ',')[0].Trim());
        configure?.Invoke(dialog);
        host.AddChild(dialog);
        dialog.FileSelected += p =>
        {
            onPicked(p);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(900, 600));
    }

    private void Alert(string title, string text)
    {
        var d = new AcceptDialog { Title = title, DialogText = text };
        host.AddChild(d);
        d.Confirmed += d.QueueFree;
        d.Canceled += d.QueueFree;
        d.PopupCentered();
    }
}

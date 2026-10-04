using Dogeometric.App.Viewport;
using Dogeometric.Core.IO;
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

    public Model Model { get; private set; } = new();
    public string? Path { get; private set; }

    public string Title => (Path == null ? "Untitled" : System.IO.Path.GetFileName(Path)) + " - Dogeometric";

    public event Action? Changed;

    public void New()
    {
        SetModel(new Model(), null);
    }

    public void ShowOpen() => ShowDialog(FileDialog.FileModeEnum.OpenFile, "Open",
        ["*.skp, *.dog ; Models", "*.dog ; Dogeometric", "*.skp ; SketchUp"], Open);

    public void ShowImport() => ShowDialog(FileDialog.FileModeEnum.OpenFile, "Import",
        ["*.skp ; SketchUp", "*.dog ; Dogeometric"], Import);

    public void Save()
    {
        if (Path == null || !Path.EndsWith(".dog", StringComparison.OrdinalIgnoreCase))
            ShowSaveAs();
        else
            Write(Path, DogFile.Save);
    }

    public void ShowSaveAs() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Save As", ["*.dog ; Dogeometric"], path =>
    {
        path = WithExtension(path, ".dog");
        if (Write(path, DogFile.Save))
        {
            Path = path;
            Changed?.Invoke();
        }
    });

    public void ShowSaveCopyAs() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Save A Copy As",
        ["*.dog ; Dogeometric", "*.skp ; SketchUp (2017 format)"], path =>
        {
            if (path.EndsWith(".skp", StringComparison.OrdinalIgnoreCase))
                WriteSkp(path);
            else
                Write(WithExtension(path, ".dog"), DogFile.Save);
        });

    public void ShowExport3D() => ShowDialog(FileDialog.FileModeEnum.SaveFile, "Export 3D Model",
        ["*.stl ; STL (binary)", "*.obj ; Wavefront OBJ", "*.glb ; glTF binary", "*.dae ; COLLADA", "*.skp ; SketchUp (2017 format)"],
        Export);

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
            status.SetHint($"Opened {System.IO.Path.GetFileName(path)} ({model.SourceVersion})");
        }
        catch (Exception ex)
        {
            Alert("Open", $"Could not open {System.IO.Path.GetFileName(path)}:\n{ex.Message}");
        }
    }

    private void Import(string path)
    {
        try
        {
            var imported = Load(path);
            Merge(imported);
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
        var triangles = MeshExtractor.Extract(Model);
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

    private void SetModel(Model model, string? path)
    {
        Model = model;
        Path = path;
        viewport.ModelBounds = () => Model.Entities.Bounds();
        Rebuild();
        viewport.ZoomExtents();
        Changed?.Invoke();
    }

    private void Rebuild() => _renderer.Build(Model, viewport.ModelRoot);

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

    private void ShowDialog(FileDialog.FileModeEnum mode, string title, string[] filters, Action<string> onPicked)
    {
        var dialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
            Filters = filters,
            UseNativeDialog = true,
            CurrentDir = Path != null ? System.IO.Path.GetDirectoryName(Path) : OS.GetSystemDir(OS.SystemDir.Documents),
        };
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

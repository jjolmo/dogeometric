using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.Tools;

/// <summary>
/// File › Import of an image "as texture", as SketchUp does it: click a face where one corner of the picture goes,
/// move to size it (its aspect stays) and click. The face gets a new textured material positioned there, shown
/// live while sizing; the picture repeats across the rest of the face. Typing a width sets the size.
/// </summary>
public sealed class TexturePlaceTool(string name, byte[] data, int pixelsWide, int pixelsHigh, Godot.Color average) : DrawingTool
{
    private Face? _face;
    private bool _back;
    private Vec3 _corner;
    private Material? _material;

    public override int CommandId => 0;
    public override string CursorImage => "paint";
    public override string VcbLabel => "Width";
    public override string StatusText => _face == null
        ? "Click a face to place the image's corner."
        : "Click to set the image size, or type its width.";

    private double Aspect => pixelsHigh / (double)Math.Max(pixelsWide, 1);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || Current is not { } inf)
            return;
        if (_face == null)
        {
            // A face of the group being edited (or the top level), as the Paint Bucket paints.
            if (inf.Face is not { } face || !doc.Context.Entities.Faces.Contains(face))
                return;
            _face = face;
            _corner = doc.Context.ToWorld.Inverse().ApplyPoint(inf.Point);
            // Paint the side facing the viewer.
            _back = inf.EntityToWorld.ApplyNormal(face.Normal).Dot(View.Camera.Direction) > 0;
            _material = new Material
            {
                Name = UniqueName(doc.Model, System.IO.Path.GetFileNameWithoutExtension(name)),
                Color = new Rgba((byte)(average.R * 255), (byte)(average.G * 255), (byte)(average.B * 255)),
                Texture = new TextureImage { FileName = name, Data = data, WidthMm = 100, HeightMm = 100 * Aspect },
            };
            RefreshStatus();
            return;
        }
        doc.CommitPreview();
        Manager.Activate(new SelectTool());
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_face == null || View.Document is not { } doc)
            return;
        var width = WidthFromCursor(doc);
        if (width > Tolerance.Length)
            Show(doc, width);
    }

    /// <summary>The picture's width: how far the cursor is from the corner along the face's horizontal.</summary>
    private double WidthFromCursor(Document doc)
    {
        var face = _face!;
        var toWorld = doc.Context.ToWorld;
        var normal = toWorld.ApplyNormal(face.Normal).Normalized();
        var ray = View.ScreenRay(Mouse);
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal, toWorld.ApplyPoint(_corner)) is not { } hit)
            return 0;
        var p = toWorld.Inverse().ApplyPoint(hit);
        var (cx, cy) = Texturing.PlanePoint(face, _corner);
        var (px, py) = Texturing.PlanePoint(face, p);
        // The larger of the two drag extents decides, so dragging up or across both size it.
        return Math.Max(Math.Abs(px - cx), Math.Abs(py - cy) / Aspect);
    }

    private void Show(Document doc, double width)
    {
        var face = _face!;
        var material = _material!;
        var height = width * Aspect;
        material.Texture!.WidthMm = width;
        material.Texture.HeightMm = height;
        var (cx, cy) = Texturing.PlanePoint(face, _corner);
        var mapping = TextureMapping.FromPlanePoints((cx, cy), (cx + width, cy), (cx, cy + height), width, height);
        doc.Preview("Import Texture", _ =>
        {
            if (!doc.Model.Materials.Contains(material))
                doc.Model.Materials.Add(material);
            if (_back)
            {
                face.BackMaterial = material;
                face.BackMapping = mapping;
            }
            else
            {
                face.FrontMaterial = material;
                face.FrontMapping = mapping;
            }
        });
        View.ShowVcbValue(Length.Format(width, LengthUnit.Millimeters, 1));
    }

    public override bool ApplyVcb(string text)
    {
        if (_face == null || View.Document is not { } doc || !Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm <= 0)
            return false;
        Show(doc, mm);
        doc.CommitPreview();
        Manager.Activate(new SelectTool());
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape)
        {
            View.Document?.CancelPreview();
            Manager.Activate(new SelectTool());
            return true;
        }
        return base.KeyDown(key);
    }

    private static string UniqueName(Model model, string baseName)
    {
        var n = baseName.Length > 0 ? baseName : "Texture";
        var candidate = n;
        for (var i = 2; model.Materials.Any(m => m.Name == candidate); i++)
            candidate = $"{n}{i}";
        return candidate;
    }

    public override void Draw(Control overlay) => DrawInference(overlay);
}

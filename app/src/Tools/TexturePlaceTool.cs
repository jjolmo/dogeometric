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
/// File › Import of an image "as texture", as SketchUp does it: click a face where one corner of the picture goes
/// (its centre with Ctrl), move to size it (its aspect stays unless Shift is held) and click; double-clicking drops it
/// at its own size. The face gets a new textured material positioned there, shown live while sizing; the picture
/// repeats across the rest of the face. Typing a width sets the size.
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
        ? "Pick first point on face to paint with image. Dbl Click=Drop.  Ctrl=By Center."
        : "Pick second point of image." + (Input.IsKeyPressed(Key.Shift) ? "  Shift=Uniform." : "  Shift=Non-Uniform.");

    private ulong _lastClickMs;

    private double Aspect => pixelsHigh / (double)Math.Max(pixelsWide, 1);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || Current is not { } inf)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 450;
        _lastClickMs = now;
        if (_face != null && doubleClick)
        {
            // Dropped at the picture's own size: a pixel to a millimetre, as an image file carries no size.
            Show(doc, pixelsWide, pixelsHigh, byCenter: false);
            doc.CommitPreview();
            Manager.Activate(new SelectTool());
            return;
        }
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
        var (width, height) = SizeFromCursor(doc);
        if (width > Tolerance.Length && height > Tolerance.Length)
            Show(doc, width, height, Input.IsKeyPressed(Key.Ctrl));
        RefreshStatus();
    }

    /// <summary>The picture's size from the cursor: its proportions kept, or free with Shift; twice as big by centre.</summary>
    private (double Width, double Height) SizeFromCursor(Document doc)
    {
        var face = _face!;
        var toWorld = doc.Context.ToWorld;
        var normal = toWorld.ApplyNormal(face.Normal).Normalized();
        var ray = View.ScreenRay(Mouse);
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal, toWorld.ApplyPoint(_corner)) is not { } hit)
            return (0, 0);
        var p = toWorld.Inverse().ApplyPoint(hit);
        var (cx, cy) = Texturing.PlanePoint(face, _corner);
        var (px, py) = Texturing.PlanePoint(face, p);
        var factor = Input.IsKeyPressed(Key.Ctrl) ? 2 : 1;
        var (dx, dy) = (Math.Abs(px - cx) * factor, Math.Abs(py - cy) * factor);
        if (Input.IsKeyPressed(Key.Shift))
            return (dx, dy);
        // The larger of the two drag extents decides, so dragging up or across both size it.
        var width = Math.Max(dx, dy / Aspect);
        return (width, width * Aspect);
    }

    private void Show(Document doc, double width, double height, bool byCenter)
    {
        var face = _face!;
        var material = _material!;
        material.Texture!.WidthMm = width;
        material.Texture.HeightMm = height;
        var (cx, cy) = Texturing.PlanePoint(face, _corner);
        if (byCenter)
            (cx, cy) = (cx - width / 2, cy - height / 2);
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
        View.ShowVcbValue(UI.Measure.Show(width));
    }

    public override bool ApplyVcb(string text)
    {
        if (_face == null || View.Document is not { } doc || !UI.Measure.Read(text, out var mm) || mm <= 0)
            return false;
        // A typed width keeps the proportions the drag gave (non-uniform with Shift), else the picture's.
        var ratio = _material?.Texture is { WidthMm: > 0 } t ? t.HeightMm / t.WidthMm : Aspect;
        Show(doc, mm, mm * ratio, Input.IsKeyPressed(Key.Ctrl));
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

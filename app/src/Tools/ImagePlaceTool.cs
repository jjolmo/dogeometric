using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.Tools;

/// <summary>
/// File › Import of a picture "as image", as SketchUp places one: click a corner on a face (or the plane facing the
/// view), move to size it and click. Ctrl places it by its centre, Shift lets its proportions change, Alt paints it on
/// the face as a texture instead; double-clicking drops it at its own size. It becomes an image entity: a component
/// holding one textured rectangle.
/// </summary>
public sealed class ImagePlaceTool(string name, byte[] data, int pixelsWide, int pixelsHigh, Godot.Color average) : DrawingTool
{
    private Vec3? _origin;
    private Vec3 _x, _y, _normal;
    private ulong _lastClickMs;

    public override int CommandId => 0;
    public override string VcbLabel => "Width";
    public override string StatusText => (_origin == null ? "Place image. Ctrl=By Center.  Alt=Paint." : "Pick second point of image.")
        + (Input.IsKeyPressed(Key.Shift) ? "  Shift=Uniform." : "  Shift=Non-Uniform.");

    private double Aspect => pixelsHigh / (double)Math.Max(pixelsWide, 1);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc || Current is not { } inf)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 450;
        _lastClickMs = now;
        if (_origin == null)
        {
            _origin = inf.Point;
            _normal = inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal).Normalized() : MostFacingPlane();
            // Seen from the viewer's side, with its top up the plane.
            if (_normal.Dot(View.Camera.Direction) > 0)
                _normal = -_normal;
            (_x, _y) = Texturing.PlaneAxes(_normal);
            RefreshStatus();
            return;
        }
        if (doubleClick)
        {
            // Dropped at the picture's own size: a pixel to a millimetre, as an image file carries no size.
            Place(doc, pixelsWide, pixelsHigh, _origin.Value);
            return;
        }
        var (w, h, corner) = Rectangle();
        if (w > Tolerance.Length && h > Tolerance.Length)
            Place(doc, w, h, corner);
    }

    /// <summary>The image's size and lower-left corner for the cursor (proportions kept unless Shift, centred with Ctrl).</summary>
    private (double Width, double Height, Vec3 Corner) Rectangle()
    {
        var origin = _origin!.Value;
        var ray = View.ScreenRay(Mouse);
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, origin) is not { } p)
            return (0, 0, origin);
        var (dx, dy) = ((p - origin).Dot(_x), (p - origin).Dot(_y));
        var byCenter = Input.IsKeyPressed(Key.Ctrl);
        var (w, h) = (Math.Abs(dx) * (byCenter ? 2 : 1), Math.Abs(dy) * (byCenter ? 2 : 1));
        if (!Input.IsKeyPressed(Key.Shift))
        {
            w = Math.Max(w, h / Aspect);
            h = w * Aspect;
        }
        var corner = byCenter
            ? origin - _x * (w / 2) - _y * (h / 2)
            : origin + _x * (dx < 0 ? -w : 0) + _y * (dy < 0 ? -h : 0);
        return (w, h, corner);
    }

    private void Place(Document doc, double width, double height, Vec3 corner)
    {
        var definition = new ComponentDefinition { Name = UniqueDefinition(doc.Model, System.IO.Path.GetFileNameWithoutExtension(name)), IsImage = true };
        var material = new Material
        {
            Name = UniqueMaterial(doc.Model, $"[{System.IO.Path.GetFileNameWithoutExtension(name)}]"),
            Color = new Rgba((byte)(average.R * 255), (byte)(average.G * 255), (byte)(average.B * 255)),
            Texture = new TextureImage { FileName = name, Data = data, WidthMm = width, HeightMm = height },
        };
        var face = definition.Entities.AddFace([Vec3.Zero, new Vec3(width, 0, 0), new Vec3(width, height, 0), new Vec3(0, height, 0)]);
        face.FrontMaterial = face.BackMaterial = material;
        var placement = new Transform(_x, _y, _normal, corner).Then(doc.Context.ToWorld.Inverse());
        ComponentInstance? placed = null;
        doc.Operation("Place Image", e =>
        {
            doc.Model.Materials.Add(material);
            doc.Model.Definitions.Add(definition);
            placed = e.AddInstance(definition, placement);
        });
        if (placed != null)
            doc.Selection.Set([placed]);
        Manager.Activate(new SelectTool());
    }

    public override bool ApplyVcb(string text)
    {
        if (_origin == null || View.Document is not { } doc || !Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm <= 0)
            return false;
        var (w, h, _) = Rectangle();
        var ratio = w > Tolerance.Length ? h / w : Aspect;
        var corner = Input.IsKeyPressed(Key.Ctrl) ? _origin.Value - _x * (mm / 2) - _y * (mm * ratio / 2) : _origin.Value;
        Place(doc, mm, mm * ratio, corner);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape:
                Manager.Activate(new SelectTool());
                return true;
            case Key.Alt when !key.Echo:
                Manager.Activate(new TexturePlaceTool(name, data, pixelsWide, pixelsHigh, average));
                return true;
            case Key.Shift or Key.Ctrl:
                RefreshStatus();
                View.QueueOverlayRedraw();
                break;
        }
        return base.KeyDown(key);
    }

    private static string UniqueDefinition(Model model, string baseName)
    {
        var n = baseName.Length > 0 ? baseName : "Image";
        var candidate = n;
        for (var i = 1; model.Definitions.Any(d => d.Name == candidate); i++)
            candidate = $"{n}#{i}";
        return candidate;
    }

    private static string UniqueMaterial(Model model, string baseName)
    {
        var candidate = baseName;
        for (var i = 1; model.Materials.Any(m => m.Name == candidate); i++)
            candidate = $"{baseName}{i}";
        return candidate;
    }

    public override void Draw(Control overlay)
    {
        if (_origin != null)
        {
            var (w, h, corner) = Rectangle();
            Vec3[] c = [corner, corner + _x * w, corner + _x * w + _y * h, corner + _y * h];
            for (var i = 0; i < 4; i++)
                DrawWorldLine(overlay, c[i], c[(i + 1) % 4], Colors.Black, 1.5f);
            DrawWorldLine(overlay, c[0], c[2], new Color(0.4f, 0.4f, 0.4f), 1, dashed: true);
            DrawWorldLine(overlay, c[1], c[3], new Color(0.4f, 0.4f, 0.4f), 1, dashed: true);
        }
        DrawInference(overlay);
    }
}

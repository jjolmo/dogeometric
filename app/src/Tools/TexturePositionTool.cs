using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;
using Material = Dogeometric.Core.Modeling.Material;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's Texture › Position (fixed pins), live: red moves, green scales and turns about red, blue scales or
/// shears the other side. Return or a click away keeps it, Esc puts it back.</summary>
public sealed class TexturePositionTool(Face face, bool back) : Tool
{
    private static readonly Color Red = new(0.9f, 0.1f, 0.1f);
    private static readonly Color Green = new(0.1f, 0.65f, 0.15f);
    private static readonly Color Blue = new(0.15f, 0.3f, 0.95f);

    private const double Inch = 25.4;
    private (double X, double Y) _o, _u, _v;
    private int _dragging = -1;
    private Material _material = null!;

    public override int CommandId => 0;
    public override string StatusText => "Drag the pins: red moves, green scales and rotates, blue scales or shears. Return or click elsewhere to finish.";

    public override void Activate()
    {
        _material = (back ? face.BackMaterial : face.FrontMaterial)!;
        var tex = _material.Texture!;
        var (tw, th) = (tex.WidthMm, tex.HeightMm);
        if ((back ? face.BackMapping : face.FrontMapping) is { } m)
        {
            // The tile's corners in the face's plane frame: [u v 1]·M with u, v in tile inches.
            var k = m.Matrix;
            double twi = tw / Inch, thi = th / Inch;
            _o = (k[6] * Inch, k[7] * Inch);
            _u = ((twi * k[0] + k[6]) * Inch, (twi * k[1] + k[7]) * Inch);
            _v = ((thi * k[3] + k[6]) * Inch, (thi * k[4] + k[7]) * Inch);
        }
        else
        {
            _o = (0, 0);
            _u = (tw, 0);
            _v = (0, th);
        }
        View.QueueOverlayRedraw();
    }

    private Vec3 Normal => face.Normal.Normalized();

    private Transform ToWorld => View.Document!.Context.ToWorld;

    /// <summary>A point of the face's plane frame in world space.</summary>
    private Vec3 World((double X, double Y) p)
    {
        var (xr, yr) = Texturing.PlaneAxes(Normal);
        var height = face.OuterLoop.Points.First().Dot(Normal);
        return ToWorld.ApplyPoint(xr * p.X + yr * p.Y + Normal * height);
    }

    /// <summary>Where the cursor meets the face's plane, in the plane frame.</summary>
    private (double X, double Y)? Plane(Vector2 screen)
    {
        var ray = View.ScreenRay(screen);
        var n = ToWorld.ApplyNormal(Normal).Normalized();
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), n, ToWorld.ApplyPoint(face.OuterLoop.Points.First())) is not { } hit)
            return null;
        return Texturing.PlanePoint(face, ToWorld.Inverse().ApplyPoint(hit));
    }

    private (double X, double Y)[] Pins => [_o, _u, _v];

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        for (var i = 0; i < 3; i++)
            if (View.ToScreen(World(Pins[i])) is { } s && s.DistanceTo(position) < 10)
            {
                _dragging = i;
                return;
            }
        Finish(doc);
    }

    public override void MouseUp(MouseButton button, Vector2 position) => _dragging = -1;

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_dragging < 0 || Plane(position) is not { } p)
            return;
        switch (_dragging)
        {
            case 0:
                var (dx, dy) = (p.X - _o.X, p.Y - _o.Y);
                _o = p;
                _u = (_u.X + dx, _u.Y + dy);
                _v = (_v.X + dx, _v.Y + dy);
                break;
            case 1:
            {
                // Scale and turn about the red pin; the other side keeps square and in proportion.
                var (ux, uy) = (_u.X - _o.X, _u.Y - _o.Y);
                var (vx, vy) = (_v.X - _o.X, _v.Y - _o.Y);
                var (nx, ny) = (p.X - _o.X, p.Y - _o.Y);
                var lu = Math.Sqrt(ux * ux + uy * uy);
                var ln = Math.Sqrt(nx * nx + ny * ny);
                if (ln < 1e-6 || lu < 1e-9)
                    return;
                var scale = ln / lu;
                var angle = Math.Atan2(ny, nx) - Math.Atan2(uy, ux);
                var (c, s) = (Math.Cos(angle), Math.Sin(angle));
                _u = p;
                _v = (_o.X + (vx * c - vy * s) * scale, _o.Y + (vx * s + vy * c) * scale);
                break;
            }
            case 2:
                _v = p;
                break;
        }
        Preview();
    }

    private void Preview()
    {
        if (View.Document is not { } doc)
            return;
        var tex = _material.Texture!;
        var mapping = TextureMapping.FromPlanePoints(_o, _u, _v, tex.WidthMm, tex.HeightMm);
        doc.Preview("Position Texture", _ =>
        {
            if (back)
                face.BackMapping = mapping;
            else
                face.FrontMapping = mapping;
        });
        View.QueueOverlayRedraw();
    }

    private void Finish(Document doc)
    {
        doc.CommitPreview();
        Manager.Activate(new SelectTool());
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (View.Document is not { } doc)
            return false;
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                Finish(doc);
                return true;
            case Key.Escape:
                doc.CancelPreview();
                Manager.Activate(new SelectTool());
                return true;
        }
        return false;
    }

    public override void Deactivate() => View.Document?.CommitPreview();

    public override void Draw(Control overlay)
    {
        var corner = (_u.X + _v.X - _o.X, _u.Y + _v.Y - _o.Y);
        (double, double)[] outline = [_o, _u, corner, _v];
        for (var i = 0; i < 4; i++)
            if (View.ToScreen(World(outline[i])) is { } a && View.ToScreen(World(outline[(i + 1) % 4])) is { } b)
                overlay.DrawDashedLine(a, b, new Color(0.2f, 0.2f, 0.2f), 1, 4);
        Color[] colors = [Red, Green, Blue];
        for (var i = 0; i < 3; i++)
            if (View.ToScreen(World(Pins[i])) is { } s)
            {
                overlay.DrawCircle(s, 7, Colors.White);
                overlay.DrawCircle(s, 5.5f, colors[i]);
            }
    }
}

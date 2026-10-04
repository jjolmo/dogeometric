using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Offset: click a face, move in or out to set the distance (or type it), click to draw the offset
/// outline. The new edges split the face, ready for Push/Pull (enclosure walls, lips, pockets).
/// </summary>
public sealed class OffsetTool : DrawingTool
{
    private static double _lastDistance;

    private Face? _face;
    private Face? _hover;
    private double _distance;

    public override int CommandId => CommandIds.Offset;
    public override string VcbLabel => "Distance";
    public override string StatusText => _face == null ? "Pick point from which offset will be measured." : "Pick point to define offset or enter value.";
    public override string VcbValue => _face != null ? Length.Format(_distance, LengthUnit.Millimeters, 1) : "";

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_face == null)
            _hover = FaceUnderCursor(position);
        else
            _distance = DistanceFromCursor(_face);
        View.ShowVcbValue(VcbValue);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_face == null)
        {
            _face = FaceUnderCursor(position);
            _distance = 0;
            RefreshStatus();
            return;
        }
        Create(doc, _face, _distance);
    }

    public override bool ApplyVcb(string text)
    {
        if (!Length.TryParse(text, LengthUnit.Millimeters, out var mm) || View.Document is not { } doc)
            return false;
        var face = _face ?? _hover;
        if (face == null)
            return false;
        // Typed distances keep the side the cursor is on (inside = positive).
        var sign = _face != null && _distance < 0 ? -1 : 1;
        Create(doc, face, sign * Math.Abs(mm));
        return true;
    }

    private void Create(Document doc, Face face, double distance)
    {
        if (Math.Abs(distance) > Tolerance.Length)
        {
            var loop = PolygonOffset.Offset(face.OuterLoop.Points.ToList(), face.Normal, distance);
            var normal = face.Normal;
            doc.Operation("Offset", e => StickyGeometry.DrawEdges(e, loop, closed: true, normal));
            _lastDistance = distance;
        }
        _face = null;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>Signed distance from the face outline to the cursor on the face plane (inside = positive).</summary>
    private double DistanceFromCursor(Face face)
    {
        var toWorld = View.Document!.Context.ToWorld;
        var normal = toWorld.ApplyNormal(face.Normal);
        var pts = face.OuterLoop.Points.Select(toWorld.ApplyPoint).ToList();
        var ray = View.ScreenRay(Mouse);
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal, pts[0]) is not { } p)
            return _distance;
        var min = double.PositiveInfinity;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var t = Math.Clamp((p - a).Dot(b - a) / Math.Max((b - a).LengthSquared, 1e-12), 0, 1);
            min = Math.Min(min, p.DistanceTo(a + (b - a) * t));
        }
        var (u, v) = Polygon.PlaneAxes(normal);
        var poly = pts.Select(q => (q.Dot(u), q.Dot(v))).ToList();
        var inside = PointInPolygon((p.Dot(u), p.Dot(v)), poly);
        // Local distance (contexts can be scaled).
        var scale = toWorld.ApplyVector(face.Normal).Length;
        return (inside ? min : -min) / scale;
    }

    private static bool PointInPolygon((double X, double Y) p, List<(double X, double Y)> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    private Face? FaceUnderCursor(Vector2 position)
    {
        if (View.Document is not { } doc || View.Pick(position) is not { Face: { } f } hit)
            return null;
        return hit.Path.SequenceEqual(doc.Context.Path) ? f : null;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _face != null)
        {
            _face = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        var toWorld = View.Document?.Context.ToWorld ?? Transform.Identity;
        var face = _face ?? _hover;
        if (face == null)
            return;
        var outline = face.OuterLoop.Points.Select(toWorld.ApplyPoint).ToList();
        for (var i = 0; i < outline.Count; i++)
            DrawWorldLine(overlay, outline[i], outline[(i + 1) % outline.Count], new Color(0, 0, 1), 1.5f);
        if (_face != null && Math.Abs(_distance) > Tolerance.Length)
        {
            var loop = PolygonOffset.Offset(face.OuterLoop.Points.ToList(), face.Normal, _distance).Select(toWorld.ApplyPoint).ToList();
            for (var i = 0; i < loop.Count; i++)
                DrawWorldLine(overlay, loop[i], loop[(i + 1) % loop.Count], Colors.Black, 1.5f);
        }
    }
}

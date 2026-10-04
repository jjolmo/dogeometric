using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;
using System.Globalization;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Rotate: click the centre (protractor on the face under the cursor or the axis plane facing the
/// viewer; arrows lock it), click the start direction, then the end. Angles snap every 15°; type degrees or a
/// slope like "1:2". Ctrl rotates a copy, "6x" afterwards makes a polar array.
/// </summary>
public sealed class RotateTool : DrawingTool
{
    private const double SnapDegrees = 15;

    private Vec3? _center;
    private Vec3 _normal = Vec3.UnitZ;
    private Vec3? _lockedNormal;
    private Vec3? _startDir;
    private List<object> _items = [];
    private bool _copy;
    private (List<object> Items, Vec3 Center, Vec3 Normal, double Angle)? _lastCopy;

    public override int CommandId => CommandIds.Rotate;
    public override string CursorImage => _copy ? "rotateadd" : "rotate";
    protected override Vec3? From => _startDir == null ? _center : null;
    public override string VcbLabel => "Angle";

    public override string StatusText => (_center, _startDir) switch
    {
        (null, _) => "Click something to select it and set the center point of rotation.",
        (_, null) => "Click to begin rotating this object.",
        _ => _copy ? "Click to set the rotated copy or enter angle." : "Click to set the rotation or enter angle.",
    };

    public override string VcbValue => Angle() is { } a ? (a * 180 / Math.PI).ToString("0.#", CultureInfo.InvariantCulture) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_center == null)
        {
            _items = doc.Selection.IsEmpty ? ItemUnderCursor(doc, position) : doc.Selection.Items.ToList();
            if (_items.Count == 0)
                return;
            _center = inf.Point;
            _normal = _lockedNormal ?? (inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal) : InferenceEngine.MostFacing(View.Camera.Direction));
            _lastCopy = null;
        }
        else if (_startDir == null)
        {
            var d = OnPlane(inf) - _center.Value;
            if (d.IsZero(1e-6))
                return;
            _startDir = d.Normalized();
        }
        else if (Angle() is { } angle)
        {
            Finish(doc, angle);
        }
        RefreshStatus();
    }

    private List<object> ItemUnderCursor(Document doc, Vector2 position)
    {
        if (View.Pick(position) is not { } hit)
            return [];
        var context = doc.Context.Path;
        if (hit.Path.Count < context.Count || !hit.Path.Take(context.Count).SequenceEqual(context))
            return [];
        return [hit.Path.Count > context.Count ? hit.Path[context.Count] : hit.Entity];
    }

    private Vec3 OnPlane(InferenceResult inf)
    {
        var c = _center!.Value;
        var ray = View.ScreenRay(Mouse);
        if (inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, c) is { } hit)
            return hit;
        return inf.Point - _normal * (inf.Point - c).Dot(_normal);
    }

    /// <summary>Signed angle from the start direction to the cursor, snapped to 15° near the steps.</summary>
    private double? Angle()
    {
        if (_center is not { } c || _startDir is not { } s || Current is not { } inf)
            return null;
        var d = (OnPlane(inf) - c).Normalized();
        if (d.IsZero(1e-9))
            return null;
        var angle = Math.Atan2(s.Cross(d).Dot(_normal), s.Dot(d));
        var deg = angle * 180 / Math.PI;
        var snapped = Math.Round(deg / SnapDegrees) * SnapDegrees;
        if (Math.Abs(deg - snapped) < 2 && inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace)
            angle = snapped * Math.PI / 180;
        return angle;
    }

    private void Finish(Document doc, double angle)
    {
        var toLocal = doc.Context.ToWorld.Inverse();
        var center = toLocal.ApplyPoint(_center!.Value);
        var normal = toLocal.ApplyVector(_normal).Normalized();
        var rotation = Transform.Rotation(normal, angle, center);
        var items = _items;
        if (_copy)
        {
            doc.Operation("Rotate", e => doc.Selection.Set(Transforming.Copy(e, items, rotation)));
            _lastCopy = (items, center, normal, angle);
        }
        else
        {
            doc.Operation("Rotate", e => Transforming.Apply(e, items, rotation));
        }
        _center = null;
        _startDir = null;
        ResetLocks();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc)
            return false;
        var t = text.Trim().ToLowerInvariant();
        if (_lastCopy is { } last && _center == null && (t.EndsWith('x') || t.StartsWith('/') || t.EndsWith('/')))
        {
            if (!int.TryParse(t.Trim('x', '/', '*'), out var n) || n < 2)
                return false;
            var divide = t.Contains('/');
            doc.Undo.Undo();
            doc.Operation("Rotate", e =>
            {
                var step = divide ? last.Angle / n : last.Angle;
                for (var i = 1; i <= n; i++)
                    Transforming.Copy(e, last.Items, Transform.Rotation(last.Normal, step * i, last.Center));
            });
            return true;
        }
        if (_startDir == null)
            return false;
        double degrees;
        if (t.Contains(':'))
        {
            // Slope "rise:run".
            var parts = t.Split(':');
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rise) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var run) || run == 0)
                return false;
            degrees = Math.Atan2(rise, run) * 180 / Math.PI;
        }
        else if (!double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
        {
            return false;
        }
        // The sign follows the side the cursor is on, as SketchUp does.
        var sign = Angle() is { } a && a < 0 ? -1 : 1;
        Finish(doc, sign * Math.Abs(degrees) * Math.PI / 180);
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Ctrl when !key.Echo:
                _copy = !_copy;
                RefreshStatus();
                return true;
            case Key.Escape when _center != null:
                _center = null;
                _startDir = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Right or Key.Left or Key.Up when _center == null:
                var axis = key.Keycode == Key.Right ? Vec3.UnitX : key.Keycode == Key.Left ? Vec3.UnitY : Vec3.UnitZ;
                _lockedNormal = _lockedNormal == axis ? null : axis;
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_center is { } c && View.ToScreen(c) is { } sc)
        {
            // Protractor: a circle in the rotation plane, coloured by its normal axis.
            var color = AxisColor(Math.Abs(_normal.X) > 0.99 || Math.Abs(_normal.Y) > 0.99 || Math.Abs(_normal.Z) > 0.99 ? _normal : null);
            var radius = 60 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(c));
            var (u, v) = Polygon.PlaneAxes(_normal);
            Vector2? prev = null;
            for (var i = 0; i <= 48; i++)
            {
                var a = 2 * Math.PI * i / 48;
                if (View.ToScreen(c + u * (radius * Math.Cos(a)) + v * (radius * Math.Sin(a))) is { } p)
                {
                    if (prev is { } q)
                        overlay.DrawLine(q, p, color, 1.5f, true);
                    prev = p;
                }
            }
            if (_startDir is { } s && Current is { } inf)
            {
                DrawWorldLine(overlay, c, c + s * radius * 1.5, Colors.Black, 1, dashed: true);
                DrawWorldLine(overlay, c, OnPlane(inf), Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}

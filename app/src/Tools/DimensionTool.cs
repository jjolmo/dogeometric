using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Dimension: click two points (or an edge), then pull the dimension line out and click to place it.
/// The offset snaps to the red, green or blue direction when close to one. An arc gives a radius, a circle a diameter,
/// with a leader to where the text is placed.
/// </summary>
public sealed class DimensionTool : DrawingTool
{
    private Vec3? _start;
    private Vec3? _end;

    // A radial dimension being placed (context coordinates), its text following the cursor.
    private LinearDimension? _radial;

    public override int CommandId => CommandIds.Dimension;
    public override string CursorImage => "dimension";
    public override string VcbLabel => _end != null ? "Offset" : "";
    public override string VcbValue => _start is { } && _end is { } ? Core.Units.Length.Format(Offset().Length, Core.Units.LengthUnit.Millimeters, 1) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    /// <summary>A typed offset places the dimension line that far out, on the side the cursor is.</summary>
    public override bool ApplyVcb(string text)
    {
        if (_start == null || _end == null || View.Document is not { } doc
            || !Core.Units.Length.TryParse(text, Core.Units.LengthUnit.Millimeters, out var mm) || mm <= 0)
            return false;
        var direction = Offset();
        if (direction.IsZero(1e-9))
            return false;
        Place(doc, direction.Normalized() * mm);
        RefreshStatus();
        return true;
    }
    protected override Vec3? From => _end == null ? _start : null;

    public override string StatusText => (_start, _end) switch
    {
        _ when _radial != null => "Select position for dimension.",
        (null, _) => "Select an edge, curve, or two points to dimension, or drag one to move.",
        (_, null) => "Select second point for linear dimension.",
        _ => "Select position for dimension.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_radial is { } radial)
        {
            radial.Offset = RadialOffset(doc, radial);
            radial.Style = doc.Model.Dimensions;
            doc.Operation("Dimension", ent => ent.Dimensions.Add(radial));
            _radial = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return;
        }
        if (_start == null)
        {
            var toLocal = doc.Context.ToWorld.Inverse();
            if (inf is { Kind: InferenceKind.OnEdge or InferenceKind.Midpoint, Edge: { Curve: not null } curveEdge } && doc.Context.Entities.Edges.Contains(curveEdge)
                && RadialDimensions.For(doc.Context.Entities, curveEdge, toLocal.ApplyPoint(inf.Point)) is { } r)
            {
                _radial = r;
                RefreshStatus();
                return;
            }
            if (inf is { Kind: InferenceKind.OnEdge, Edge: { } edge })
            {
                // An edge dimensions its own length.
                _start = inf.EntityToWorld.ApplyPoint(edge.Start.Position);
                _end = inf.EntityToWorld.ApplyPoint(edge.End.Position);
            }
            else
            {
                _start = inf.Point;
            }
        }
        else if (_end == null)
        {
            if (inf.Point.DistanceTo(_start.Value) <= Tolerance.Length)
                return;
            _end = inf.Point;
        }
        else
        {
            Place(doc);
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>From the arrow point to the cursor, on the plane through it facing the viewer (context coordinates).</summary>
    private Vec3 RadialOffset(Document doc, LinearDimension d)
    {
        var xf = doc.Context.ToWorld;
        var start = xf.ApplyPoint(d.Start);
        var ray = View.ScreenRay(Mouse);
        return InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), View.Camera.Direction.Normalized(), start) is { } hit
            ? xf.Inverse().ApplyVector(hit - start)
            : Vec3.Zero;
    }

    /// <summary>Offset of the dimension line from the measured points, for the cursor.</summary>
    private Vec3 Offset()
    {
        var s = _start!.Value;
        var e = _end!.Value;
        var axis = (e - s).Normalized();
        // The cursor on the plane through the measured segment that faces the viewer most.
        var view = View.Camera.Direction;
        var normal = view - axis * view.Dot(axis);
        var ray = View.ScreenRay(Mouse);
        Vec3 point;
        if (Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.Center or InferenceKind.OnEdge } snap)
            point = snap.Point;
        else if (!normal.IsZero(1e-9) && InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal.Normalized(), s) is { } hit)
            point = hit;
        else
            return Vec3.Zero;
        var offset = point - s;
        offset -= axis * offset.Dot(axis);
        // Snap to an axis direction perpendicular to the segment.
        foreach (var a in new[] { Red, Green, Blue })
        {
            if (Math.Abs(a.Dot(axis)) > 1e-6 || offset.IsZero(1e-9))
                continue;
            var cos = Math.Abs(offset.Normalized().Dot(a));
            if (cos > Math.Cos(10 * Math.PI / 180))
                return a * offset.Dot(a);
        }
        return offset;
    }

    private void Place(Document doc, Vec3? worldOffset = null)
    {
        var toLocal = doc.Context.ToWorld.Inverse();
        var s = toLocal.ApplyPoint(_start!.Value);
        var e = toLocal.ApplyPoint(_end!.Value);
        var offset = toLocal.ApplyVector(worldOffset ?? Offset());
        doc.Operation("Dimension", ent => ent.Dimensions.Add(new LinearDimension(s, e, offset) { Style = doc.Model.Dimensions }));
        _start = null;
        _end = null;
        ResetLocks();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && (_start != null || _radial != null))
        {
            _radial = null;
            _start = null;
            _end = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_radial is { } r && View.Document is { } d)
        {
            var xf = d.Context.ToWorld;
            var start = xf.ApplyPoint(r.Start);
            var end = start + xf.ApplyVector(RadialOffset(d, r));
            DrawWorldLine(overlay, start, end, Colors.Black, 1);
            if (View.ToScreen(end) is { } at)
            {
                var text = r.Prefix + Core.Units.Length.Format(r.Length, d.Model.Units, d.Model.UnitPrecision);
                overlay.DrawString(overlay.GetThemeDefaultFont(), at + new Vector2(4, 4), text, HorizontalAlignment.Left, -1, 13, Colors.Black);
            }
        }
        if (_start is { } s)
        {
            if (_end is { } e)
            {
                // Preview of the dimension being placed.
                var o = Offset();
                DrawWorldLine(overlay, s, s + o, Colors.Black, 1);
                DrawWorldLine(overlay, e, e + o, Colors.Black, 1);
                DrawWorldLine(overlay, s + o, e + o, Colors.Black, 1);
                if (View.ToScreen((s + e) * 0.5 + o) is { } mid)
                {
                    var model = View.Document!.Model;
                    var text = Core.Units.Length.Format(s.DistanceTo(e), model.Units, model.UnitPrecision);
                    var font = overlay.GetThemeDefaultFont();
                    var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 13);
                    overlay.DrawString(font, mid - new Vector2(size.X / 2, 4), text, HorizontalAlignment.Left, -1, 13, Colors.Black);
                }
            }
            else if (Current is { } inf)
            {
                DrawWorldLine(overlay, s, inf.Point, Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}

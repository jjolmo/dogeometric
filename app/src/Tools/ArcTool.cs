using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's 2-Point Arc: click the start, the end (or type the chord), then the bulge (or type it); "12s" sets the
/// segments. Started at an edge's end, the bulge snaps where the arc is tangent to that edge (cyan). With its ends on
/// two edges of a corner, it snaps tangent to both (magenta) and rounds the corner off; double-clicking another
/// corner then rounds it with the same radius.</summary>
public sealed class ArcTool : DrawingTool
{
    private static int _segments = Shapes.DefaultArcSegments;
    private static double _filletRadius;

    private Vec3? _start;
    private Vec3? _end;
    private Vec3? _tangent;
    private Edge? _startEdge;
    private Edge? _endEdge;
    private bool _filletSnap;
    private ulong _lastClickMs;
    private Vector2 _lastClickAt;

    public override int CommandId => CommandIds.Arc2Point;
    public override string CursorImage => "arc1";
    protected override Vec3? From => _end == null ? _start : null;
    public override string VcbLabel => _end == null ? "Length" : "Bulge";
    public override string StatusText => _start == null ? "Click to set first endpoint." : _end == null ? "Click to set second endpoint or enter length." : "Click to finish.";

    public override string VcbValue => (_start, _end, Current) switch
    {
        ({ } s, null, { } c) => Length.Format(s.DistanceTo(c.Point), LengthUnit.Millimeters, 1),
        ({ } s, { } e, { } c) => Length.Format(Math.Abs(Arc(s, e, c.Point).Bulge), LengthUnit.Millimeters, 1),
        _ => "",
    };

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 450 && position.DistanceTo(_lastClickAt) < 6;
        (_lastClickMs, _lastClickAt) = (now, position);
        if (doubleClick && _start != null && _end == null && _filletRadius > 0 && RoundCorner(inf))
            return;
        if (_start == null)
        {
            _start = inf.Point;
            _tangent = TangentAt(inf);
            _startEdge = ContextEdge(inf);
        }
        else if (_end == null)
        {
            _end = inf.Point;
            _endEdge = ContextEdge(inf);
        }
        else
        {
            var (bulge, dir, _) = Arc(_start.Value, _end.Value, inf.Point);
            if (_filletSnap && FilletCorner() is { } fillet)
                Round(fillet.Corner, fillet.Radius);
            else
                Create(bulge, dir);
        }
        RefreshStatus();
    }

    /// <summary>The edge of the open context the inference lies on, if any.</summary>
    private Edge? ContextEdge(InferenceResult inf) =>
        inf is { Kind: InferenceKind.OnEdge or InferenceKind.Midpoint, Edge: { } e } && View.Document is { } doc && doc.Context.Entities.Edges.Contains(e) ? e : null;

    /// <summary>
    /// The corner the arc can round off: its ends on the two edges of one corner, as far from it as each other.
    /// Returns the corner, the fillet's radius and where its middle is (world).
    /// </summary>
    private (Vertex Corner, double Radius, Vec3 Apex, Vec3 Side, double Bulge)? FilletCorner()
    {
        if (_start is not { } s || _end is not { } e || _startEdge is not { } a || _endEdge is not { } b || a == b || View.Document is not { } doc)
            return null;
        var shared = new[] { a.Start, a.End }.Intersect([b.Start, b.End]).FirstOrDefault();
        if (shared == null || Fillet.Corner(doc.Context.Entities, shared) is not var (_, _, da, db))
            return null;
        var toWorld = doc.Context.ToWorld;
        var corner = toWorld.ApplyPoint(shared.Position);
        var (ds, de) = (s.DistanceTo(corner), e.DistanceTo(corner));
        if (Math.Abs(ds - de) > Math.Max(ds, de) * 0.02 || ds <= Tolerance.Length)
            return null;
        // Angles survive the context's (uniform) scale, so the radius is worked out in world units.
        var radius = Fillet.RadiusFor(da, db, (ds + de) / 2);
        var half = Math.Acos(Math.Clamp(da.Dot(db), -1, 1)) / 2;
        var bulge = radius * (1 - Math.Cos(Math.PI / 2 - half));
        var mid = (s + e) * 0.5;
        var side = (corner - mid).Normalized();
        return (shared, radius / toWorld.ApplyVector(da).Length, mid + side * bulge, side, bulge);
    }

    private void Round(Vertex corner, double radius)
    {
        if (View.Document is not { } doc)
            return;
        var ok = false;
        doc.Operation("Fillet", e => ok = Fillet.Apply(e, corner, radius, _segments));
        if (ok)
            _filletRadius = radius;
        Reset();
    }

    /// <summary>Double-click on a corner: rounds it with the last fillet's radius.</summary>
    private bool RoundCorner(InferenceResult inf)
    {
        if (View.Document is not { } doc)
            return false;
        var local = doc.Context.ToWorld.Inverse().ApplyPoint(inf.Point);
        var v = doc.Context.Entities.Vertices.FirstOrDefault(x => x.Position.DistanceTo(local) <= Tolerance.Length);
        if (v == null || Fillet.Corner(doc.Context.Entities, v) == null)
            return false;
        Round(v, _filletRadius);
        return true;
    }

    private void Reset()
    {
        _tangent = null;
        _start = null;
        _end = null;
        _startEdge = null;
        _endEdge = null;
        _filletSnap = false;
        ResetLocks();
        RefreshStatus();
        UpdateInference();
        View.QueueOverlayRedraw();
    }

    /// <summary>Signed distance of <paramref name="p"/> from the chord, measured in the drawing plane.</summary>
    private double Bulge(Vec3 s, Vec3 e, Vec3 p)
    {
        var chord = (e - s).Normalized();
        var toP = p - (s + e) * 0.5;
        var perp = toP - chord * toP.Dot(chord);
        return perp.Length;
    }

    /// <summary>The direction an edge arrives at the endpoint <paramref name="inf"/> snapped to, if it did.</summary>
    private Vec3? TangentAt(InferenceResult inf)
    {
        if (inf is not { Kind: InferenceKind.Endpoint, Edge: { } edge } || View.Document is not { } doc)
            return null;
        var xf = doc.Context.Entities.Edges.Contains(edge) ? doc.Context.ToWorld : inf.EntityToWorld;
        var (a, b) = (xf.ApplyPoint(edge.Start.Position), xf.ApplyPoint(edge.End.Position));
        var arriving = a.DistanceTo(inf.Point) < b.DistanceTo(inf.Point) ? a - b : b - a;
        return arriving.IsZero(1e-9) ? null : arriving.Normalized();
    }

    /// <summary>The arc's bulge and its side for the cursor at <paramref name="p"/>; near the tangent arc it snaps to it.</summary>
    private (double Bulge, Vec3 Direction, bool Tangent) Arc(Vec3 s, Vec3 e, Vec3 p)
    {
        _filletSnap = false;
        if (FilletCorner() is { } fillet && View.ToScreen(fillet.Apex) is { } filletApex && View.ToScreen(p) is { } cursor && filletApex.DistanceTo(cursor) < 10)
        {
            _filletSnap = true;
            return (fillet.Bulge, fillet.Side, true);
        }
        if (_tangent is { } t && Shapes.TangentArc(s, e, t) is var (bulge, side))
        {
            var apex = (s + e) * 0.5 + side * bulge;
            if (View.ToScreen(apex) is { } a && View.ToScreen(p) is { } c && a.DistanceTo(c) < 10)
                return (bulge, side, true);
        }
        return (Bulge(s, e, p), BulgeDirection(s, e, p), false);
    }

    private Vec3 BulgeDirection(Vec3 s, Vec3 e, Vec3 p)
    {
        var chord = (e - s).Normalized();
        var toP = p - (s + e) * 0.5;
        var perp = toP - chord * toP.Dot(chord);
        if (!perp.IsZero(1e-9))
            return perp.Normalized();
        // Fall back to the axis plane facing the viewer.
        return MostFacingPlane().Cross(chord).Normalized();
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith('s') && int.TryParse(t[..^1], out var n))
        {
            if (n < 1 || n > 999)
                return false;
            _segments = n;
            return true;
        }
        if (!Length.TryParse(t, LengthUnit.Millimeters, out var mm) || Current is not { } c)
            return false;
        if (_start is { } s && _end == null)
        {
            var dir = (c.Point - s).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            _end = s + dir * mm;
            RefreshStatus();
            return true;
        }
        if (_start is { } s2 && _end is { } e2)
        {
            Create(mm, BulgeDirection(s2, e2, c.Point));
            return true;
        }
        return false;
    }

    private void Create(double bulge, Vec3 direction)
    {
        if (_start is not { } s || _end is not { } e || View.Document is not { } doc)
            return;
        var pts = Shapes.TwoPointArc(s, e, direction, bulge, _segments);
        var toLocal = doc.Context.ToWorld.Inverse();
        var local = pts.Select(toLocal.ApplyPoint).ToList();
        var curve = new Curve { Segments = _segments };
        doc.Operation("Arc", ent => StickyGeometry.DrawEdges(ent, local, closed: false, curve: curve));
        Reset();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _start != null)
        {
            _start = null;
            _end = null;
            ResetLocks();
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_start is { } s && Current is { } c)
        {
            if (_end is { } e)
            {
                var (bulge, dir, tangent) = Arc(s, e, c.Point);
                var pts = Shapes.TwoPointArc(s, e, dir, bulge, _segments);
                var color = tangent ? UI.AppPreferences.Current.Tangent : Colors.Black;
                for (var i = 0; i + 1 < pts.Count; i++)
                    DrawWorldLine(overlay, pts[i], pts[i + 1], color, tangent ? 2.5f : 1.5f);
                DrawWorldLine(overlay, s, e, Colors.Black, 1, dashed: true);
                if (tangent && View.ToScreen(pts[pts.Count / 2]) is { } apex)
                {
                    DrawTooltip(overlay, apex, "Tangent to Edge");
                    return;
                }
            }
            else
            {
                DrawWorldLine(overlay, s, c.Point, AxisColor(c.Kind == InferenceKind.OnAxis ? c.AxisDirection : null), 1.5f);
            }
        }
        DrawInference(overlay);
    }
}

using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Offset: click a face (or pre-select connected coplanar edges), move in or out to set the distance (or
/// type it), click to draw the offset. Overlapping parts are trimmed unless Alt allows them; double-click a face to
/// repeat the last distance. The new edges split the face, ready for Push/Pull.
/// </summary>
public sealed class OffsetTool : DrawingTool
{
    private static double _lastDistance;
    private static bool _allowOverlap;

    /// <summary>What is offset: a face's loops, or a chain of selected edges (closed chains act as one loop).</summary>
    private sealed record Source(List<List<Vec3>> Loops, List<Vec3>? Chain, Vec3 Normal);

    private Source? _source;
    private Face? _hover;
    private double _distance;
    private ulong _lastClickMs;

    public override int CommandId => CommandIds.Offset;
    public override string CursorImage => "offset";
    public override string VcbLabel => "Distance";
    public override string StatusText => (_source == null ? "Select face or edges to offset." : "Pick point to define offset or enter value.")
        + (_allowOverlap ? "  Alt = Trim overlap." : "  Alt = Allow overlap.");
    public override string VcbValue => _source != null ? UI.Measure.Show(_distance) : "";

    public override void Activate()
    {
        base.Activate();
        _source = SelectedEdges();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_source == null)
            _hover = FaceUnderCursor(position);
        else
            _distance = DistanceFromCursor(_source);
        View.ShowVcbValue(VcbValue);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var now = Time.GetTicksMsec();
        var doubleClick = now - _lastClickMs < 450;
        _lastClickMs = now;
        if (_source == null || doubleClick && _lastDistance != 0 && Math.Abs(_distance) <= Tolerance.Length)
        {
            // Double-clicking a face offsets it by the last distance, as in SketchUp.
            if (doubleClick && _lastDistance != 0 && FaceUnderCursor(position) is { } again)
            {
                Create(doc, FromFace(again), _lastDistance);
                return;
            }
            _source = FaceUnderCursor(position) is { } f ? FromFace(f) : null;
            _distance = 0;
            RefreshStatus();
            return;
        }
        Create(doc, _source, _distance);
    }

    public override bool ApplyVcb(string text)
    {
        if (!UI.Measure.Read(text, out var mm) || View.Document is not { } doc)
            return false;
        var source = _source ?? (_hover != null ? FromFace(_hover) : null);
        if (source == null)
            return false;
        // Typed distances keep the side the cursor is on (inside or left = positive).
        var sign = _source != null && _distance < 0 ? -1 : 1;
        Create(doc, source, sign * Math.Abs(mm));
        return true;
    }

    /// <summary>A face's loops wound for <see cref="PolygonOffset"/>: the outline counter-clockwise, holes clockwise.</summary>
    private static Source FromFace(Face f)
    {
        var normal = f.Normal;
        var loops = f.Loops.Select((l, i) =>
        {
            var points = l.Points.ToList();
            if (Polygon.Normal(points).Dot(normal) > 0 != (i == 0))
                points.Reverse();
            return points;
        }).ToList();
        return new Source(loops, null, normal);
    }

    /// <summary>Two or more connected, coplanar selected edges in the open context, in chain order.</summary>
    private Source? SelectedEdges()
    {
        if (View.Document is not { } doc)
            return null;
        var edges = doc.Selection.Items.OfType<Edge>().Where(doc.Context.Entities.Edges.Contains).ToList();
        if (edges.Count < 2 || edges.Count != doc.Selection.Items.Count())
            return null;
        var links = edges.SelectMany(e => new[] { (V: e.Start, E: e), (V: e.End, E: e) }).GroupBy(x => x.V).ToDictionary(g => g.Key, g => g.Select(x => x.E).ToList());
        if (links.Values.Any(l => l.Count > 2))
            return null;
        var start = links.FirstOrDefault(l => l.Value.Count == 1).Key ?? edges[0].Start;
        var chain = new List<Vertex> { start };
        var used = new HashSet<Edge>();
        var at = start;
        while (links[at].FirstOrDefault(e => !used.Contains(e)) is { } next)
        {
            used.Add(next);
            at = next.Other(at);
            if (at == start)
                break;
            chain.Add(at);
        }
        if (used.Count != edges.Count)
            return null;
        var points = chain.Select(v => v.Position).ToList();
        var closed = at == start && chain.Count >= 3;
        var normal = closed ? Polygon.Normal(points) : ChainNormal(points);
        if (normal.IsZero(1e-9) || points.Any(p => Math.Abs((p - points[0]).Dot(normal)) > Tolerance.Length))
            return null;
        return closed ? new Source([points], null, normal) : new Source([], points, normal);
    }

    private static Vec3 ChainNormal(List<Vec3> points)
    {
        for (var i = 1; i + 1 < points.Count; i++)
        {
            var n = (points[i] - points[i - 1]).Cross(points[i + 1] - points[i]);
            if (!n.IsZero(1e-9))
                return n.Normalized();
        }
        return Vec3.Zero;
    }

    /// <summary>The offset outlines: closed loops (trimmed unless Alt allows overlap) or the open chain's offset.</summary>
    private static List<(List<Vec3> Points, bool Closed)> Results(Source s, double distance)
    {
        if (s.Chain is { } chain)
            return [(PolygonOffset.OffsetOpen(chain, s.Normal, distance), false)];
        return s.Loops.SelectMany(l => _allowOverlap ? [PolygonOffset.Offset(l, s.Normal, distance)] : PolygonOffset.Trimmed(l, s.Normal, distance))
            .Select(l => (l, true)).ToList();
    }

    private void Create(Document doc, Source source, double distance)
    {
        if (Math.Abs(distance) > Tolerance.Length)
        {
            var results = Results(source, distance);
            doc.Operation("Offset", e =>
            {
                foreach (var (points, closed) in results)
                    StickyGeometry.DrawEdges(e, points, closed, source.Normal);
            });
            _lastDistance = distance;
        }
        _source = null;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>Signed distance from the source to the cursor on its plane (inside a face, or left of a chain = positive).</summary>
    private double DistanceFromCursor(Source s)
    {
        var toWorld = View.Document!.Context.ToWorld;
        var normal = toWorld.ApplyNormal(s.Normal).Normalized();
        var lines = s.Chain is { } c ? [c.Select(toWorld.ApplyPoint).ToList()] : s.Loops.Select(l => l.Select(toWorld.ApplyPoint).ToList()).ToList();
        var ray = View.ScreenRay(Mouse);
        if (InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal, lines[0][0]) is not { } p)
            return _distance;
        var min = double.PositiveInfinity;
        var left = false;
        foreach (var pts in lines)
        {
            var segments = s.Chain != null ? pts.Count - 1 : pts.Count;
            for (var i = 0; i < segments; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                var t = Math.Clamp((p - a).Dot(b - a) / Math.Max((b - a).LengthSquared, 1e-12), 0, 1);
                var d = p.DistanceTo(a + (b - a) * t);
                if (d < min)
                {
                    min = d;
                    left = normal.Cross(b - a).Dot(p - a) > 0;
                }
            }
        }
        var inside = s.Chain != null ? left : Inside(p, lines, normal);
        // Local distance (contexts can be scaled).
        var scale = toWorld.ApplyVector(s.Normal.Normalized()).Length;
        return (inside ? min : -min) / scale;
    }

    /// <summary>Inside the outer loop and outside every hole.</summary>
    private static bool Inside(Vec3 p, List<List<Vec3>> loops, Vec3 normal)
    {
        var (u, v) = Polygon.PlaneAxes(normal);
        return loops.Select((l, i) => PointInPolygon((p.Dot(u), p.Dot(v)), l.Select(q => (q.Dot(u), q.Dot(v))).ToList()) == (i == 0)).All(x => x);
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
        if (key.Keycode == Key.Escape && _source != null)
        {
            _source = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        if (key.Keycode == Key.Alt && !key.Echo)
        {
            _allowOverlap = !_allowOverlap;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        var toWorld = View.Document?.Context.ToWorld ?? Transform.Identity;
        var source = _source ?? (_hover != null ? FromFace(_hover) : null);
        if (source == null)
            return;
        void Polyline(List<Vec3> points, bool closed, Color color)
        {
            var w = points.Select(toWorld.ApplyPoint).ToList();
            for (var i = 0; i + (closed ? 0 : 1) < w.Count; i++)
                DrawWorldLine(overlay, w[i], w[(i + 1) % w.Count], color, 1.5f);
        }
        foreach (var l in source.Loops)
            Polyline(l, true, new Color(0, 0, 1));
        if (source.Chain is { } chain)
            Polyline(chain, false, new Color(0, 0, 1));
        if (_source != null && Math.Abs(_distance) > Tolerance.Length)
            foreach (var (points, closed) in Results(_source, _distance))
                Polyline(points, closed, Colors.Black);
    }
}

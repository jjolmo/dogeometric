using Dogeometric.App.Viewport;
using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>Where geometry goes when it is laid on a clicked face: that face's collection and its transform to world.</summary>
internal static class SurfaceTarget
{
    public static (Entities Entities, Transform ToWorld)? Of(Document doc, PickHit hit)
    {
        var entities = hit.Path.Count > 0 ? hit.Path[^1].Definition.Entities : doc.Model.Entities;
        if (FaceOf(hit, entities) == null)
            return null;
        var toWorld = hit.Path.Aggregate(Transform.Identity, (acc, inst) => inst.Transform.Then(acc));
        return (entities, toWorld);
    }

    /// <summary>The face clicked, or one beside the edge clicked (edges win picks near them).</summary>
    public static Face? FaceOf(PickHit hit, Entities entities) =>
        hit.Face ?? (hit.Edge is { } edge ? Topology.FacesOf(entities, edge).FirstOrDefault() : null);

    /// <summary>Lays a world-space path on <paramref name="target"/> along a world direction, as one undoable step.</summary>
    public static int Lay(Document doc, string name, (Entities Entities, Transform ToWorld) target, IReadOnlyList<Vec3> worldPath, Vec3 worldDirection, bool closed)
    {
        var toLocal = target.ToWorld.Inverse();
        var path = worldPath.Select(toLocal.ApplyPoint).ToList();
        var dir = toLocal.ApplyVector(worldDirection);
        var made = 0;
        doc.Undo.Begin(name, target.Entities);
        try
        {
            foreach (var run in Sandbox.DrapePath(target.Entities, path, dir, closed))
            {
                StickyGeometry.DrawEdges(target.Entities, run);
                made++;
            }
            doc.Undo.Commit();
        }
        catch
        {
            doc.Undo.Abort();
            throw;
        }
        return made;
    }
}

/// <summary>Sandbox › Drape: with edges selected, click a surface: the edges drop straight down onto it and split its faces.</summary>
public sealed class DrapeTool : Tool
{
    private List<List<Vec3>> _paths = [];

    public override int CommandId => ExtensionIds.SandboxDrape;
    public override string CursorImage => "select";
    public override string StatusText => _paths.Count == 0 ? "Select the edges to drape first." : "Click the surface to drape the edges onto.";

    public override void Activate()
    {
        if (View.Document is not { } doc)
            return;
        var xf = doc.Context.ToWorld;
        _paths = Curviloft.Chains(doc.Selection.Items.OfType<Edge>()).Select(c => c.Select(xf.ApplyPoint).ToList()).ToList();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _paths.Count == 0 || View.Document is not { } doc || View.Pick(position) is not { } hit)
            return;
        if (SurfaceTarget.Of(doc, hit) is not { } target)
            return;
        foreach (var path in _paths)
            SurfaceTarget.Lay(doc, "Drape", target, path, -Vec3.UnitZ, closed: path.Count > 2 && path[0].DistanceTo(path[^1]) < Tolerance.Length);
        Manager.Activate(new SelectTool());
    }
}

/// <summary>Tools on Surface's shapes (new ones go last: their command ids follow this order).</summary>
public enum SurfaceShape { Line, Rectangle, Circle, Polygon, Ellipse, Parallelogram, Arc, Sector, Circle3P, Polyline, Freehand }

public static class SurfaceShapes
{
    /// <summary>The shape's name as Tools on Surface shows it.</summary>
    public static string Label(this SurfaceShape shape) => shape == SurfaceShape.Circle3P ? "Circle 3P" : shape.ToString();
}

/// <summary>
/// Tools on Surface (Fredo6): shapes drawn on a surface, flat or curved. The shape is laid out in the plane touching
/// the surface at the first click, then laid onto the faces, splitting them. Polyline ends with Return, a double
/// click or a click on its start; Freehand follows a drag.
/// </summary>
public sealed class SurfaceShapeTool(SurfaceShape shape) : Tool
{
    private static int _sides = 24;
    private readonly List<Vec3> _points = [];
    private Vec3 _normal;
    private (Entities, Transform)? _target;
    private Vec3? _hover;
    private bool _drawing;

    public override int CommandId => ExtensionIds.SurfaceShape(shape);
    public override string CursorImage => "pencil";
    private bool HasSides => shape is SurfaceShape.Circle or SurfaceShape.Polygon or SurfaceShape.Ellipse or SurfaceShape.Circle3P;
    public override string VcbLabel => HasSides ? "Sides" : "Length";
    public override string VcbValue => HasSides ? _sides.ToString() : "";

    /// <summary>Clicks the shape takes; 0 for Polyline (open-ended) and Freehand (a drag).</summary>
    private int Clicks => shape switch
    {
        SurfaceShape.Parallelogram or SurfaceShape.Arc or SurfaceShape.Sector or SurfaceShape.Circle3P => 3,
        SurfaceShape.Polyline or SurfaceShape.Freehand => 0,
        _ => 2,
    };

    private bool Closed => shape is not (SurfaceShape.Line or SurfaceShape.Arc or SurfaceShape.Polyline or SurfaceShape.Freehand);

    public override string StatusText => (_points.Count, shape) switch
    {
        (0, SurfaceShape.Freehand) => "Freehand on Surface: press and drag on the surface.",
        (0, _) => $"{shape.Label()} on Surface: click a point on the surface.",
        (_, SurfaceShape.Polyline) => "Click the next point; Return, a double click or the first point ends it.",
        (_, SurfaceShape.Freehand) => "Drag, then release.",
        (1, SurfaceShape.Circle or SurfaceShape.Polygon or SurfaceShape.Ellipse) => "Click to set the size.",
        (1, SurfaceShape.Arc or SurfaceShape.Sector) => "Click the start of the arc.",
        (2, SurfaceShape.Arc or SurfaceShape.Sector) => "Click the end of the arc.",
        (_, SurfaceShape.Circle3P) => "Click the next point on the circle.",
        _ => "Click the next corner.",
    };

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _hover = PointOnPlane(position);
        if (shape == SurfaceShape.Freehand && _drawing && _hover is { } h && View.ToScreen(_points[^1]) is { } last && last.DistanceTo(position) > 6)
            _points.Add(h);
        View.QueueOverlayRedraw();
    }

    private Vec3? PointOnPlane(Vector2 position)
    {
        if (_points.Count == 0)
            return View.Pick(position)?.Point;
        var ray = View.ScreenRay(position);
        return Dogeometric.Core.Inference.InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, _points[0]);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_points.Count == 0)
        {
            if (View.Pick(position) is not { } hit || SurfaceTarget.Of(doc, hit) is not { } target || SurfaceTarget.FaceOf(hit, target.Entities) is not { } face)
                return;
            _points.Add(hit.Point);
            _normal = target.ToWorld.ApplyNormal(face.Normal).Normalized();
            _target = target;
            _drawing = shape == SurfaceShape.Freehand;
            RefreshStatus();
            return;
        }
        if (PointOnPlane(position) is not { } p)
            return;
        if (shape == SurfaceShape.Polyline)
        {
            var closing = _points.Count > 2 && View.ToScreen(_points[0]) is { } first && first.DistanceTo(position) < 8;
            if (closing || (_points.Count > 1 && p.DistanceTo(_points[^1]) < Tolerance.Length))
            {
                Finish(doc, closing);
                return;
            }
        }
        _points.Add(p);
        if (Clicks > 0 && _points.Count >= Clicks)
            Finish(doc, Closed);
        else
            RefreshStatus();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (shape == SurfaceShape.Freehand && _drawing && View.Document is { } doc)
            Finish(doc, false);
    }

    private void Finish(Document doc, bool closed)
    {
        var outline = Outline(_points);
        if (outline.Count >= 2 && _target is { } t)
            SurfaceTarget.Lay(doc, $"{shape.Label()} on Surface", t, outline, -_normal, closed);
        Reset();
    }

    private void Reset()
    {
        _points.Clear();
        _target = null;
        _drawing = false;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>The shape in the touching plane through <paramref name="pts"/> (the clicks, maybe plus the cursor).</summary>
    private List<Vec3> Outline(IReadOnlyList<Vec3> pts)
    {
        if (pts.Count < 2)
            return [];
        var (u, v) = Polygon.PlaneAxes(_normal);
        var first = pts[0];
        var p = pts[1];
        switch (shape)
        {
            case SurfaceShape.Line:
                return [first, p];
            case SurfaceShape.Polyline or SurfaceShape.Freehand:
                return [.. pts];
            case SurfaceShape.Rectangle:
            {
                var d = p - first;
                var du = u * d.Dot(u);
                var dv = v * d.Dot(v);
                return [first, first + du, first + du + dv, first + dv];
            }
            case SurfaceShape.Ellipse:
            {
                var d = p - first;
                double a = Math.Abs(d.Dot(u)), b = Math.Abs(d.Dot(v));
                if (a < Tolerance.Length || b < Tolerance.Length)
                    return [];
                return Enumerable.Range(0, _sides).Select(i => 2 * Math.PI * i / _sides)
                    .Select(t => first + u * (a * Math.Cos(t)) + v * (b * Math.Sin(t))).ToList();
            }
            case SurfaceShape.Parallelogram:
                return pts.Count < 3 ? [first, p] : [first, p, pts[2], first + (pts[2] - p)];
            case SurfaceShape.Arc or SurfaceShape.Sector:
            {
                if (pts.Count < 3)
                    return [first, p];
                var r = first.DistanceTo(p);
                var a0 = Math.Atan2((p - first).Dot(v), (p - first).Dot(u));
                var a1 = Math.Atan2((pts[2] - first).Dot(v), (pts[2] - first).Dot(u));
                var sweep = a1 - a0;
                if (sweep <= 0)
                    sweep += 2 * Math.PI;
                var segments = Math.Max(2, (int)Math.Ceiling(_sides * sweep / (2 * Math.PI)));
                var arc = Shapes.CenterArc(first, _normal, p, sweep, segments);
                return shape == SurfaceShape.Sector && r > Tolerance.Length ? [first, .. arc] : arc;
            }
            case SurfaceShape.Circle3P:
            {
                if (pts.Count < 3)
                    return [first, p];
                return Circumcircle(first, p, pts[2]) is { } c
                    ? Shapes.RegularPolygon(c.Centre, _normal, first - c.Centre, c.Radius, _sides)
                    : [];
            }
            default:
                var radius = first.DistanceTo(p);
                return radius < Tolerance.Length ? [] : Shapes.RegularPolygon(first, _normal, p - first, radius, _sides, false);
        }
    }

    private (Vec3 Centre, double Radius)? Circumcircle(Vec3 a, Vec3 b, Vec3 c)
    {
        var (u, v) = Polygon.PlaneAxes(_normal);
        double ax = a.Dot(u), ay = a.Dot(v), bx = b.Dot(u), by = b.Dot(v), cx = c.Dot(u), cy = c.Dot(v);
        var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-12)
            return null;
        var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
        var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
        var centre = u * ux + v * uy + _normal * a.Dot(_normal);
        return (centre, centre.DistanceTo(a));
    }

    public override bool ApplyVcb(string text)
    {
        if (HasSides && int.TryParse(text.Trim().TrimEnd('s'), out var n) && n >= 3)
        {
            _sides = n;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        return false;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (_points.Count == 0)
            return false;
        if (key.Keycode is Key.Enter or Key.KpEnter && shape == SurfaceShape.Polyline && View.Document is { } doc)
        {
            Finish(doc, false);
            return true;
        }
        if (key.Keycode != Key.Escape)
            return false;
        Reset();
        return true;
    }

    public override void Draw(Control overlay)
    {
        if (_points.Count == 0)
            return;
        var pts = Outline(_hover is { } h && !_drawing ? [.. _points, h] : _points);
        var closed = Closed && (Clicks == 0 || _points.Count + 1 >= Clicks);
        for (var i = 0; i + 1 < pts.Count + (closed ? 1 : 0); i++)
            if (View.ToScreen(pts[i]) is { } a && View.ToScreen(pts[(i + 1) % pts.Count]) is { } b)
                overlay.DrawLine(a, b, new Color(0.85f, 0.1f, 0.1f), 1.5f, true);
    }
}

/// <summary>The curve under the cursor on a surface: hard edges running through points where only two meet, in
/// whatever group they lie.</summary>
internal static class SurfaceCurves
{
    public static (Entities Entities, Transform ToWorld, List<Edge> Edges)? At(Document doc, ModelViewport view, Vector2 position)
    {
        if (view.Pick(position) is not { Edge: { } edge } hit)
            return null;
        var e = hit.Path.Count > 0 ? hit.Path[^1].Definition.Entities : doc.Model.Entities;
        var toWorld = hit.Path.Aggregate(Transform.Identity, (acc, inst) => inst.Transform.Then(acc));
        bool Hard(Edge x) => (x.Flags & (EdgeFlags.Soft | EdgeFlags.Hidden)) == 0;
        var at = new Dictionary<Vertex, List<Edge>>();
        foreach (var x in e.Edges.Where(Hard))
            foreach (var v in new[] { x.Start, x.End })
            {
                if (!at.TryGetValue(v, out var list))
                    at[v] = list = [];
                list.Add(x);
            }
        var chain = new HashSet<Edge> { edge };
        var queue = new Queue<Edge>([edge]);
        while (queue.Count > 0)
        {
            var x = queue.Dequeue();
            foreach (var v in new[] { x.Start, x.End })
                if (at.TryGetValue(v, out var list) && list.Count == 2 && list.First(y => y != x) is var next && chain.Add(next))
                    queue.Enqueue(next);
        }
        return (e, toWorld, [.. chain]);
    }

    public static void Draw(ModelViewport view, Control overlay, Transform toWorld, IEnumerable<Edge> edges)
    {
        foreach (var x in edges)
            if (view.ToScreen(toWorld.ApplyPoint(x.Start.Position)) is { } a && view.ToScreen(toWorld.ApplyPoint(x.End.Position)) is { } b)
                overlay.DrawLine(a, b, new Color(0, 0, 1), 3);
    }
}

/// <summary>Tools on Surface's Offset: click a curve drawn on a surface, then move sideways (or type the distance)
/// and click; the offset curve is laid on the surface.</summary>
public sealed class SurfaceOffsetTool : Tool
{
    private (Entities Entities, Transform ToWorld, List<Edge> Edges)? _hover;
    private (Entities Entities, Transform ToWorld)? _target;
    private List<Vec3> _chain = [];
    private bool _closed;
    private Vec3 _normal;
    private double _distance;
    private double _side = 1;

    public override int CommandId => ExtensionIds.SurfaceOffset;
    public override string CursorImage => "offset";
    public override string VcbLabel => "Distance";
    public override string VcbValue => _chain.Count > 0 ? UI.Measure.Show(Math.Abs(_distance)) : "";
    public override string StatusText => _chain.Count == 0
        ? "Offset on Surface: click a curve drawn on a surface."
        : "Move to the side and click, or type the distance.";

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (View.Document is not { } doc)
            return;
        if (_chain.Count == 0)
            _hover = SurfaceCurves.At(doc, View, position);
        else if (PointOnPlane(position) is { } p)
        {
            _distance = CurveOffset.SideDistance(_chain, _closed, _normal, p);
            _side = Math.Sign(_distance) is 0 ? 1 : Math.Sign(_distance);
            View.ShowVcbValue(VcbValue);
        }
        View.QueueOverlayRedraw();
    }

    private Vec3? PointOnPlane(Vector2 position)
    {
        var ray = View.ScreenRay(position);
        var centre = _chain.Aggregate(Vec3.Zero, (a, q) => a + q) / _chain.Count;
        return Dogeometric.Core.Inference.InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, centre);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        if (_chain.Count == 0)
        {
            if (SurfaceCurves.At(doc, View, position) is not { } curve)
                return;
            var points = Curviloft.Chains(curve.Edges).OrderByDescending(c => c.Count).First();
            _closed = points.Count > 3 && points[0].DistanceTo(points[^1]) < Tolerance.Length;
            if (_closed)
                points.RemoveAt(points.Count - 1);
            _chain = points.Select(curve.ToWorld.ApplyPoint).ToList();
            var faces = curve.Edges.SelectMany(x => Topology.FacesOf(curve.Entities, x)).Distinct().ToList();
            var n = faces.Aggregate(Vec3.Zero, (a, f) => a + f.Normal.Normalized());
            if (n.IsZero(1e-9))
            {
                _chain = [];
                return;
            }
            _normal = curve.ToWorld.ApplyNormal(n).Normalized();
            _target = (curve.Entities, curve.ToWorld);
            _hover = null;
            RefreshStatus();
            return;
        }
        Finish(doc, _distance);
    }

    private void Finish(Document doc, double distance)
    {
        if (Math.Abs(distance) > Tolerance.Length && _target is { } t)
            SurfaceTarget.Lay(doc, "Offset on Surface", t, CurveOffset.Offset(_chain, _closed, _normal, distance), -_normal, _closed);
        _chain = [];
        _target = null;
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (_chain.Count == 0 || View.Document is not { } doc || !UI.Measure.Read(text, out var mm))
            return false;
        Finish(doc, Math.Abs(mm) * _side);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode != Key.Escape || _chain.Count == 0)
            return false;
        _chain = [];
        RefreshStatus();
        View.QueueOverlayRedraw();
        return true;
    }

    public override void Draw(Control overlay)
    {
        if (_chain.Count == 0)
        {
            if (_hover is { } h)
                SurfaceCurves.Draw(View, overlay, h.ToWorld, h.Edges);
            return;
        }
        var pts = CurveOffset.Offset(_chain, _closed, _normal, _distance);
        for (var i = 0; i + 1 < pts.Count + (_closed ? 1 : 0); i++)
            if (View.ToScreen(pts[i]) is { } a && View.ToScreen(pts[(i + 1) % pts.Count]) is { } b)
                overlay.DrawLine(a, b, new Color(0.85f, 0.1f, 0.1f), 1.5f, true);
    }
}

/// <summary>Tools on Surface's Eraser: a click erases the whole curve under the cursor (hard edges running through
/// points where only two meet), and the faces either side heal back into the surface.</summary>
public sealed class SurfaceEraserTool : Tool
{
    private List<Edge> _hover = [];

    public override int CommandId => ExtensionIds.SurfaceEraser;
    public override string CursorImage => "eraser";
    public override string StatusText => "Eraser on Surface: click a curve drawn on a surface to erase it.";

    private Entities? _entities;
    private Transform _toWorld = Transform.Identity;

    private List<Edge> CurveAt(Vector2 position)
    {
        if (View.Document is not { } doc || SurfaceCurves.At(doc, View, position) is not { } curve)
            return [];
        (_entities, _toWorld) = (curve.Entities, curve.ToWorld);
        return curve.Edges;
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _hover = CurveAt(position);
        View.QueueOverlayRedraw();
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || View.Document is not { } doc)
            return;
        var curve = CurveAt(position);
        if (curve.Count == 0 || _entities is not { } entities)
            return;
        doc.Selection.Remove(curve);
        doc.Undo.Begin("Erase on Surface", entities);
        Editing.Erase(entities, curve);
        doc.Undo.Commit();
        _hover = [];
        View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        SurfaceCurves.Draw(View, overlay, _toWorld, _hover);
    }
}

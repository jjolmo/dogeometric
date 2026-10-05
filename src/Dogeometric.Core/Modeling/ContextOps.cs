using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>The right-click menu's operations on curves, faces and instances that SketchUp offers by entity kind.</summary>
public static class ContextOps
{
    /// <summary>Explode Curve: the curve's edges become plain edges.</summary>
    public static int ExplodeCurves(IEnumerable<Edge> edges)
    {
        var curves = edges.Select(e => e.Curve).OfType<Curve>().ToHashSet();
        var count = 0;
        foreach (var e in edges.ToList())
            if (e.Curve != null && curves.Contains(e.Curve))
            {
                e.Curve = null;
                count++;
            }
        return count;
    }

    /// <summary>Weld Edges: a chain of connected edges becomes one curve (a polyline curve, as SketchUp makes).</summary>
    public static Curve? Weld(IReadOnlyCollection<Edge> edges)
    {
        if (edges.Count < 2)
            return null;
        // Connected: walking shared vertices from one edge reaches them all.
        var byVertex = edges.SelectMany(e => new[] { (e.Start, e), (e.End, e) }).ToLookup(x => x.Item1, x => x.e);
        var seen = new HashSet<Edge> { edges.First() };
        var queue = new Queue<Edge>(seen);
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            foreach (var v in new[] { e.Start, e.End })
                foreach (var next in byVertex[v].Where(seen.Add))
                    queue.Enqueue(next);
        }
        // A curve is one chain: no vertex may join three of its edges.
        if (seen.Count != edges.Count || byVertex.Any(g => g.Count() > 2))
            return null;
        var curve = new Curve { Segments = edges.Count };
        foreach (var e in edges)
            e.Curve = curve;
        return curve;
    }

    /// <summary>Convert to Polygon: a circle's or arc's edges keep their curve but extrude with hard edges.</summary>
    public static int ToPolygon(IEnumerable<Edge> edges)
    {
        var curves = edges.Select(e => e.Curve).OfType<Curve>().Where(c => c.Radius > 0).ToHashSet();
        foreach (var c in curves)
            c.IsPolygon = true;
        return curves.Count;
    }

    /// <summary>Find Center of an arc or circle: a guide point at its centre (in the curve's own coordinates).</summary>
    public static GuidePoint? FindCenter(Entities e, Edge edge)
    {
        if (edge.Curve is not { Radius: > 0 } curve)
            return null;
        var g = new GuidePoint(curve.Center);
        e.GuidePoints.Add(g);
        return g;
    }

    /// <summary>Area › Selection / Tag / Material: the summed area of faces in the open context, in square millimetres.</summary>
    public static double Area(IEnumerable<Face> faces) => faces.Sum(f => f.Area);

    /// <summary>Reset Scale: the instance keeps its place and orientation but loses any scaling.</summary>
    public static void ResetScale(ComponentInstance inst)
    {
        var t = inst.Transform;
        inst.Transform = new Transform(t.X.Normalized(), t.Y.Normalized(), t.Z.Normalized(), t.Origin);
    }

    /// <summary>Reset Skew: the instance's axes become square to each other again, the red one leading.</summary>
    public static void ResetSkew(ComponentInstance inst)
    {
        var t = inst.Transform;
        var x = t.X.Normalized();
        var y = (t.Y - x * t.Y.Dot(x)).Normalized();
        var z = x.Cross(y);
        if (z.Dot(t.Z) < 0)
            z = -z;
        inst.Transform = new Transform(x * t.X.Length, y * t.Y.Length, z * t.Z.Length, t.Origin);
    }

    /// <summary>Scale Definition: the instance's scale is moved into its definition, so it reads as unscaled while
    /// every copy changes size with it.</summary>
    public static void ScaleDefinition(ComponentInstance inst)
    {
        var t = inst.Transform;
        var (sx, sy, sz) = (t.X.Length, t.Y.Length, t.Z.Length);
        foreach (var v in inst.Definition.Entities.Vertices)
            v.Position = new Vec3(v.Position.X * sx, v.Position.Y * sy, v.Position.Z * sz);
        foreach (var child in inst.Definition.Entities.Instances)
            child.Transform = child.Transform.Then(new Transform(Vec3.UnitX * sx, Vec3.UnitY * sy, Vec3.UnitZ * sz, Vec3.Zero));
        inst.Transform = new Transform(t.X / sx, t.Y / sy, t.Z / sz, t.Origin);
    }
}

/// <summary>Entity Info's curve fields: an arc's or circle's radius and segments, changed by redrawing it.</summary>
public static class Curves
{
    /// <summary>The curve's points in order along it (closed curves don't repeat their first point).</summary>
    public static List<Vec3> Points(Entities e, Curve curve, out bool closed)
    {
        var edges = e.Edges.Where(x => x.Curve == curve).ToList();
        closed = false;
        if (edges.Count == 0)
            return [];
        var byVertex = edges.SelectMany(x => new[] { (x.Start, x), (x.End, x) }).ToLookup(p => p.Item1, p => p.x);
        // An open curve starts at a vertex with one of its edges; a closed one anywhere.
        var start = edges.SelectMany(x => new[] { x.Start, x.End }).FirstOrDefault(v => byVertex[v].Count() == 1);
        closed = start == null;
        start ??= edges[0].Start;
        var points = new List<Vec3> { start.Position };
        var (at, used) = (start, new HashSet<Edge>());
        while (byVertex[at].FirstOrDefault(x => !used.Contains(x)) is { } next)
        {
            used.Add(next);
            at = next.Start == at ? next.End : next.Start;
            if (at == start)
                break;
            points.Add(at.Position);
        }
        return points;
    }

    /// <summary>Redraws an arc or circle with <paramref name="segments"/> segments and <paramref name="radius"/>,
    /// keeping its centre, plane, start and sweep. Returns the new edges, or null when it isn't an arc or circle.</summary>
    public static List<Edge>? Redraw(Entities e, Curve curve, int segments, double radius)
    {
        if (curve.Radius <= 0 || segments < 3 && radius <= 0)
            return null;
        var pts = Points(e, curve, out var closed);
        if (pts.Count < 2)
            return null;
        var n = curve.Normal.Normalized();
        var from = (pts[0] - curve.Center).Normalized();
        var start = curve.Center + from * radius;
        double sweep;
        if (closed)
            sweep = 2 * Math.PI;
        else
        {
            var to = (pts[^1] - curve.Center).Normalized();
            sweep = Math.Atan2(from.Cross(to).Dot(n), from.Dot(to));
            // The middle point says which way round the arc goes.
            var mid = (pts[pts.Count / 2] - curve.Center).Normalized();
            var midAngle = Math.Atan2(from.Cross(mid).Dot(n), from.Dot(mid));
            if (sweep > 0 && (midAngle < 0 || midAngle > sweep))
                sweep -= 2 * Math.PI;
            else if (sweep < 0 && (midAngle > 0 || midAngle < sweep))
                sweep += 2 * Math.PI;
        }
        var count = closed ? Math.Max(3, segments) : Math.Max(1, (int)Math.Round(segments * Math.Abs(sweep) / (2 * Math.PI)));
        var points = Shapes.CenterArc(curve.Center, n, start, sweep, count);
        if (closed)
            points.RemoveAt(points.Count - 1);
        Editing.Erase(e, e.Edges.Where(x => x.Curve == curve).Cast<object>().ToList());
        var fresh = new Curve { Center = curve.Center, Normal = n, Radius = radius, Segments = segments, IsPolygon = curve.IsPolygon };
        return StickyGeometry.DrawEdges(e, points, closed, n, fresh);
    }
}

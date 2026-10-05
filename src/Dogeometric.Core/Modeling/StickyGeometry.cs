using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's "sticky" loose geometry: new edges merge with existing vertices, split edges they touch or cross,
/// and closed coplanar loops become faces (see <see cref="FaceFinder"/>).
/// </summary>
public static class StickyGeometry
{
    /// <summary>
    /// Draws a chain of edges through <paramref name="points"/> (closing it when <paramref name="closed"/>), then
    /// creates the faces they close. Returns the edges that now cover the drawn segments.
    /// </summary>
    public static List<Edge> DrawEdges(Entities e, IReadOnlyList<Vec3> points, bool closed = false, Vec3? preferredNormal = null, Curve? curve = null, bool fill = true)
    {
        var created = new List<Edge>();
        var count = closed ? points.Count : points.Count - 1;
        for (var i = 0; i < count; i++)
            created.AddRange(AddSegment(e, points[i], points[(i + 1) % points.Count]));
        if (curve != null)
            foreach (var edge in created)
                edge.Curve ??= curve;
        if (fill)
            FaceFinder.Update(e, created, preferredNormal);
        return created;
    }

    /// <summary>
    /// Adds the segment a–b, merged into the existing geometry: shared vertices, split existing edges at the
    /// segment's ends and at crossings, and the segment itself split wherever existing vertices lie on it.
    /// </summary>
    public static List<Edge> AddSegment(Entities e, Vec3 a, Vec3 b)
    {
        if (a.DistanceTo(b) <= Tolerance.Length)
            return [];

        // Points along a–b where the new edge must have a vertex: its ends, existing vertices on it, crossings.
        var cuts = new List<(double T, Vec3 P)> { (0, a), (1, b) };
        var dir = b - a;
        var len2 = dir.LengthSquared;

        foreach (var v in e.Vertices)
        {
            var t = (v.Position - a).Dot(dir) / len2;
            if (t <= 0 || t >= 1)
                continue;
            if ((a + dir * t).DistanceTo(v.Position) <= Tolerance.Length)
                cuts.Add((t, v.Position));
        }

        foreach (var edge in e.Edges.ToList())
        {
            if (SegmentIntersection(a, b, edge.Start.Position, edge.End.Position) is { } hit)
            {
                var (t, p, s) = hit;
                // Split the existing edge where the new one touches or crosses its interior.
                if (s > 0 && s < 1 && p.DistanceTo(edge.Start.Position) > Tolerance.Length && p.DistanceTo(edge.End.Position) > Tolerance.Length)
                    SplitEdge(e, edge, e.VertexAt(p));
                if (t > 0 && t < 1)
                    cuts.Add((t, p));
            }
        }

        var ordered = cuts.OrderBy(c => c.T).ToList();
        var result = new List<Edge>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var p = ordered[i].P;
            var q = ordered[i + 1].P;
            if (p.DistanceTo(q) <= Tolerance.Length)
                continue;
            var va = e.VertexAt(p);
            var vb = e.VertexAt(q);
            if (va == vb)
                continue;
            result.Add(e.EdgeBetween(va, vb));
        }
        return result;
    }

    /// <summary>
    /// Splits <paramref name="edge"/> at <paramref name="v"/> (which must lie on it): the edge keeps its start and
    /// a new edge runs from <paramref name="v"/> to the old end. Faces using the edge get both pieces in order.
    /// </summary>
    public static Edge SplitEdge(Entities e, Edge edge, Vertex v)
    {
        var tail = new Edge(v, edge.End) { Flags = edge.Flags, Crease = edge.Crease, Tag = edge.Tag, Material = edge.Material, Curve = edge.Curve };
        edge.End = v;
        e.Edges.Add(tail);
        foreach (var face in e.Faces)
        {
            foreach (var loop in face.Loops)
            {
                for (var i = 0; i < loop.Edges.Count; i++)
                {
                    if (loop.Edges[i].Edge != edge)
                        continue;
                    var reversed = loop.Edges[i].Reversed;
                    // Forward use: start→v then v→end. Reversed use: end→v (tail reversed) then v→start.
                    if (!reversed)
                        loop.Edges.Insert(i + 1, (tail, false));
                    else
                        loop.Edges.Insert(i, (tail, true));
                    i++;
                }
            }
        }
        return tail;
    }

    /// <summary>
    /// Where segments p0–p1 and q0–q1 meet (within tolerance): parameters along each and the meeting point.
    /// Null when they miss or are parallel.
    /// </summary>
    public static (double T, Vec3 Point, double S)? SegmentIntersection(Vec3 p0, Vec3 p1, Vec3 q0, Vec3 q1)
    {
        var d1 = p1 - p0;
        var d2 = q1 - q0;
        var r = p0 - q0;
        double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), c = d1.Dot(r), b = d1.Dot(d2);
        var denom = a * e - b * b;
        if (a < 1e-18 || e < 1e-18 || denom <= 1e-12 * a * e)
            return null; // degenerate or parallel
        var t = (b * f - c * e) / denom;
        var s = (a * f - b * c) / denom;
        if (t < -1e-9 || t > 1 + 1e-9 || s < -1e-9 || s > 1 + 1e-9)
            return null;
        t = Math.Clamp(t, 0, 1);
        s = Math.Clamp(s, 0, 1);
        var pp = p0 + d1 * t;
        var qq = q0 + d2 * s;
        return pp.DistanceTo(qq) <= Tolerance.Length ? (t, (pp + qq) * 0.5, s) : null;
    }
}

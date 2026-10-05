using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Sandbox tools: terrain meshes from scratch (a grid) or from contour lines (Delaunay), Smoove to raise
/// and lower them smoothly, Add Detail to split triangles and Flip Edge to turn a diagonal.
/// </summary>
public static class Sandbox
{
    /// <summary>From Scratch: a flat grid of <paramref name="spacing"/> cells over the rectangle, quads split in two.</summary>
    public static void Grid(Entities e, Vec3 origin, Vec3 xAxis, double width, Vec3 yAxis, double depth, double spacing)
    {
        var nx = Math.Max(1, (int)Math.Round(width / spacing));
        var ny = Math.Max(1, (int)Math.Round(depth / spacing));
        var x = xAxis.Normalized() * (width / nx);
        var y = yAxis.Normalized() * (depth / ny);
        var weld = new Welder(e);
        var up = x.Cross(y).Normalized();
        var faces = new List<Face>();
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                var a = origin + x * i + y * j;
                faces.Add(Up(weld.Face([a, a + x, a + x + y], []), up));
                faces.Add(Up(weld.Face([a, a + x + y, a + y], []), up));
            }
        SoftenDiagonals(e, faces);
    }

    /// <summary>From Contours: a triangulated surface through every vertex of the contour edges (Delaunay seen from above).</summary>
    public static int FromContours(Entities e, IEnumerable<Edge> contours)
    {
        var points = contours.SelectMany(x => new[] { x.Start.Position, x.End.Position })
            .GroupBy(p => (Math.Round(p.X, 3), Math.Round(p.Y, 3))).Select(g => g.First()).ToList();
        if (points.Count < 3)
            return 0;
        var triangles = Delaunay(points);
        var weld = new Welder(e);
        var faces = new List<Face>();
        foreach (var (a, b, c) in triangles)
        {
            var pa = points[a];
            var pb = points[b];
            var pc = points[c];
            // Points in a line seen from above (contours sharing a radius) make flat slivers: none of the surface.
            if (Math.Abs((pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X)) < 1e-9)
                continue;
            faces.Add(Up(weld.Face([pa, pb, pc], []), Vec3.UnitZ));
        }
        foreach (var edge in faces.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).Count() == 2)
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        return faces.Count;
    }

    /// <summary>Bowyer-Watson on the XY projection; returns index triples, counter-clockwise from above.</summary>
    public static List<(int, int, int)> Delaunay(IReadOnlyList<Vec3> points)
    {
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var size = Math.Max(maxX - minX, maxY - minY) * 20 + 1;
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        // A tiny, fixed nudge per point breaks ties between points on one circle (concentric contours), which
        // otherwise leave the in-circle test undecided and the triangulation overlapping.
        var nudge = Math.Max(maxX - minX, maxY - minY) * 1e-9;
        var pts = points.Select((p, i) => (p.X + nudge * Math.Sin(i * 12.9898), p.Y + nudge * Math.Cos(i * 78.233))).ToList();
        pts.Add((cx - size, cy - size));
        pts.Add((cx + size, cy - size));
        pts.Add((cx, cy + size));
        var n = points.Count;
        var tris = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };
        for (var i = 0; i < n; i++)
        {
            var (px, py) = pts[i];
            var bad = tris.Where(t => InCircle(pts[t.A], pts[t.B], pts[t.C], px, py)).ToList();
            var edges = new Dictionary<(int, int), int>();
            foreach (var t in bad)
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var key = u < v ? (u, v) : (v, u);
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
            tris.RemoveAll(bad.Contains);
            foreach (var t in bad)
                foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                    if (edges[u < v ? (u, v) : (v, u)] == 1)
                        tris.Add(Ccw(pts, u, v, i));
        }
        return tris.Where(t => t.A < n && t.B < n && t.C < n).Select(t => (t.A, t.B, t.C)).ToList();
    }

    private static (int, int, int) Ccw(List<(double X, double Y)> p, int a, int b, int c)
    {
        var cross = (p[b].X - p[a].X) * (p[c].Y - p[a].Y) - (p[b].Y - p[a].Y) * (p[c].X - p[a].X);
        return cross >= 0 ? (a, b, c) : (a, c, b);
    }

    private static bool InCircle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, double px, double py)
    {
        // Assumes a, b, c counter-clockwise (kept so by Ccw).
        double ax = a.X - px, ay = a.Y - py, bx = b.X - px, by = b.Y - py, cx = c.X - px, cy = c.Y - py;
        var det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
        return det > 0;
    }

    /// <summary>
    /// Smoove: moves the vertices within <paramref name="radius"/> of <paramref name="centre"/> (measured across the
    /// ground) along <paramref name="direction"/> by up to <paramref name="height"/>, with a smooth fall-off.
    /// </summary>
    public static int Smoove(Entities e, Vec3 centre, double radius, Vec3 direction, double height)
    {
        var dir = direction.Normalized();
        var moved = 0;
        foreach (var v in e.Vertices)
        {
            var offset = v.Position - centre;
            var across = offset - dir * offset.Dot(dir);
            var d = across.Length;
            if (d >= radius)
                continue;
            var weight = 0.5 * (1 + Math.Cos(Math.PI * d / radius));
            v.Position += dir * (height * weight);
            moved++;
        }
        return moved;
    }

    /// <summary>Stamp: the terrain under <paramref name="footprint"/> (seen from above) flattens to <paramref name="baseHeight"/>,
    /// sloping smoothly back within <paramref name="offset"/>. Returns how many vertices moved.</summary>
    public static int Stamp(Entities e, IReadOnlyList<Vec3> footprint, double baseHeight, double offset)
    {
        var outline = footprint.Select(p => new Vec3(p.X, p.Y, baseHeight)).ToList();
        if (outline.Count < 3)
            return 0;
        if (Polygon.Normal(outline).Z < 0)
            outline.Reverse();
        var ring = CurveOffset.Offset(outline, closed: true, Vec3.UnitZ, offset);
        foreach (var loop in new[] { outline, ring })
            foreach (var run in DrapePath(e, loop, -Vec3.UnitZ, closed: true))
                StickyGeometry.DrawEdges(e, run);

        var flat = outline.Select(p => (p.X, p.Y)).ToArray();
        var moved = new List<Vertex>();
        foreach (var v in e.Vertices)
        {
            var q = (v.Position.X, v.Position.Y);
            var d = Inside(q, flat) ? 0 : DistanceToOutline(q, flat);
            if (d >= offset)
                continue;
            var t = offset <= 0 ? 0 : d / offset;
            var weight = t * t * (3 - 2 * t);
            v.Position = new Vec3(v.Position.X, v.Position.Y, baseHeight + (v.Position.Z - baseHeight) * weight);
            moved.Add(v);
        }
        var touched = e.Faces.Where(f => f.OuterLoop.Vertices.Any(moved.Contains)).ToList();
        foreach (var f in touched)
            FredoScale.SplitIfBent(e, f);
        return moved.Count;
    }

    private static bool Inside((double X, double Y) q, (double X, double Y)[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) &&
                q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private static double DistanceToOutline((double X, double Y) q, (double X, double Y)[] poly)
    {
        var best = double.MaxValue;
        for (var i = 0; i < poly.Length; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Length]);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var t = Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / Math.Max(dx * dx + dy * dy, 1e-12), 0, 1);
            var (px, py) = (a.X + dx * t - q.X, a.Y + dy * t - q.Y);
            best = Math.Min(best, Math.Sqrt(px * px + py * py));
        }
        return best;
    }

    /// <summary>Add Detail: each triangle splits in four at its edge midpoints (neighbours split along the same edges).</summary>
    public static int AddDetail(Entities e, IReadOnlyCollection<Face> faces) => SplitTriangles(e, faces);

    private static int SplitTriangles(Entities e, IReadOnlyCollection<Face> faces)
    {
        var set = faces.Where(e.Faces.Contains).ToList();
        var before = e.Faces.Count;
        var weld = new Welder(e);
        var mid = new Dictionary<Edge, Vec3>();
        foreach (var edge in set.SelectMany(Topology.EdgesOf).Distinct())
            mid[edge] = (edge.Start.Position + edge.End.Position) / 2;
        var made = new List<(List<Vec3> Pts, Face Src)>();
        foreach (var f in set)
        {
            var corners = f.OuterLoop.Vertices.Select(v => v.Position).ToList();
            var edges = f.OuterLoop.Edges.Select(x => x.Edge).ToList();
            if (corners.Count != 3 || f.Loops.Count != 1)
                continue;
            var m = edges.Select(x => mid[x]).ToList();
            made.Add(([corners[0], m[0], m[2]], f));
            made.Add(([corners[1], m[1], m[0]], f));
            made.Add(([corners[2], m[2], m[1]], f));
            made.Add(([m[0], m[1], m[2]], f));
        }
        var replaced = made.Select(x => x.Src).ToHashSet();
        // Faces next to the split ones get the midpoints in their outlines, so no gaps open.
        foreach (var other in e.Faces.Where(f => !replaced.Contains(f)).ToList())
            foreach (var loop in other.Loops)
                for (var i = loop.Edges.Count - 1; i >= 0; i--)
                {
                    var (edge, rev) = loop.Edges[i];
                    if (!mid.TryGetValue(edge, out var m) || !Topology.FacesOf(e, edge).Any(replaced.Contains))
                        continue;
                    var from = weld.VertexAt((rev ? edge.End : edge.Start).Position);
                    var middle = weld.VertexAt(m);
                    var to = weld.VertexAt((rev ? edge.Start : edge.End).Position);
                    var e1 = weld.EdgeBetween(from, middle);
                    var e2 = weld.EdgeBetween(middle, to);
                    loop.Edges[i] = (e1, e1.Start != from);
                    loop.Edges.Insert(i + 1, (e2, e2.Start != middle));
                }
        var flags = set.SelectMany(Topology.EdgesOf).Distinct().ToDictionary(x => x, x => x.Flags);
        e.Faces.RemoveAll(replaced.Contains);
        e.Edges.RemoveAll(mid.ContainsKey);
        var newFaces = new List<Face>();
        foreach (var (pts, src) in made)
        {
            var f = weld.Face(pts, []);
            newFaces.Add(f);
            f.FrontMaterial = src.FrontMaterial;
            f.BackMaterial = src.BackMaterial;
            f.Tag = src.Tag;
            if (f.Normal.Dot(src.Normal) < 0)
                FaceFinder.Reverse(f);
        }
        // A smoothed surface stays smooth: the new inner edges take the smoothing the old ones had.
        if (flags.Values.Any(x => x.HasFlag(EdgeFlags.Smooth)))
            foreach (var edge in newFaces.SelectMany(Topology.EdgesOf).Distinct())
                if (Topology.FacesOf(e, edge).All(newFaces.Contains))
                    edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        Editing.RemoveOrphanVertices(e);
        return e.Faces.Count - before;
    }

    /// <summary>Flip Edge: the diagonal between two triangles turns to join their other two corners.</summary>
    public static bool FlipEdge(Entities e, Edge edge)
    {
        var faces = Topology.FacesOf(e, edge).ToList();
        if (faces.Count != 2 || faces.Any(f => f.Loops.Count != 1 || f.OuterLoop.Edges.Count != 3))
            return false;
        var p = faces[0].OuterLoop.Vertices.First(v => v != edge.Start && v != edge.End);
        var q = faces[1].OuterLoop.Vertices.First(v => v != edge.Start && v != edge.End);
        var (a, b) = (edge.Start.Position, edge.End.Position);
        var up = (faces[0].Normal + faces[1].Normal).Normalized();
        var src = faces[0];
        var flags = edge.Flags;
        e.Faces.RemoveAll(faces.Contains);
        e.Edges.Remove(edge);
        var weld = new Welder(e);
        foreach (var pts in new List<Vec3>[] { [p.Position, q.Position, a], [q.Position, p.Position, b] })
        {
            var f = Up(weld.Face(pts, []), up);
            f.FrontMaterial = src.FrontMaterial;
            f.BackMaterial = src.BackMaterial;
            f.Tag = src.Tag;
        }
        var diagonal = weld.EdgeBetween(weld.VertexAt(p.Position), weld.VertexAt(q.Position));
        diagonal.Flags = flags;
        return true;
    }

    /// <summary>Drape (and Tools on Surface): the path laid on <paramref name="e"/>'s faces along <paramref name="direction"/>, cut
    /// at their edges so each piece lies on one face; returns the runs on the surface (parts off it are dropped).</summary>
    public static List<List<Vec3>> DrapePath(Entities e, IReadOnlyList<Vec3> path, Vec3 direction, bool closed)
    {
        var d = direction.Normalized();
        var (u, w) = Polygon.PlaneAxes(d);
        (double X, double Y) Flat(Vec3 p) => (p.Dot(u), p.Dot(w));
        var faces = e.Faces.Where(f => !f.Hidden && Math.Abs(f.Normal.Normalized().Dot(d)) > 1e-6).ToList();
        var edges = faces.SelectMany(Topology.EdgesOf).Distinct().ToList();

        Vec3? Lift(Vec3 p)
        {
            // The first face met coming from far behind the path along the direction (the surface facing it).
            Vec3? best = null;
            var bestT = double.MaxValue;
            var origin = p - d * 1e7;
            foreach (var f in faces)
            {
                var n = f.Normal.Normalized();
                var denom = n.Dot(d);
                var t = (f.OuterLoop.Points.First() - origin).Dot(n) / denom;
                if (t >= bestT)
                    continue;
                var hit = origin + d * t;
                if (Contains(f, hit, n))
                {
                    bestT = t;
                    best = hit;
                }
            }
            return best;
        }

        var points = closed && path.Count > 2 ? path.Append(path[0]).ToList() : path.ToList();
        var runs = new List<List<Vec3>>();
        var run = new List<Vec3>();
        void Add(Vec3? q)
        {
            if (q is not { } v)
            {
                if (run.Count >= 2)
                    runs.Add(run);
                run = [];
                return;
            }
            if (run.Count == 0 || run[^1].DistanceTo(v) > Tolerance.Length)
                run.Add(v);
        }

        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var (ax, ay) = Flat(a);
            var (bx, by) = Flat(b);
            var cuts = new List<(double T, Vec3 P)>();
            foreach (var edge in edges)
            {
                var (cx, cy) = Flat(edge.Start.Position);
                var (ex, ey) = Flat(edge.End.Position);
                double rx = bx - ax, ry = by - ay, sx = ex - cx, sy = ey - cy;
                var den = rx * sy - ry * sx;
                if (Math.Abs(den) < 1e-12)
                    continue;
                var t = ((cx - ax) * sy - (cy - ay) * sx) / den;
                var s2 = ((cx - ax) * ry - (cy - ay) * rx) / den;
                if (t > 1e-9 && t < 1 - 1e-9 && s2 >= -1e-9 && s2 <= 1 + 1e-9)
                    cuts.Add((t, edge.Start.Position + (edge.End.Position - edge.Start.Position) * Math.Clamp(s2, 0, 1)));
            }
            if (i == 0)
                Add(Lift(a));
            foreach (var (t, p) in cuts.OrderBy(c => c.T))
            {
                // Only crossings on the visible surface count (an edge further back is not where the path lies).
                if (Lift(a + (b - a) * t) is { } l && l.DistanceTo(p) < 0.01)
                    Add(p);
            }
            Add(Lift(b));
        }
        if (run.Count >= 2)
            runs.Add(run);
        return runs;
    }

    private static bool Contains(Face f, Vec3 point, Vec3 n)
    {
        var (u, v) = Polygon.PlaneAxes(n);
        var inside = false;
        foreach (var loop in f.Loops)
        {
            var pts = loop.Points.ToList();
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                double xi = pts[i].Dot(u), yi = pts[i].Dot(v), xj = pts[j].Dot(u), yj = pts[j].Dot(v);
                double px = point.Dot(u), py = point.Dot(v);
                if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
        }
        // On the outline counts as inside (paths cross faces exactly at their edges).
        return inside || f.Loops.SelectMany(l => l.Edges).Any(x => DistanceToSegment(point, x.Edge.Start.Position, x.Edge.End.Position) < Tolerance.Length);
    }

    private static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
    {
        var ab = b - a;
        var t = Math.Clamp((p - a).Dot(ab) / Math.Max(ab.LengthSquared, 1e-18), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    private static Face Up(Face f, Vec3 up)
    {
        if (f.Normal.Dot(up) < 0)
            FaceFinder.Reverse(f);
        return f;
    }

    /// <summary>Each grid cell's diagonal (its triangles' longest side) is soft and smooth; the grid lines stay.</summary>
    private static void SoftenDiagonals(Entities e, List<Face> faces)
    {
        foreach (var f in faces)
        {
            var longest = Topology.EdgesOf(f).MaxBy(x => x.Length)!;
            if (Topology.FacesOf(e, longest).Count() == 2)
                longest.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        }
    }
}

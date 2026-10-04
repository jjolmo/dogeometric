using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Creates the faces new edges close, the way SketchUp does: every closed coplanar loop becomes a face, a loop
/// drawn across a face splits it, and a loop inside a face becomes a new face plus a hole in the old one.
/// </summary>
/// <remarks>
/// For each plane the new edges lie in, the in-plane edges connected to them (and the edges of faces in that plane
/// they touch or fall inside) form a planar graph. Walking it by "next edge clockwise" yields its minimal cycles:
/// counter-clockwise ones bound regions, clockwise ones are the outer boundaries of connected pieces (holes in
/// whatever region surrounds them). Regions become faces when they were covered by a face before or include a
/// new edge; faces they replace hand over their materials, tag and orientation.
/// </remarks>
public static class FaceFinder
{
    private const double PlaneTolerance = 1e-3; // mm

    public static void Update(Entities e, IReadOnlyCollection<Edge> newEdges, Vec3? preferredNormal = null)
    {
        var handled = new HashSet<Edge>();
        foreach (var edge in newEdges)
        {
            if (handled.Contains(edge) || !e.Edges.Contains(edge))
                continue;
            foreach (var plane in CandidatePlanes(e, edge, preferredNormal))
            {
                var used = Reface(e, plane, newEdges);
                handled.UnionWith(used);
            }
        }
    }

    /// <summary>Planes through <paramref name="edge"/> spanned with each non-collinear edge at either end.</summary>
    private static IEnumerable<Plane> CandidatePlanes(Entities e, Edge edge, Vec3? preferredNormal)
    {
        var dir = (edge.End.Position - edge.Start.Position).Normalized();
        var planes = new List<Plane>();
        void Consider(Vec3 normal)
        {
            if (normal.IsZero(1e-9))
                return;
            var n = normal.Normalized();
            var p = new Plane(n, n.Dot(edge.Start.Position));
            if (!planes.Any(q => q.SameAs(p)))
                planes.Add(p);
        }

        // Faces already containing the edge define planes too (an edge drawn on a face splits it).
        foreach (var f in e.Faces)
        {
            var n = f.Normal;
            if (Math.Abs(n.Dot(dir)) < 1e-6 && Math.Abs(n.Dot(edge.Start.Position) - n.Dot(f.OuterLoop.Points.First())) < PlaneTolerance)
                Consider(n);
        }
        foreach (var other in e.Edges)
        {
            if (other == edge)
                continue;
            var shares = other.Start == edge.Start || other.End == edge.Start || other.Start == edge.End || other.End == edge.End;
            if (!shares)
                continue;
            Consider(dir.Cross((other.End.Position - other.Start.Position).Normalized()));
        }
        if (planes.Count == 0 && preferredNormal is { } pn && Math.Abs(pn.Normalized().Dot(dir)) < 1e-6)
            Consider(pn);
        return planes;
    }

    private readonly record struct Plane(Vec3 Normal, double D)
    {
        public bool Contains(Vec3 p) => Math.Abs(Normal.Dot(p) - D) <= PlaneTolerance;

        public bool SameAs(Plane o) =>
            Math.Abs(Math.Abs(Normal.Dot(o.Normal)) - 1) < 1e-9 && Math.Abs(D - Normal.Dot(o.Normal) * o.D) <= PlaneTolerance;
    }

    /// <summary>Rebuilds the faces of one plane around the new edges. Returns the new edges that lie in it.</summary>
    private static List<Edge> Reface(Entities e, Plane plane, IReadOnlyCollection<Edge> newEdges)
    {
        bool InPlane(Edge x) => plane.Contains(x.Start.Position) && plane.Contains(x.End.Position);

        var seeds = newEdges.Where(x => e.Edges.Contains(x) && InPlane(x)).ToList();
        if (seeds.Count == 0)
            return [];

        var (u, v) = Polygon.PlaneAxes(plane.Normal);
        (double X, double Y) To2D(Vec3 p) => (p.Dot(u), p.Dot(v));

        // Faces in this plane that the new edges touch or lie inside: they get rebuilt.
        var planeFaces = e.Faces.Where(f => f.Loops.All(l => l.Edges.All(x => InPlane(x.Edge)))).ToList();
        var seedSet = seeds.ToHashSet();
        var replaced = planeFaces.Where(f =>
            Topology.EdgesOf(f).Any(seedSet.Contains) ||
            seeds.Any(s => ContainsPoint(f, (s.Start.Position + s.End.Position) * 0.5, To2D))).ToList();

        // Edge set: in-plane edges connected to the seeds, plus all edges of replaced faces.
        var edges = new HashSet<Edge>();
        var adjacency = new Dictionary<Vertex, List<Edge>>();
        foreach (var x in e.Edges.Where(InPlane))
        {
            foreach (var vert in new[] { x.Start, x.End })
            {
                if (!adjacency.TryGetValue(vert, out var list))
                    adjacency[vert] = list = [];
                list.Add(x);
            }
        }
        var queue = new Queue<Edge>(seeds.Concat(replaced.SelectMany(Topology.EdgesOf)));
        while (queue.Count > 0)
        {
            var x = queue.Dequeue();
            if (!edges.Add(x))
                continue;
            foreach (var vert in new[] { x.Start, x.End })
                foreach (var next in adjacency.GetValueOrDefault(vert) ?? [])
                    if (!edges.Contains(next))
                        queue.Enqueue(next);
        }

        var cycles = Cycles(edges, To2D);

        // Bounded regions (CCW) and component outlines (CW, become holes of their surrounding region).
        var regions = cycles.Where(c => c.Area > 1e-9).OrderBy(c => c.Area).ToList();
        var outlines = cycles.Where(c => c.Area < -1e-9).ToList();

        // Each piece's outline is a hole of the smallest region of another piece that contains it.
        var holesOf = new Dictionary<Cycle, List<Cycle>>();
        foreach (var outline in outlines)
        {
            var ownEdges = outline.Edges.Select(h => h.Edge).ToHashSet();
            var p = To2D(outline.Points[0]);
            var container = regions
                .Where(r => !r.Edges.Any(h => ownEdges.Contains(h.Edge)) && PointInPolygon(p, r.Points.Select(To2D).ToList()))
                .MinBy(r => r.Area);
            if (container == null)
                continue;
            if (!holesOf.TryGetValue(container, out var list))
                holesOf[container] = list = [];
            list.Add(outline);
        }

        var newFaces = new List<Face>();
        foreach (var region in regions)
        {
            var sample = InteriorPoint(region, To2D);
            var source = replaced.FirstOrDefault(f => ContainsPoint(f, sample, To2D));
            var hasNewEdge = region.Edges.Any(x => seedSet.Contains(x.Edge));
            var existing = planeFaces.Except(replaced).FirstOrDefault(f => SameBoundary(f.OuterLoop, region));
            if (existing != null || (source == null && !hasNewEdge))
                continue;

            var face = new Face
            {
                FrontMaterial = source?.FrontMaterial,
                BackMaterial = source?.BackMaterial,
                Tag = source?.Tag,
            };
            var loop = new FaceLoop();
            loop.Edges.AddRange(region.Edges);
            face.Loops.Add(loop);

            foreach (var outline in holesOf.GetValueOrDefault(region) ?? [])
            {
                var hole = new FaceLoop();
                hole.Edges.AddRange(outline.Edges);
                face.Loops.Add(hole);
            }

            // Orientation: keep the replaced face's; SketchUp's new horizontal faces face down; otherwise keep the
            // winding of the plane the edges were drawn in.
            var wanted = source?.Normal ?? DefaultNormal(plane.Normal, face.Normal);
            if (face.Normal.Dot(wanted) < 0)
                Reverse(face);
            newFaces.Add(face);
        }

        foreach (var f in replaced)
            e.Faces.Remove(f);
        e.Faces.AddRange(newFaces);
        return seeds;
    }

    private static Vec3 DefaultNormal(Vec3 planeNormal, Vec3 current)
    {
        // SketchUp draws new horizontal faces with their front facing down.
        if (Math.Abs(planeNormal.Z) > 1 - 1e-9)
            return -Vec3.UnitZ;
        return current;
    }

    public static void Reverse(Face face)
    {
        foreach (var loop in face.Loops)
        {
            loop.Edges.Reverse();
            for (var i = 0; i < loop.Edges.Count; i++)
                loop.Edges[i] = (loop.Edges[i].Edge, !loop.Edges[i].Reversed);
        }
        (face.FrontMaterial, face.BackMaterial) = (face.BackMaterial, face.FrontMaterial);
    }

    private sealed record Cycle(List<(Edge Edge, bool Reversed)> Edges, List<Vec3> Points, double Area);

    /// <summary>All minimal cycles of the planar graph (each directed edge used once).</summary>
    private static List<Cycle> Cycles(HashSet<Edge> edges, Func<Vec3, (double X, double Y)> to2D)
    {
        // Outgoing half-edges per vertex sorted by angle.
        var outgoing = new Dictionary<Vertex, List<(Edge Edge, bool Reversed, double Angle)>>();
        foreach (var x in edges)
        {
            foreach (var reversed in new[] { false, true })
            {
                var from = reversed ? x.End : x.Start;
                var to = reversed ? x.Start : x.End;
                var a = to2D(from.Position);
                var b = to2D(to.Position);
                if (!outgoing.TryGetValue(from, out var list))
                    outgoing[from] = list = [];
                list.Add((x, reversed, Math.Atan2(b.Y - a.Y, b.X - a.X)));
            }
        }
        foreach (var list in outgoing.Values)
            list.Sort((p, q) => p.Angle.CompareTo(q.Angle));

        var visited = new HashSet<(Edge, bool)>();
        var cycles = new List<Cycle>();
        foreach (var x in edges)
        {
            foreach (var startRev in new[] { false, true })
            {
                if (visited.Contains((x, startRev)))
                    continue;
                var loop = new List<(Edge Edge, bool Reversed)>();
                var cur = (Edge: x, Reversed: startRev);
                var guard = 0;
                while (visited.Add(cur) && guard++ < 100_000)
                {
                    loop.Add(cur);
                    var head = cur.Reversed ? cur.Edge.Start : cur.Edge.End;
                    var list = outgoing[head];
                    // Twin of the current half-edge, then the next one clockwise from it: walks the region on the left.
                    var twinIndex = list.FindIndex(o => o.Edge == cur.Edge && o.Reversed != cur.Reversed);
                    var next = list[(twinIndex - 1 + list.Count) % list.Count];
                    cur = (next.Edge, next.Reversed);
                }
                var cleaned = RemoveAntennae(loop);
                if (cleaned.Count < 3)
                    continue;
                var points = cleaned.Select(h => (h.Reversed ? h.Edge.End : h.Edge.Start).Position).ToList();
                cycles.Add(new Cycle(cleaned, points, SignedArea(points.Select(to2D).ToList())));
            }
        }
        return cycles;
    }

    /// <summary>Drops dangling edges walked out and back within one cycle (edges inside a face don't split it).</summary>
    private static List<(Edge Edge, bool Reversed)> RemoveAntennae(List<(Edge Edge, bool Reversed)> loop)
    {
        var counts = loop.GroupBy(h => h.Edge).ToDictionary(g => g.Key, g => g.Count());
        return loop.Where(h => counts[h.Edge] == 1).ToList();
    }

    private static double SignedArea(List<(double X, double Y)> p)
    {
        double a = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Count];
            a += x1 * y2 - x2 * y1;
        }
        return a / 2;
    }

    private static bool SameBoundary(FaceLoop loop, Cycle c) =>
        loop.Edges.Count == c.Edges.Count && loop.Edges.Select(x => x.Edge).ToHashSet().SetEquals(c.Edges.Select(x => x.Edge));

    private static bool ContainsPoint(Face f, Vec3 p, Func<Vec3, (double X, double Y)> to2D)
    {
        var q = to2D(p);
        if (!PointInPolygon(q, f.OuterLoop.Points.Select(to2D).ToList()))
            return false;
        return !f.InnerLoops.Any(l => PointInPolygon(q, l.Points.Select(to2D).ToList()));
    }

    /// <summary>A point strictly inside a cycle: centre of its first ear-clipped triangle.</summary>
    private static Vec3 InteriorPoint(Cycle c, Func<Vec3, (double X, double Y)> to2D)
    {
        var idx = Polygon.Triangulate(c.Points);
        return idx.Count >= 3 ? (c.Points[idx[0]] + c.Points[idx[1]] + c.Points[idx[2]]) / 3 : c.Points[0];
    }

    private static bool PointInPolygon((double X, double Y) p, List<(double X, double Y)> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}

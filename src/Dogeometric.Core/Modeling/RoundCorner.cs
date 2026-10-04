using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>Fredo6 RoundCorner's three tools.</summary>
public enum RoundCornerMode
{
    /// <summary>Edges become fillets; corners where three or more meet get a rounded (spherical) patch.</summary>
    Round,

    /// <summary>Edges become fillets; at corners the fillets run on until they meet in sharp creases.</summary>
    Sharp,

    /// <summary>Edges and corners are cut flat (one segment).</summary>
    Bevel,
}

/// <summary>RoundCorner (Fredo6): each chosen edge becomes a strip tangent to its faces at the offset; corners get an end cap,
/// a mitre or a corner patch depending on how many chosen edges meet there.</summary>
public static class RoundCorner
{
    public const int MaxSegments = 60;

    public sealed record Result(int Edges, int Corners, IReadOnlyList<string> Problems);

    public static Result Apply(Entities e, IEnumerable<Edge> edges, double offset, int segments, RoundCornerMode mode) =>
        new Builder(e, offset, mode == RoundCornerMode.Bevel ? 1 : Math.Clamp(segments, 1, MaxSegments), mode).Run(edges);

    /// <summary>A face's corner at a vertex: the loop entry leaving it (<see cref="En"/>) and the one arriving (<see cref="Ep"/>).</summary>
    private sealed record Corner(Face Face, FaceLoop Loop, int Index, Edge Ep, Edge En);

    private sealed class Builder(Entities e, double d, int n, RoundCornerMode mode)
    {
        private const double Tol = Tolerance.Length;

        private readonly Dictionary<Edge, List<Face>> _facesOf = [];
        private readonly HashSet<Edge> _sel = [];
        private readonly Dictionary<Vertex, List<Corner>> _fans = [];
        private readonly Dictionary<Corner, Vec3?> _point = [];
        private readonly Dictionary<(Edge, Vertex), List<Vec3>> _profile = [];
        private readonly Dictionary<(Edge, Vertex), (Vec3 Point, Vec3 Dir, double Radius)> _axis = [];
        private readonly Dictionary<Corner, List<Vec3>> _splice = [];
        private readonly List<string> _problems = [];
        private readonly Dictionary<Face, Vec3> _normals = [];
        private readonly Dictionary<FaceLoop, bool> _ccw = [];

        public Result Run(IEnumerable<Edge> edges)
        {
            foreach (var f in e.Faces)
            {
                _normals[f] = f.Normal;
                foreach (var loop in f.Loops)
                {
                    _ccw[loop] = Polygon.Normal(loop.Points.ToList()).Dot(_normals[f]) > 0;
                    foreach (var (edge, _) in loop.Edges)
                    {
                        if (!_facesOf.TryGetValue(edge, out var list))
                            _facesOf[edge] = list = [];
                        if (!list.Contains(f))
                            list.Add(f);
                    }
                }
            }

            foreach (var edge in edges.Distinct().Where(e.Edges.Contains))
            {
                var faces = _facesOf.GetValueOrDefault(edge) ?? [];
                if (faces.Count != 2)
                    _problems.Add("An edge without exactly two faces was left out.");
                else if (Math.Abs(_normals[faces[0]].Dot(_normals[faces[1]])) > 1 - 1e-9)
                    _problems.Add("An edge between coplanar faces was left out.");
                else
                    _sel.Add(edge);
            }

            // Corners whose faces do not close around the vertex (open meshes, reversed faces) cannot be worked out.
            while (true)
            {
                _fans.Clear();
                var bad = new HashSet<Vertex>();
                foreach (var v in _sel.SelectMany(x => new[] { x.Start, x.End }).Distinct())
                {
                    if (Fan(v) is { } fan)
                        _fans[v] = fan;
                    else
                        bad.Add(v);
                }
                if (bad.Count == 0)
                    break;
                _problems.Add($"{bad.Count} corner(s) are not closed or have reversed faces and were left out.");
                _sel.RemoveWhere(x => bad.Contains(x.Start) || bad.Contains(x.End));
            }
            if (_sel.Count == 0)
                return new Result(0, 0, _problems);

            foreach (var (_, fan) in _fans)
                foreach (var c in fan)
                    _point[c] = CornerPoint(c);
            foreach (var edge in _sel)
            {
                Profile(edge, edge.Start, edge.End);
                Profile(edge, edge.End, edge.Start);
            }

            var polygons = new List<(List<Vec3> Points, Vec3 Normal, Face? Source, bool Rounded)>();
            var patches = 0;
            foreach (var (v, fan) in _fans)
                patches += Patch(v, fan, polygons);
            foreach (var edge in _sel)
                Strip(edge, polygons);

            var affected = e.Faces.Where(f => f.Loops.Any(l => l.Vertices.Any(_fans.ContainsKey))).ToList();
            var rebuilt = affected.Select(f => (Face: f, Loops: f.Loops.Select(l => Trace(f, l)).ToList())).ToList();

            var doomed = new HashSet<Edge>(_sel);
            foreach (var edge in e.Edges)
                if (_fans.ContainsKey(edge.Start) || _fans.ContainsKey(edge.End))
                    doomed.Add(edge);
            var affectedSet = affected.ToHashSet();
            e.Faces.RemoveAll(affectedSet.Contains);
            e.Edges.RemoveAll(doomed.Contains);
            Editing.RemoveOrphanVertices(e);

            var weld = new Welder(e);
            var rounded = new HashSet<Face>();
            foreach (var (old, loops) in rebuilt)
            {
                if (loops[0].Count < 3)
                    continue;
                var f = weld.Face(loops[0], loops.Skip(1).Where(l => l.Count >= 3).ToList());
                f.FrontMaterial = old.FrontMaterial;
                f.BackMaterial = old.BackMaterial;
                f.Tag = old.Tag;
                f.Hidden = old.Hidden;
                f.FrontMapping = old.FrontMapping;
                f.BackMapping = old.BackMapping;
            }
            foreach (var (points, normal, source, isRound) in polygons)
            {
                var pts = Clean(points);
                if (pts.Count < 3)
                    continue;
                // A zero normal means the winding was set when the polygon was made (corner patches).
                if (normal != Vec3.Zero && Polygon.Normal(pts).Dot(normal) < 0)
                    pts.Reverse();
                var f = weld.Face(pts, []);
                f.FrontMaterial = source?.FrontMaterial;
                f.BackMaterial = source?.BackMaterial;
                f.Tag = source?.Tag;
                if (isRound)
                    rounded.Add(f);
            }
            if (mode != RoundCornerMode.Bevel)
                Soften(rounded);
            return new Result(_sel.Count, patches, _problems);
        }

        // ------------------------------------------------------------------ topology

        /// <summary>The faces around <paramref name="v"/> in order, each followed by the one across its leaving edge.</summary>
        private List<Corner>? Fan(Vertex v)
        {
            var corners = new List<Corner>();
            foreach (var f in e.Faces)
                foreach (var loop in f.Loops)
                    for (var i = 0; i < loop.Edges.Count; i++)
                    {
                        var (en, rev) = loop.Edges[i];
                        if ((rev ? en.End : en.Start) == v)
                            corners.Add(new Corner(f, loop, i, loop.Edges[(i - 1 + loop.Edges.Count) % loop.Edges.Count].Edge, en));
                    }
            if (corners.Count < 3)
                return null;
            var fan = new List<Corner> { corners[0] };
            while (fan.Count < corners.Count)
            {
                var last = fan[^1];
                var next = corners.Where(c => c.Ep == last.En && c.Face != last.Face).ToList();
                if (next.Count != 1)
                    return null;
                if (next[0] == fan[0])
                    break;
                fan.Add(next[0]);
            }
            var closing = corners.Where(c => c.Ep == fan[^1].En && c.Face != fan[^1].Face).ToList();
            return fan.Count == corners.Count && closing.Count == 1 && closing[0] == fan[0] ? fan : null;
        }

        private Vertex VertexAt(Corner c) => c.Loop.Edges[c.Index] is var (edge, rev) && rev ? edge.End : edge.Start;

        /// <summary>The direction into the face, square to <paramref name="edge"/>, for an edge running along <paramref name="t"/>.</summary>
        private Vec3 Inward(Corner c, Vec3 t) => (_ccw[c.Loop] ? 1 : -1) * _normals[c.Face].Normalized().Cross(t).Normalized();

        /// <summary>Where the corner moves: where the lines at the offset from the chosen edges cross (null if neither is chosen).</summary>
        private Vec3? CornerPoint(Corner c)
        {
            var v = VertexAt(c).Position;
            bool bp = _sel.Contains(c.Ep), bn = _sel.Contains(c.En);
            if (!bp && !bn)
                return null;
            var tp = (v - c.Ep.Other(VertexAt(c)).Position).Normalized();
            var tn = (c.En.Other(VertexAt(c)).Position - v).Normalized();
            var ip = Inward(c, tp);
            var inn = Inward(c, tn);
            var p1 = bp ? v + ip * d : v;
            var p2 = bn ? v + inn * d : v;
            return Intersect(p1, tp, p2, tn) ?? v + (bp ? ip : inn) * d;
        }

        private static Vec3? Intersect(Vec3 p1, Vec3 d1, Vec3 p2, Vec3 d2)
        {
            var w0 = p1 - p2;
            double a = d1.Dot(d1), b = d1.Dot(d2), c = d2.Dot(d2), dd = d1.Dot(w0), ee = d2.Dot(w0);
            var denom = a * c - b * b;
            if (Math.Abs(denom) < 1e-10)
                return null;
            return p1 + d1 * ((b * ee - c * dd) / denom);
        }

        private Corner CornerOf(Vertex v, Face f, Edge edge) => _fans[v].First(c => c.Face == f && (c.Ep == edge || c.En == edge));

        // ------------------------------------------------------------------ strips

        /// <summary>The section of <paramref name="edge"/>'s strip at its end <paramref name="a"/>, from its first face to its second.</summary>
        private void Profile(Edge edge, Vertex a, Vertex b)
        {
            var faces = _facesOf[edge];
            var u = (b.Position - a.Position).Normalized();
            var o = a.Position;
            var p1 = _point[CornerOf(a, faces[0], edge)]!.Value;
            var p2 = _point[CornerOf(a, faces[1], edge)]!.Value;
            double s1 = (p1 - o).Dot(u), s2 = (p2 - o).Dot(u);
            var r1 = p1 - o - u * s1;
            var r2 = p2 - o - u * s2;

            // Rational quadratic Bézier from r1 to r2 with its control point on the edge: an exact circular arc in the
            // square section, and the exact ellipse wherever the strip is cut at a slant.
            var cos = Math.Clamp(r1.Dot(r2) / (r1.Length * r2.Length), -1, 1);
            var w = Math.Sin(Math.Acos(cos) / 2);
            var q = new List<Vec3>();
            for (var j = 0; j <= n; j++)
            {
                var t = (double)j / n;
                if (n == 1 || mode == RoundCornerMode.Bevel)
                {
                    q.Add(r1 + (r2 - r1) * t);
                    continue;
                }
                double b0 = (1 - t) * (1 - t), b1 = 2 * w * t * (1 - t), b2 = t * t;
                q.Add((r1 * b0 + r2 * b2) / (b0 + b1 + b2));
            }

            // Where the section lies along the edge: square to it at corner patches, else in the plane of the two
            // trimmed corners and the vertex (the end cap or the mitre with the next strip).
            var chosen = _fans[a].Select(c => c.En).Count(_sel.Contains);
            var normal = (p1 - o).Cross(p2 - o);
            var useCut = chosen < 3 && normal.Length > 1e-9 * Math.Max(1, d * d) && Math.Abs(normal.Normalized().Dot(u)) > 1e-6;
            var chord = r2 - r1;
            var points = new List<Vec3>();
            foreach (var qj in q)
            {
                double s;
                if (useCut)
                    s = -qj.Dot(normal) / u.Dot(normal);
                else
                {
                    var lambda = chord.LengthSquared < 1e-18 ? 0 : (qj - r1).Dot(chord) / chord.LengthSquared;
                    s = s1 + (s2 - s1) * lambda;
                }
                points.Add(o + qj + u * s);
            }
            points[0] = p1;
            points[^1] = p2;
            _profile[(edge, a)] = points;

            // The fillet's axis: where the normals at the two tangent points cross.
            var n1 = _normals[faces[0]].Normalized();
            var n2 = _normals[faces[1]].Normalized();
            var centre = Intersect(r1, n1, r2, n2) ?? (r1 + r2) / 2;
            _axis[(edge, a)] = (o + centre + u * ((s1 + s2) / 2), u, (r1 - centre).Length);
        }

        private void Strip(Edge edge, List<(List<Vec3>, Vec3, Face?, bool)> polygons)
        {
            var faces = _facesOf[edge];
            var outward = (_normals[faces[0]].Normalized() + _normals[faces[1]].Normalized()).Normalized();
            var pa = _profile[(edge, edge.Start)];
            var pb = _profile[(edge, edge.End)];
            for (var j = 0; j < n; j++)
                polygons.Add(([pa[j], pa[j + 1], pb[j + 1], pb[j]], outward, faces[0], true));
        }

        // ------------------------------------------------------------------ corners

        /// <summary>Fills the hole left around <paramref name="v"/>; returns 1 when that took a corner patch.</summary>
        private int Patch(Vertex v, List<Corner> fan, List<(List<Vec3>, Vec3, Face?, bool)> polygons)
        {
            var hole = new List<Vec3>();
            for (var i = 0; i < fan.Count; i++)
            {
                var c = fan[i];
                hole.Add(_point[c] ?? v.Position);
                if (!_sel.Contains(c.En))
                    continue;
                var profile = _profile[(c.En, v)];
                var forward = _facesOf[c.En][0] == c.Face;
                for (var j = 1; j < profile.Count - 1; j++)
                    hole.Add(profile[forward ? j : profile.Count - 1 - j]);
            }
            var outward = fan.Aggregate(Vec3.Zero, (acc, c) => acc + _normals[c.Face].Normalized()).Normalized();
            var boundary = Clean(hole);
            if (boundary.Count < 3 || Polygon.Area(boundary) < Tol * Tol)
                return 0;

            var nulls = fan.Where(c => _point[c] == null).ToList();
            if (nulls.Count == 1 && Planar(boundary, out var plane) && Math.Abs(plane.Dot(_normals[nulls[0].Face].Normalized())) > 1 - 1e-6)
            {
                // A single untouched face around the corner (the end of a rounded edge): it takes the profile in.
                var k = fan.IndexOf(nulls[0]);
                var before = _point[fan[(k - 1 + fan.Count) % fan.Count]] ?? v.Position;
                var after = _point[fan[(k + 1) % fan.Count]] ?? v.Position;
                var start = hole.FindIndex(p => p.DistanceTo(before) < Tol);
                var path = new List<Vec3>();
                for (var i = 1; i < hole.Count; i++)
                {
                    var p = hole[(start - i + hole.Count) % hole.Count];
                    if (p.DistanceTo(after) < Tol)
                        break;
                    path.Add(p);
                }
                _splice[nulls[0]] = path;
                return 0;
            }

            var chosen = fan.Count(c => _sel.Contains(c.En));
            if (mode == RoundCornerMode.Bevel || chosen < 3 || Planar(boundary, out _))
            {
                if (Planar(boundary, out _))
                    polygons.Add((boundary, outward, fan[0].Face, mode != RoundCornerMode.Bevel));
                else
                    Fan(boundary, Centroid(boundary), outward, fan[0].Face, polygons);
                return chosen >= 3 ? 1 : 0;
            }
            Dome(v, fan, boundary, outward, polygons);
            return 1;
        }

        /// <summary>A corner patch: rings from the hole's edge to its middle, laid on a sphere (Round) or the strips' cylinders (Sharp).</summary>
        private void Dome(Vertex v, List<Corner> fan, List<Vec3> boundary, Vec3 outward, List<(List<Vec3>, Vec3, Face?, bool)> polygons)
        {
            var axes = fan.Where(c => _sel.Contains(c.En)).Select(c => _axis[(c.En, v)]).ToList();
            var centre = axes.Aggregate(Vec3.Zero, (acc, x) => acc + x.Point) / axes.Count;
            var radius = boundary.Average(p => p.DistanceTo(centre));
            var middle = Centroid(boundary);
            var convex = (middle - centre).Dot(outward) > 0;
            // Convex and concave edges meeting: no sphere or cylinders fit both, so the patch just blends inwards.
            var kinds = fan.Where(c => _sel.Contains(c.En)).Select(c => Convex(c.En)).Distinct().Count();
            if (Polygon.Normal(boundary).Dot(outward) < 0)
                boundary = Enumerable.Reverse(boundary).ToList();

            Vec3 Lay(Vec3 p)
            {
                if (kinds > 1)
                    return p;
                var dir = (p - centre).Normalized();
                if (mode == RoundCornerMode.Round)
                    return centre + dir * radius;
                // Sharp: the strips' cylinders carried on into the corner, meeting in creases.
                double? best = null;
                foreach (var (point, axis, r) in axes)
                {
                    var rel = centre - point;
                    var dp = dir - axis * dir.Dot(axis);
                    var rp = rel - axis * rel.Dot(axis);
                    double qa = dp.Dot(dp), qb = 2 * dp.Dot(rp), qc = rp.Dot(rp) - r * r;
                    var disc = qb * qb - 4 * qa * qc;
                    if (qa < 1e-12 || disc < 0)
                        continue;
                    var t = (-qb + Math.Sqrt(disc)) / (2 * qa);
                    if (best == null || (convex ? t < best : t > best))
                        best = t;
                }
                return best is { } tt ? centre + dir * tt : centre + dir * radius;
            }

            var rings = Math.Max(1, (n + 1) / 2);
            var previous = boundary;
            for (var k = 1; k < rings; k++)
            {
                var t = (double)k / rings;
                var ring = boundary.Select(p => Lay(p + (middle - p) * t)).ToList();
                for (var i = 0; i < ring.Count; i++)
                {
                    var j = (i + 1) % ring.Count;
                    polygons.Add(([previous[i], previous[j], ring[j]], Vec3.Zero, fan[0].Face, true));
                    polygons.Add(([previous[i], ring[j], ring[i]], Vec3.Zero, fan[0].Face, true));
                }
                previous = ring;
            }
            Fan(previous, Lay(middle), Vec3.Zero, fan[0].Face, polygons);
        }

        private static void Fan(List<Vec3> ring, Vec3 apex, Vec3 outward, Face? source, List<(List<Vec3>, Vec3, Face?, bool)> polygons)
        {
            for (var i = 0; i < ring.Count; i++)
                polygons.Add(([ring[i], ring[(i + 1) % ring.Count], apex], outward, source, true));
        }

        // ------------------------------------------------------------------ trimmed faces

        /// <summary>A face loop with its corners at the affected vertices moved, split or spliced.</summary>
        private List<Vec3> Trace(Face f, FaceLoop loop)
        {
            var result = new List<Vec3>();
            for (var i = 0; i < loop.Edges.Count; i++)
            {
                var (edge, rev) = loop.Edges[i];
                var v = rev ? edge.End : edge.Start;
                if (!_fans.TryGetValue(v, out var fan))
                {
                    result.Add(v.Position);
                    continue;
                }
                var c = fan.First(x => x.Loop == loop && x.Index == i);
                var own = _point[c] ?? v.Position;
                var k = fan.IndexOf(c);
                var before = fan[(k - 1 + fan.Count) % fan.Count];
                var after = fan[(k + 1) % fan.Count];
                // Along an untouched edge, the neighbour's corner may sit further out: pass through it, so both
                // faces share the same edge pieces.
                if (!_sel.Contains(c.Ep) && (_point[before] ?? v.Position) is var pb && pb.DistanceTo(v.Position) > own.DistanceTo(v.Position) + Tol)
                    result.Add(pb);
                if (_splice.TryGetValue(c, out var path))
                    result.AddRange(path);
                else
                    result.Add(own);
                if (!_sel.Contains(c.En) && (_point[after] ?? v.Position) is var pa && pa.DistanceTo(v.Position) > own.DistanceTo(v.Position) + Tol)
                    result.Add(pa);
            }
            return Clean(result);
        }

        // ------------------------------------------------------------------ helpers

        private static List<Vec3> Clean(List<Vec3> points)
        {
            var list = new List<Vec3>(points);
            var changed = true;
            while (changed && list.Count >= 3)
            {
                changed = false;
                for (var i = 0; i < list.Count && list.Count >= 3; i++)
                {
                    var next = list[(i + 1) % list.Count];
                    var after = list[(i + 2) % list.Count];
                    if (list[i].DistanceTo(next) < Tol)
                    {
                        list.RemoveAt((i + 1) % list.Count);
                        changed = true;
                    }
                    else if (list[i].DistanceTo(after) < Tol)
                    {
                        // A spike (there and straight back) encloses nothing.
                        var a = (i + 1) % list.Count;
                        var b = (i + 2) % list.Count;
                        foreach (var idx in new[] { a, b }.OrderDescending())
                            list.RemoveAt(idx);
                        changed = true;
                    }
                }
            }
            return list.Count >= 3 ? list : [];
        }

        private static bool Planar(List<Vec3> pts, out Vec3 normal)
        {
            normal = Polygon.Normal(pts);
            var origin = pts[0];
            var n = normal;
            return pts.All(p => Math.Abs((p - origin).Dot(n)) < Tol * 10);
        }

        /// <summary>An edge is convex when its second face turns away behind the first one's front.</summary>
        private bool Convex(Edge edge)
        {
            var faces = _facesOf[edge];
            var c = _fans[edge.Start].First(x => x.Face == faces[1] && (x.Ep == edge || x.En == edge));
            var t = (edge.End.Position - edge.Start.Position).Normalized();
            return Inward(c, c.En == edge ? t : -t).Dot(_normals[faces[0]]) < 0;
        }

        private static Vec3 Centroid(List<Vec3> pts) => pts.Aggregate(Vec3.Zero, (a, p) => a + p) / pts.Count;

        /// <summary>Edges between rounded faces become soft and smooth; borders with nearly tangent faces soft (RoundCorner's defaults).</summary>
        private void Soften(HashSet<Face> rounded)
        {
            var facesOf = new Dictionary<Edge, List<Face>>();
            foreach (var f in rounded)
                foreach (var edge in Topology.EdgesOf(f))
                {
                    if (!facesOf.TryGetValue(edge, out var list))
                        facesOf[edge] = list = [];
                    list.Add(f);
                }
            foreach (var (edge, list) in facesOf)
            {
                var all = list.Count == 2 ? list : Topology.FacesOf(e, edge).ToList();
                if (all.Count != 2)
                    continue;
                if (all.All(rounded.Contains))
                    edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                else if (all[0].Normal.Dot(all[1].Normal) > Math.Cos(25 * Math.PI / 180))
                    edge.Flags |= EdgeFlags.Soft;
            }
        }
    }

    /// <summary>Adds faces sharing vertices and edges by position, without scanning the whole collection each time.</summary>
    private sealed class Welder
    {
        private const double Cell = Tolerance.Length * 4;
        private readonly Entities _e;
        private readonly Dictionary<(long, long, long), List<Vertex>> _grid = [];
        private readonly Dictionary<(Vertex, Vertex), Edge> _edges = [];

        public Welder(Entities e)
        {
            _e = e;
            foreach (var v in e.Vertices)
                Add(v);
            foreach (var edge in e.Edges)
                _edges[Key(edge.Start, edge.End)] = edge;
        }

        private static (long, long, long) CellOf(Vec3 p) => ((long)Math.Floor(p.X / Cell), (long)Math.Floor(p.Y / Cell), (long)Math.Floor(p.Z / Cell));

        private void Add(Vertex v)
        {
            var key = CellOf(v.Position);
            if (!_grid.TryGetValue(key, out var list))
                _grid[key] = list = [];
            list.Add(v);
        }

        private static (Vertex, Vertex) Key(Vertex a, Vertex b) => a.GetHashCode() <= b.GetHashCode() ? (a, b) : (b, a);

        private Vertex VertexAt(Vec3 p)
        {
            var (cx, cy, cz) = CellOf(p);
            for (var x = cx - 1; x <= cx + 1; x++)
                for (var y = cy - 1; y <= cy + 1; y++)
                    for (var z = cz - 1; z <= cz + 1; z++)
                        if (_grid.TryGetValue((x, y, z), out var list))
                            foreach (var v in list)
                                if (v.Position.DistanceTo(p) <= Tolerance.Length)
                                    return v;
            var nv = _e.AddVertex(p);
            Add(nv);
            return nv;
        }

        private Edge EdgeBetween(Vertex a, Vertex b)
        {
            var key = Key(a, b);
            if (!_edges.TryGetValue(key, out var edge))
                _edges[key] = edge = _e.AddEdge(a, b);
            return edge;
        }

        public Face Face(IReadOnlyList<Vec3> outer, IReadOnlyList<List<Vec3>> holes)
        {
            var face = new Face();
            face.Loops.Add(Loop(outer));
            foreach (var h in holes)
                face.Loops.Add(Loop(h));
            _e.Faces.Add(face);
            return face;
        }

        private FaceLoop Loop(IReadOnlyList<Vec3> pts)
        {
            var loop = new FaceLoop();
            var verts = pts.Select(VertexAt).ToList();
            for (var i = 0; i < verts.Count; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % verts.Count];
                var edge = EdgeBetween(a, b);
                loop.Edges.Add((edge, edge.Start != a));
            }
            return loop;
        }
    }
}

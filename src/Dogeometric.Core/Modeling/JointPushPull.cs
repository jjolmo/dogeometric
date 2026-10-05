using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>Fredo6 JointPushPull's push-pull modes.</summary>
public enum JointPushPullMode
{
    /// <summary>The faces move together, every face ending up exactly at the offset from where it was (even thickness).</summary>
    Joint,

    /// <summary>Each face moves on its own along its normal.</summary>
    Normal,

    /// <summary>Everything moves along one direction.</summary>
    Vector,

    /// <summary>Everything moves along the faces' average normal, as one compact extrusion.</summary>
    Extrude,

    /// <summary>The faces move as in Joint but their borders slide along the faces around them, which stretch (a
    /// multi-face "smart" push-pull: no new walls).</summary>
    Follow,

    /// <summary>Each face moves along its normal and the sharp edges and corners between them are rounded with the
    /// offset as radius (cylinder strips, sphere patches): a box thickened outwards gets a rounded skin.</summary>
    Round,
}

/// <summary>Which walls join the moved faces to where they started.</summary>
public enum JointPushPullBorders { Contour, Grid, None }

/// <summary>
/// JointPushPull (Fredo6): push-pulls many faces at once, or thickens a surface (Thicken keeps the original faces,
/// so a surface becomes a closed shell of that thickness).
/// </summary>
public static class JointPushPull
{
    public sealed record Options
    {
        public JointPushPullMode Mode { get; init; } = JointPushPullMode.Joint;
        public JointPushPullBorders Borders { get; init; } = JointPushPullBorders.Contour;
        public bool Thicken { get; init; }
        public bool AsGroup { get; init; }

        /// <summary>Vector mode's direction (normalised on use).</summary>
        public Vec3 Direction { get; init; } = Vec3.UnitZ;

        /// <summary>Round mode's segments per rounded edge.</summary>
        public int Segments { get; init; } = 6;
    }

    /// <summary>Push-pulls <paramref name="faces"/> of <paramref name="model"/>'s <paramref name="e"/> by <paramref name="offset"/>; returns the faces made.</summary>
    public static List<Face> Apply(Model model, Entities e, IReadOnlyCollection<Face> faces, double offset, Options o)
    {
        var set = faces.Where(e.Faces.Contains).ToList();
        if (set.Count == 0 || Math.Abs(offset) < Tolerance.Length)
            return [];
        if (o.Mode == JointPushPullMode.Follow)
            return Follow(e, set, offset);
        // Round needs the faces to pull apart at every edge between them; where they would cut into each other it
        // falls back to Joint's mitres.
        if (o.Mode == JointPushPullMode.Round && !RoundsApart(e, set, offset))
            o = o with { Mode = JointPushPullMode.Joint };
        var selected = set.ToHashSet();
        var normals = set.ToDictionary(f => f, f => f.Normal.Normalized());

        // Where each corner goes; Normal mode moves each face on its own, so corners are per face there.
        var shared = new Dictionary<Vertex, Vec3>();
        Vec3 Move(Face f, Vertex v)
        {
            switch (o.Mode)
            {
                case JointPushPullMode.Normal or JointPushPullMode.Round:
                    return v.Position + normals[f] * offset;
                case JointPushPullMode.Vector:
                    return v.Position + o.Direction.Normalized() * offset;
                case JointPushPullMode.Extrude:
                    var average = set.Aggregate(Vec3.Zero, (a, x) => a + normals[x] * x.Area).Normalized();
                    return v.Position + average * offset;
                default:
                    if (!shared.TryGetValue(v, out var p))
                        shared[v] = p = v.Position + JointDisplacement(set.Where(x => x.Loops.Any(l => l.Vertices.Contains(v))).Select(x => normals[x]).ToList(), offset);
                    return p;
            }
        }

        var polygons = new List<(List<Vec3> Outer, List<List<Vec3>> Holes, Face Source)>();
        foreach (var f in set)
        {
            var loops = f.Loops.Select(l => l.Vertices.Select(v => Move(f, v)).ToList()).ToList();
            polygons.Add((loops[0], loops.Skip(1).ToList(), f));
        }

        // Walls: along the selection's outline (Contour), or around every face (Grid; Normal mode needs them).
        var walls = new List<(List<Vec3> Points, Face Source, Vec3 Out)>();
        if (o.Borders != JointPushPullBorders.None)
        {
            foreach (var f in set)
                foreach (var loop in f.Loops)
                    foreach (var (edge, rev) in loop.Edges)
                    {
                        var inSelection = Topology.FacesOf(e, edge).Count(selected.Contains);
                        var grid = (o.Borders == JointPushPullBorders.Grid || o.Mode == JointPushPullMode.Normal) && o.Mode != JointPushPullMode.Round;
                        if (!grid && inSelection > 1)
                            continue;
                        var a = rev ? edge.End : edge.Start;
                        var b = rev ? edge.Start : edge.End;
                        // Outwards: away from the face, in its plane, and along the push.
                        var outward = normals[f].Cross(b.Position - a.Position).Normalized() * -1;
                        walls.Add(([a.Position, b.Position, Move(f, b), Move(f, a)], f, outward));
                    }
        }

        // Round's strips and patches are worked out while the faces are still there to tell edges apart.
        var rounding = o.Mode == JointPushPullMode.Round
            ? Rounding(e, set, offset, Math.Max(1, o.Segments), o.Borders != JointPushPullBorders.None)
            : [];

        var target = e;
        ComponentInstance? group = null;
        if (o.AsGroup)
        {
            var def = new ComponentDefinition { Name = "JointPushPull", IsGroup = true };
            model.Definitions.Add(def);
            group = e.AddInstance(def, Transform.Identity);
            target = def.Entities;
        }

        if (o.Thicken && o.AsGroup)
        {
            // The originals go into the group too, so the group holds the whole shell.
            foreach (var f in set)
            {
                var copy = new Welder(target).Face(f.OuterLoop.Points.ToList(), f.InnerLoops.Select(l => l.Points.ToList()).ToList());
                Copy(f, copy);
                if (offset > 0)
                    FaceFinder.Reverse(copy);
            }
        }
        else if (o.Thicken)
        {
            // The slab lies on the faces' front side: their fronts now face into it, so they turn round.
            if (offset > 0)
                foreach (var f in set)
                    FaceFinder.Reverse(f);
        }
        else if (!o.AsGroup)
        {
            // Classic push-pull: the original faces go (their edges stay where other faces still use them).
            e.Faces.RemoveAll(selected.Contains);
            var orphans = set.SelectMany(Topology.EdgesOf).Distinct().Where(x => !Topology.FacesOf(e, x).Any()).ToList();
            e.Edges.RemoveAll(orphans.Contains);
            Editing.RemoveOrphanVertices(e);
        }

        var weld = new Welder(target);
        var made = new List<Face>();
        foreach (var (outer, holes, src) in polygons)
        {
            var f = weld.Face(outer, holes);
            Copy(src, f);
            // Moving against the front leaves the moved face facing back into the solid: turn it round.
            if (offset < 0 && (o.Thicken || o.AsGroup))
                FaceFinder.Reverse(f);
            made.Add(f);
        }
        var facing = offset < 0 && (o.Thicken || o.AsGroup) ? -1 : 1;
        var rounded = new List<Face>();
        foreach (var (pts, outward) in rounding)
        {
            if (pts.Distinct().Count() < 3)
                continue;
            var f = weld.Face(pts, []);
            if (f.Normal.Dot(outward) * facing < 0)
                FaceFinder.Reverse(f);
            rounded.Add(f);
        }
        made.AddRange(rounded);
        // The rounding reads as one smooth surface, joined smoothly to the faces it rounds.
        Editing.SoftenByAngle(target, rounded.SelectMany(Topology.EdgesOf).Distinct().ToList(), Math.Max(20, 100.0 / Math.Max(1, o.Segments)));
        foreach (var (pts, src, outward) in walls)
        {
            if (pts[0].DistanceTo(pts[3]) < Tolerance.Length && pts[1].DistanceTo(pts[2]) < Tolerance.Length)
                continue;
            var quad = pts.Distinct().ToList();
            if (quad.Count < 3)
                continue;
            var f = weld.Face(quad, []);
            Copy(src, f);
            // A classic push into a solid makes a pocket whose walls face into it; any slab's walls face out.
            var push = !o.Thicken && !o.AsGroup && offset < 0 ? -1 : 1;
            if (f.Normal.Dot(outward) * push < 0)
                FaceFinder.Reverse(f);
            made.Add(f);
        }
        Editing.RemoveOrphanVertices(target);
        return made;
    }

    /// <summary>
    /// A move that puts every one of the faces (unit normals) exactly at <paramref name="offset"/>: the least-squares
    /// solution of n·d = offset, which is exact for up to three independent faces.
    /// </summary>
    public static Vec3 JointDisplacement(IReadOnlyList<Vec3> normals, double offset)
    {
        // Distinct directions only: coplanar neighbours add nothing.
        var dirs = new List<Vec3>();
        foreach (var n in normals)
            if (dirs.All(d => d.Dot(n) < 1 - 1e-9))
                dirs.Add(n);
        if (dirs.Count == 1)
            return dirs[0] * offset;
        // Normal equations (Σ n nᵀ) d = offset Σ n, with a touch of damping for nearly parallel faces.
        double[,] m = new double[3, 3];
        var rhs = Vec3.Zero;
        foreach (var n in dirs)
        {
            double[] v = [n.X, n.Y, n.Z];
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    m[i, j] += v[i] * v[j];
            rhs += n * offset;
        }
        var exact = (double[,])m.Clone();
        for (var i = 0; i < 3; i++)
            m[i, i] += 1e-9;
        var avg = dirs.Aggregate(Vec3.Zero, (a, n) => a + n);
        var solved = Solve(m, rhs);
        // The damping biases the answer by about its size; a few refinement steps against the exact system remove it.
        for (var step = 0; step < 4 && solved is { } x; step++)
        {
            var ax = new Vec3(
                exact[0, 0] * x.X + exact[0, 1] * x.Y + exact[0, 2] * x.Z,
                exact[1, 0] * x.X + exact[1, 1] * x.Y + exact[1, 2] * x.Z,
                exact[2, 0] * x.X + exact[2, 1] * x.Y + exact[2, 2] * x.Z);
            solved = Solve(m, rhs - ax) is { } dx ? x + dx : x;
        }
        // Very sharp folds blow up; fall back to the average direction there.
        if (solved == null || solved.Value.Length > Math.Abs(offset) * 10)
            return avg.Normalized() * offset;
        // Rank-two cases (an edge between two faces) leave the component along the edge free: take none of it.
        return solved.Value;
    }

    /// <summary>
    /// Follow: each corner of the faces moves so that every selected face around it ends at the offset while it stays
    /// in the plane of every other face around it; those faces stretch, and any left bent split into triangles.
    /// </summary>
    private static List<Face> Follow(Entities e, List<Face> set, double offset)
    {
        var selected = set.ToHashSet();
        var around = new Dictionary<Vertex, List<Face>>();
        foreach (var f in e.Faces)
            foreach (var v in f.Loops.SelectMany(l => l.Vertices))
            {
                if (!around.TryGetValue(v, out var list))
                    around[v] = list = [];
                list.Add(f);
            }
        var moves = new Dictionary<Vertex, Vec3>();
        foreach (var v in set.SelectMany(f => f.Loops.SelectMany(l => l.Vertices)).Distinct())
        {
            var faces = around[v];
            var rows = new List<(Vec3 N, double Target, double Weight)>();
            foreach (var f in faces)
            {
                var n = f.Normal.Normalized();
                if (rows.Any(r => Math.Abs(r.N.Dot(n)) > 1 - 1e-9))
                    continue;
                rows.Add(selected.Contains(f) ? (n, offset, 1) : (n, 0, 1000));
            }
            moves[v] = Weighted(rows) ?? JointDisplacement(faces.Where(selected.Contains).Select(f => f.Normal.Normalized()).ToList(), offset);
        }
        var touched = moves.Keys.SelectMany(v => around[v]).Distinct().ToList();
        foreach (var (v, d) in moves)
            v.Position += d;
        foreach (var f in touched)
            FredoScale.SplitIfBent(e, f);
        return [.. set.Where(e.Faces.Contains)];
    }

    /// <summary>Whether every edge between two of the faces opens up when they move by <paramref name="offset"/>.</summary>
    private static bool RoundsApart(Entities e, List<Face> set, double offset)
    {
        var selected = set.ToHashSet();
        foreach (var edge in set.SelectMany(Topology.EdgesOf).Distinct())
        {
            var faces = Topology.FacesOf(e, edge).Where(selected.Contains).ToList();
            if (faces.Count != 2)
                continue;
            var (f1, f2) = (faces[0], faces[1]);
            var n1 = f1.Normal.Normalized();
            if (Math.Abs(n1.Dot(f2.Normal.Normalized())) > 1 - 1e-9)
                continue;
            // f2 falls away behind f1's plane at a convex edge.
            var p = f2.OuterLoop.Points.MaxBy(q => Math.Abs((q - edge.Start.Position).Dot(n1)));
            if ((p - edge.Start.Position).Dot(n1) * offset > 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Round mode's rounding: a strip of <paramref name="segments"/> quads round every edge between two of the faces, a
    /// sphere patch at every corner where three or more meet, and (with walls) a flat fan closing each strip at the
    /// selection's border. Directions on the arcs are normalised blends of the faces' normals, the same on strips and
    /// patches, so they meet exactly.
    /// </summary>
    private static List<(List<Vec3> Points, Vec3 Outward)> Rounding(Entities e, List<Face> set, double offset, int segments, bool caps)
    {
        var selected = set.ToHashSet();
        var made = new List<(List<Vec3>, Vec3)>();
        Vec3 Blend(Vec3 a, Vec3 b, double t) => (a * (1 - t) + b * t).Normalized();
        void Add(List<Vec3> pts, Vec3 outward) => made.Add((pts, outward));

        var facesAt = new Dictionary<Vertex, List<Face>>();
        foreach (var f in set)
            foreach (var v in f.Loops.SelectMany(l => l.Vertices))
            {
                if (!facesAt.TryGetValue(v, out var list))
                    facesAt[v] = list = [];
                list.Add(f);
            }

        foreach (var edge in set.SelectMany(Topology.EdgesOf).Distinct())
        {
            var faces = Topology.FacesOf(e, edge).Where(selected.Contains).ToList();
            if (faces.Count != 2)
                continue;
            var (n1, n2) = (faces[0].Normal.Normalized(), faces[1].Normal.Normalized());
            if (n1.Dot(n2) > 1 - 1e-9)
                continue;
            var (a, b) = (edge.Start.Position, edge.End.Position);
            for (var k = 0; k < segments; k++)
            {
                var (d0, d1) = (Blend(n1, n2, (double)k / segments), Blend(n1, n2, (double)(k + 1) / segments));
                Add([a + d0 * offset, a + d1 * offset, b + d1 * offset, b + d0 * offset], (d0 + d1).Normalized());
            }
            // A strip ending on the selection's border is closed by a flat fan round that end.
            foreach (var end in new[] { edge.Start, edge.End })
                if (caps && Topology.FacesOf(e, edge).Count() == 2 && IsBorder(e, end, selected))
                {
                    var fan = new List<Vec3> { end.Position };
                    for (var k = 0; k <= segments; k++)
                        fan.Add(end.Position + Blend(n1, n2, (double)k / segments) * offset);
                    var along = (end == edge.Start ? a - b : b - a).Normalized();
                    Add(fan, along);
                }
        }

        foreach (var (v, faces) in facesAt)
        {
            var ring = Ring(e, v, faces);
            if (ring == null || ring.Count < 3)
                continue;
            var normals = ring.Select(f => f.Normal.Normalized()).ToList();
            var m = normals.Aggregate(Vec3.Zero, (x, n) => x + n).Normalized();
            for (var i = 0; i < normals.Count; i++)
            {
                var (p, q) = (normals[i], normals[(i + 1) % normals.Count]);
                Vec3 Grid(int x, int y) => v.Position + (m * (segments - x - y) + p * x + q * y).Normalized() * offset;
                for (var x = 0; x < segments; x++)
                    for (var y = 0; x + y < segments; y++)
                    {
                        Add([Grid(x, y), Grid(x + 1, y), Grid(x, y + 1)], ((m + p + q) / 3).Normalized());
                        if (x + y + 2 <= segments)
                            Add([Grid(x + 1, y), Grid(x + 1, y + 1), Grid(x, y + 1)], ((m + p + q) / 3).Normalized());
                    }
            }
        }
        return made;
    }

    private static bool IsBorder(Entities e, Vertex v, HashSet<Face> selected) =>
        e.Faces.Any(f => !selected.Contains(f) && f.Loops.Any(l => l.Vertices.Contains(v)))
        || e.Edges.Any(x => (x.Start == v || x.End == v) && Topology.FacesOf(e, x).Count(selected.Contains) < 2);

    /// <summary>The faces round a corner in order (each sharing an edge with the next), when they close all the way round.</summary>
    private static List<Face>? Ring(Entities e, Vertex v, List<Face> faces)
    {
        var ring = new List<Face> { faces[0] };
        var edges = Topology.EdgesOf(faces[0]).Where(x => x.Start == v || x.End == v).ToList();
        if (edges.Count != 2)
            return null;
        var at = edges[0];
        var current = faces[0];
        while (true)
        {
            var next = faces.FirstOrDefault(f => f != current && Topology.EdgesOf(f).Contains(at));
            if (next == null)
                return null;
            if (next == ring[0])
                return ring.Count == faces.Count ? ring : null;
            ring.Add(next);
            var other = Topology.EdgesOf(next).Where(x => (x.Start == v || x.End == v) && x != at).ToList();
            if (other.Count != 1)
                return null;
            (current, at) = (next, other[0]);
            if (ring.Count > faces.Count)
                return null;
        }
    }

    /// <summary>The least-squares displacement meeting each row's n·d = target, weighted; null when it is undetermined.</summary>
    private static Vec3? Weighted(List<(Vec3 N, double Target, double Weight)> rows)
    {
        var m = new double[3, 3];
        var rhs = Vec3.Zero;
        foreach (var (n, target, w) in rows)
        {
            double[] v = [n.X, n.Y, n.Z];
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    m[i, j] += w * v[i] * v[j];
            rhs += n * (w * target);
        }
        var exact = (double[,])m.Clone();
        for (var i = 0; i < 3; i++)
            m[i, i] += 1e-9;
        var solved = Solve(m, rhs);
        for (var step = 0; step < 4 && solved is { } x; step++)
        {
            var ax = new Vec3(
                exact[0, 0] * x.X + exact[0, 1] * x.Y + exact[0, 2] * x.Z,
                exact[1, 0] * x.X + exact[1, 1] * x.Y + exact[1, 2] * x.Z,
                exact[2, 0] * x.X + exact[2, 1] * x.Y + exact[2, 2] * x.Z);
            solved = Solve(m, rhs - ax) is { } dx ? x + dx : x;
        }
        return solved;
    }

    private static Vec3? Solve(double[,] m, Vec3 b)
    {
        double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        if (Math.Abs(det) < 1e-24)
            return null;
        double D(double[,] a) => a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        double[] r = [b.X, b.Y, b.Z];
        var x = new double[3];
        for (var c = 0; c < 3; c++)
        {
            var a = (double[,])m.Clone();
            for (var i = 0; i < 3; i++)
                a[i, c] = r[i];
            x[c] = D(a) / det;
        }
        return new Vec3(x[0], x[1], x[2]);
    }

    private static void Copy(Face from, Face to)
    {
        to.FrontMaterial = from.FrontMaterial;
        to.BackMaterial = from.BackMaterial;
        to.Tag = from.Tag;
    }
}

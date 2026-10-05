using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>FredoScale's deformations of a selection's bounding box.</summary>
public enum Deformation
{
    /// <summary>The far end of the box scales by the factor, the near end not at all (amount: percent).</summary>
    Taper,

    /// <summary>The far end turns by the angle about the box's axis, the near end not at all (amount: degrees).</summary>
    Twist,

    /// <summary>The box leans: the far end slides sideways by the angle (amount: degrees).</summary>
    Shear,

    /// <summary>The box bends round an arc of that angle (amount: degrees).</summary>
    Bend,

    /// <summary>Box scaling: the near end stays, the rest scales along the axis (amount: percent).</summary>
    Scale,

    /// <summary>Box stretching: everything past the box's middle slides along the axis, the ends keep their shape
    /// (amount: millimetres).</summary>
    Stretch,

    /// <summary>The whole box turns about its axis through the centre (amount: degrees).</summary>
    Rotate,
}

/// <summary>
/// FredoScale (Fredo6): scales, stretches, rotates, tapers, twists, shears or bends geometry along one axis of its bounding box. Faces that stop
/// being flat are split into triangles, as SketchUp does when geometry is bent.
/// </summary>
public static class FredoScale
{
    /// <summary>
    /// Deforms the vertices of <paramref name="items"/> (edges and faces) in <paramref name="e"/> along
    /// <paramref name="axis"/> (0 red, 1 green, 2 blue). Returns how many vertices moved.
    /// </summary>
    public static int Apply(Entities e, IEnumerable<object> items, Deformation kind, int axis, double amount)
    {
        var faces = items.OfType<Face>().ToList();
        var edges = items.OfType<Edge>().Concat(faces.SelectMany(Topology.EdgesOf)).Distinct().ToList();
        var vertices = edges.SelectMany(x => new[] { x.Start, x.End }).Distinct().ToList();
        if (vertices.Count == 0)
            return 0;
        var box = Bounds3.FromPoints(vertices.Select(v => v.Position));
        var a = axis;
        var (b, c) = a switch { 0 => (1, 2), 1 => (2, 0), _ => (0, 1) };
        var length = Get(box.Size, a);
        if (length < Tolerance.Length)
            return 0;
        var centre = box.Center;

        // The faces touched, so those that bend can be split once the vertices have moved.
        var touched = e.Faces.Where(f => f.Loops.Any(l => l.Vertices.Any(vertices.Contains))).ToList();
        foreach (var v in vertices)
            v.Position = Move(v.Position, kind, a, b, c, amount, Get(box.Min, a), length, centre, box);
        foreach (var f in touched)
            SplitIfBent(e, f);
        return vertices.Count;
    }

    /// <summary>The box <see cref="Apply"/> deforms for <paramref name="items"/>.</summary>
    public static Bounds3 BoxOf(IEnumerable<object> items)
    {
        var list = items.ToList();
        var edges = list.OfType<Edge>().Concat(list.OfType<Face>().SelectMany(Topology.EdgesOf));
        return Bounds3.FromPoints(edges.SelectMany(x => new[] { x.Start.Position, x.End.Position }));
    }

    /// <summary>
    /// FredoScale's "to Target": the amount that takes <paramref name="from"/> (a point of the box) to
    /// <paramref name="to"/>, for the deformations that have it (Scale, Taper, Shear, Stretch); null when it can't.
    /// </summary>
    public static double? TargetAmount(Deformation kind, int axis, Bounds3 box, Vec3 from, Vec3 to)
    {
        var (b, _) = axis switch { 0 => (1, 2), 1 => (2, 0), _ => (0, 1) };
        var start = Get(box.Min, axis);
        var length = Get(box.Size, axis);
        var along = Get(from, axis) - start;
        switch (kind)
        {
            case Deformation.Scale when Math.Abs(along) > Tolerance.Length:
                return (Get(to, axis) - start) / along * 100;
            case Deformation.Stretch:
                return Get(to, axis) - Get(from, axis);
            case Deformation.Shear when Math.Abs(along) > Tolerance.Length:
                return Math.Atan((Get(to, b) - Get(from, b)) / along) * 180 / Math.PI;
            case Deformation.Taper when length > Tolerance.Length && along > Tolerance.Length:
            {
                var centre = Get(box.Center, b);
                var off = Get(from, b) - centre;
                if (Math.Abs(off) < Tolerance.Length)
                    return null;
                var ratio = (Get(to, b) - centre) / off;
                return (1 + (ratio - 1) / (along / length)) * 100;
            }
            default:
                return null;
        }
    }

    private static Vec3 Move(Vec3 p, Deformation kind, int a, int b, int c, double amount, double start, double length, Vec3 centre, Bounds3 box)
    {
        var t = (Get(p, a) - start) / length;
        switch (kind)
        {
            case Deformation.Taper:
            {
                var factor = 1 + (amount / 100 - 1) * t;
                var q = p;
                q = Set(q, b, Get(centre, b) + (Get(p, b) - Get(centre, b)) * factor);
                q = Set(q, c, Get(centre, c) + (Get(p, c) - Get(centre, c)) * factor);
                return q;
            }
            case Deformation.Twist:
            {
                var angle = amount * Math.PI / 180 * t;
                double u = Get(p, b) - Get(centre, b), w = Get(p, c) - Get(centre, c);
                var q = Set(p, b, Get(centre, b) + u * Math.Cos(angle) - w * Math.Sin(angle));
                return Set(q, c, Get(centre, c) + u * Math.Sin(angle) + w * Math.Cos(angle));
            }
            case Deformation.Shear:
                return Set(p, b, Get(p, b) + Math.Tan(amount * Math.PI / 180) * (Get(p, a) - start));
            case Deformation.Bend:
            {
                // The box's near face stays; its axis curls round a centre on the b side so the far face turns by the angle.
                var angle = amount * Math.PI / 180;
                if (Math.Abs(angle) < 1e-9)
                    return p;
                var radius = length / angle;
                var pivotB = Get(box.Min, b) - radius;
                var r = Get(p, b) - pivotB;
                var theta = angle * t;
                var q = Set(p, a, start + r * Math.Sin(theta));
                return Set(q, b, pivotB + r * Math.Cos(theta));
            }
            case Deformation.Scale:
                return Set(p, a, start + (Get(p, a) - start) * amount / 100);
            case Deformation.Stretch:
                return t > 0.5 ? Set(p, a, Get(p, a) + amount) : p;
            case Deformation.Rotate:
            {
                var angle = amount * Math.PI / 180;
                double u = Get(p, b) - Get(centre, b), w = Get(p, c) - Get(centre, c);
                var q = Set(p, b, Get(centre, b) + u * Math.Cos(angle) - w * Math.Sin(angle));
                return Set(q, c, Get(centre, c) + u * Math.Sin(angle) + w * Math.Cos(angle));
            }
            default:
                return p;
        }
    }

    private static double Get(Vec3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static Vec3 Set(Vec3 v, int i, double value) => i switch
    {
        0 => new Vec3(value, v.Y, v.Z),
        1 => new Vec3(v.X, value, v.Z),
        _ => new Vec3(v.X, v.Y, value),
    };

    /// <summary>A face whose corners no longer share a plane becomes a fan of triangles (edges between them soft).</summary>
    internal static void SplitIfBent(Entities e, Face f)
    {
        var pts = f.OuterLoop.Points.ToList();
        if (pts.Count <= 3 || f.Loops.Count > 1)
            return;
        var n = Polygon.Normal(pts);
        var origin = pts[0];
        if (pts.All(p => Math.Abs((p - origin).Dot(n)) < Tolerance.Length))
            return;
        var weld = new Welder(e);
        var made = new List<Face>();
        var verts = f.OuterLoop.Vertices.ToList();
        for (var i = 1; i + 1 < verts.Count; i++)
        {
            var t = weld.Face([verts[0].Position, verts[i].Position, verts[i + 1].Position], []);
            t.FrontMaterial = f.FrontMaterial;
            t.BackMaterial = f.BackMaterial;
            t.Tag = f.Tag;
            if (t.Normal.Dot(n) < 0)
                FaceFinder.Reverse(t);
            made.Add(t);
        }
        e.Faces.Remove(f);
        foreach (var edge in made.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).All(made.Contains) && Topology.FacesOf(e, edge).Count() == 2)
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
    }
}

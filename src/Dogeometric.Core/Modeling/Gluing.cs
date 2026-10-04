using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>SketchUp's component gluing: which faces a component may stick to, how it sits on one, and the openings
/// that cutting components show in the faces they are glued to.</summary>
public static class Gluing
{
    /// <summary>Whether a face with this (unit) normal suits the definition's "Glue to" choice.</summary>
    public static bool Accepts(GlueTo glue, Vec3 normal) => glue switch
    {
        GlueTo.Any => true,
        GlueTo.Horizontal => Math.Abs(normal.Z) > 0.999,
        GlueTo.Vertical => Math.Abs(normal.Z) < 0.001,
        GlueTo.Sloped => Math.Abs(normal.Z) is > 0.001 and < 0.999,
        _ => false,
    };

    /// <summary>
    /// The placement of a component glued at <paramref name="point"/> on a face with <paramref name="normal"/>: its
    /// blue axis along the normal, its red axis level (or along red on a horizontal face).
    /// </summary>
    public static Transform OnFace(Vec3 point, Vec3 normal)
    {
        var z = normal.Normalized();
        var x = Math.Abs(z.Z) > 0.999 ? Vec3.UnitX : Vec3.UnitZ.Cross(z).Normalized();
        var y = z.Cross(x);
        return new Transform(x, y, z, point);
    }

    /// <summary>
    /// Make Component with Glue to: if the new component's geometry lies on a face of <paramref name="e"/>, its axes
    /// stand on that face (blue along the normal, origin at the outline's corner) and it glues there. Returns the face.
    /// </summary>
    public static Face? Settle(Model model, Entities e, ComponentInstance inst)
    {
        var pts = inst.Definition.Entities.Vertices.Select(v => inst.Transform.ApplyPoint(v.Position)).ToList();
        foreach (var f in e.Faces)
        {
            var n = f.Normal.Normalized();
            if (!Accepts(inst.Definition.GlueTo, n))
                continue;
            var p0 = f.OuterLoop.Points.First();
            var onPlane = pts.Where(p => Math.Abs((p - p0).Dot(n)) < Tolerance.Length).ToList();
            if (onPlane.Count < 3)
                continue;
            var frame = OnFace(p0, n);
            var toFrame = frame.Inverse();
            var local = onPlane.Select(toFrame.ApplyPoint).ToList();
            var centre = local.Aggregate(Vec3.Zero, (a, p) => a + p) / local.Count;
            var outline = f.OuterLoop.Points.Select(toFrame.ApplyPoint).Select(p => (p.X, p.Y)).ToArray();
            if (!Inside((centre.X, centre.Y), outline))
                continue;
            var origin = frame.ApplyPoint(new Vec3(local.Min(p => p.X), local.Min(p => p.Y), 0));
            Grouping.ChangeAxes(model, inst, frame with { Origin = origin });
            inst.GluedTo = f;
            // The hole the geometry left in the face closes: the component cuts its opening from now on.
            var taken = pts.ToList();
            bool Taken(Vec3 p) => taken.Any(q => q.DistanceTo(p) < Tolerance.Length);
            var holes = f.InnerLoops.Where(l => l.Points.All(Taken)).ToList();
            if (holes.Count > 0)
            {
                f.Loops.RemoveAll(holes.Contains);
                var used = e.Faces.SelectMany(Topology.EdgesOf).ToHashSet();
                var loose = holes.SelectMany(l => l.Edges.Select(x => x.Edge)).Where(x => !used.Contains(x)).ToHashSet();
                e.Edges.RemoveAll(loose.Contains);
                Editing.RemoveOrphanVertices(e);
            }
            return f;
        }
        return null;
    }

    /// <summary>A face in <paramref name="e"/> the instance's red-green plane lies on, facing its blue axis, or null.</summary>
    public static Face? FaceUnder(Entities e, ComponentInstance inst)
    {
        var origin = inst.Transform.ApplyPoint(Vec3.Zero);
        var blue = inst.Transform.ApplyVector(Vec3.UnitZ).Normalized();
        foreach (var f in e.Faces)
        {
            var n = f.Normal.Normalized();
            if (n.Dot(blue) < 0.999 || !Accepts(inst.Definition.GlueTo, n))
                continue;
            if (Math.Abs((origin - f.OuterLoop.Points.First()).Dot(n)) > Tolerance.Length)
                continue;
            var (u, v) = Polygon.PlaneAxes(n);
            var outline = f.OuterLoop.Points.Select(p => (p.Dot(u), p.Dot(v))).ToArray();
            if (Inside((origin.Dot(u), origin.Dot(v)), outline))
                return f;
        }
        return null;
    }

    /// <summary>For each face in <paramref name="e"/>, the openings cut by glued instances (loops in this collection's space).</summary>
    /// <param name="placement">Where instances are drawn instead of their transform (one being dragged), if anywhere.</param>
    public static Dictionary<Face, List<List<Vec3>>> Openings(Entities e, IReadOnlyDictionary<ComponentInstance, Transform>? placement = null)
    {
        var result = new Dictionary<Face, List<List<Vec3>>>();
        foreach (var inst in e.Instances)
        {
            if (inst.GluedTo is not { } face || !inst.Definition.CutsOpening || !e.Faces.Contains(face))
                continue;
            var xf = placement != null && placement.TryGetValue(inst, out var moved) ? moved : inst.Transform;
            foreach (var loop in Footprint(inst.Definition))
            {
                var opening = loop.Select(xf.ApplyPoint).ToList();
                if (!result.TryGetValue(face, out var list))
                    result[face] = list = [];
                if (Fits(face, opening, list))
                    list.Add(opening);
            }
        }
        return result;
    }

    /// <summary>An opening the face can show: inside its outline, clear of its holes and of the other openings.</summary>
    private static bool Fits(Face face, List<Vec3> opening, List<List<Vec3>> others)
    {
        var (u, v) = Polygon.PlaneAxes(face.Normal.Normalized());
        (double, double)[] Flat(IEnumerable<Vec3> pts) => pts.Select(p => (p.Dot(u), p.Dot(v))).ToArray();
        var mine = Flat(opening);
        var outer = Flat(face.OuterLoop.Points);
        if (!mine.All(p => Inside(p, outer)) || Crosses(mine, outer))
            return false;
        foreach (var other in face.InnerLoops.Select(l => Flat(l.Points)).Concat(others.Select(Flat)))
            if (Crosses(mine, other) || mine.Any(p => Inside(p, other)) || other.Any(p => Inside(p, mine)))
                return false;
        return true;
    }

    private static bool Crosses((double X, double Y)[] a, (double X, double Y)[] b)
    {
        static double Cross((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++)
            {
                var (p1, p2) = (a[i], a[(i + 1) % a.Length]);
                var (q1, q2) = (b[j], b[(j + 1) % b.Length]);
                double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
                if (((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9)))
                    return true;
            }
        return false;
    }

    /// <summary>The outermost closed loops of the definition's edges on its red-green plane.</summary>
    public static List<List<Vec3>> Footprint(ComponentDefinition def)
    {
        var flat = def.Entities.Edges
            .Where(x => Math.Abs(x.Start.Position.Z) < Tolerance.Length && Math.Abs(x.End.Position.Z) < Tolerance.Length)
            .Select(x => (x.Start.Position, x.End.Position)).ToList();
        var loops = SectionFill.Loops(flat);
        var flat2 = loops.Select(l => l.Select(p => (p.X, p.Y)).ToArray()).ToList();
        return loops.Where((l, i) => !flat2.Where((_, j) => j != i).Any(other => Inside(flat2[i][0], other))).ToList();
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
}

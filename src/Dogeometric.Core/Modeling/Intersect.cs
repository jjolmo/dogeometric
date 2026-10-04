using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Intersect Faces: where the selected faces cut through other faces, edges are added in the active
/// context, splitting the faces they cross (With Model: against every face; With Selection: only among the
/// selection). Faces inside selected groups and components take part, transformed.
/// </summary>
public static class Intersect
{
    /// <summary>A face in world coordinates.</summary>
    private sealed record WorldFace(Face Source, Transform ToWorld, List<List<Vec3>> Loops, Vec3 Normal, double D, Bounds3 Bounds);

    public static List<Edge> WithModel(Document doc) => Run(doc, Scope.Model);

    public static List<Edge> WithSelection(Document doc) => Run(doc, Scope.Selection);

    /// <summary>With Context: against the faces of the group being edited (or the top level), not inside other groups.</summary>
    public static List<Edge> WithContext(Document doc) => Run(doc, Scope.Context);

    private enum Scope { Model, Selection, Context }

    private static List<Edge> Run(Document doc, Scope scope)
    {
        var ctx = doc.Context;
        var selected = new List<WorldFace>();
        foreach (var item in doc.Selection.Items)
        {
            switch (item)
            {
                case Face f:
                    selected.Add(Make(f, ctx.ToWorld));
                    break;
                case ComponentInstance inst:
                    Collect(inst.Definition.Entities, inst.Transform.Then(ctx.ToWorld), selected);
                    break;
            }
        }
        if (selected.Count == 0)
            return [];

        List<WorldFace> others;
        if (scope == Scope.Selection)
        {
            others = selected;
        }
        else if (scope == Scope.Context)
        {
            others = ctx.Entities.Faces.Where(f => !f.Hidden).Select(f => Make(f, ctx.ToWorld)).ToList();
        }
        else
        {
            others = [];
            Collect(doc.Model.Entities, Transform.Identity, others);
        }

        var segments = new List<(Vec3 A, Vec3 B)>();
        foreach (var a in selected)
        {
            foreach (var b in others)
            {
                if (ReferenceEquals(a.Source, b.Source) && a.ToWorld == b.ToWorld)
                    continue;
                if (!Overlap(a.Bounds, b.Bounds))
                    continue;
                segments.AddRange(FaceFace(a, b));
            }
        }

        var toLocal = ctx.ToWorld.Inverse();
        List<Edge> created = [];
        doc.Operation("Intersect Faces", e =>
        {
            foreach (var (p, q) in segments)
                created.AddRange(StickyGeometry.AddSegment(e, toLocal.ApplyPoint(p), toLocal.ApplyPoint(q)));
            created = created.Distinct().ToList(); // a pair of selected faces meets twice (a with b, b with a)
            FaceFinder.Update(e, created);
        });
        return created;
    }

    /// <summary>
    /// The section cut of the model's active (top-level) section plane: where it slices visible faces, in world
    /// coordinates. Empty when no section is active.
    /// </summary>
    public static List<(Vec3 A, Vec3 B)> SectionCut(Model model)
    {
        var result = new List<(Vec3, Vec3)>();
        if (model.Entities.ActiveSection is not { } plane)
            return result;
        var faces = new List<WorldFace>();
        Collect(model.Entities, Transform.Identity, faces);
        var n = plane.Normal;
        var d = n.Dot(plane.Point);
        foreach (var f in faces)
        {
            var dir = n.Cross(f.Normal);
            if (dir.Length < 1e-9)
                continue;
            dir = dir.Normalized();
            var nf = f.Normal;
            var c = n.Dot(nf);
            var det = 1 - c * c;
            var origin = n * ((d - f.D * c) / det) + nf * ((f.D - d * c) / det);
            foreach (var (from, to) in Intervals(f, origin, dir))
                result.Add((origin + dir * from, origin + dir * to));
        }
        return result;
    }

    private static void Collect(Entities e, Transform xf, List<WorldFace> output)
    {
        foreach (var f in e.Faces)
            if (!f.Hidden)
                output.Add(Make(f, xf));
        foreach (var inst in e.Instances)
            if (!inst.Hidden)
                Collect(inst.Definition.Entities, inst.Transform.Then(xf), output);
    }

    private static WorldFace Make(Face f, Transform xf)
    {
        var loops = f.Loops.Select(l => l.Points.Select(xf.ApplyPoint).ToList()).ToList();
        var normal = Polygon.Normal(loops[0]);
        var bounds = Bounds3.FromPoints(loops[0]);
        return new WorldFace(f, xf, loops, normal, normal.Dot(loops[0][0]), bounds);
    }

    private static bool Overlap(Bounds3 a, Bounds3 b) =>
        a.Min.X <= b.Max.X + Tolerance.Length && b.Min.X <= a.Max.X + Tolerance.Length &&
        a.Min.Y <= b.Max.Y + Tolerance.Length && b.Min.Y <= a.Max.Y + Tolerance.Length &&
        a.Min.Z <= b.Max.Z + Tolerance.Length && b.Min.Z <= a.Max.Z + Tolerance.Length;

    /// <summary>Segments where two planar faces cross: their common line, clipped to the inside of both.</summary>
    private static IEnumerable<(Vec3, Vec3)> FaceFace(WorldFace a, WorldFace b)
    {
        var dir = a.Normal.Cross(b.Normal);
        if (dir.Length < 1e-9)
            yield break; // parallel or coplanar
        dir = dir.Normalized();
        // A point on both planes: solve along the two normals.
        var n1 = a.Normal;
        var n2 = b.Normal;
        var n1n2 = n1.Dot(n2);
        var det = 1 - n1n2 * n1n2;
        var c1 = (a.D - b.D * n1n2) / det;
        var c2 = (b.D - a.D * n1n2) / det;
        var origin = n1 * c1 + n2 * c2;

        var ia = Intervals(a, origin, dir);
        var ib = Intervals(b, origin, dir);
        int i = 0, j = 0;
        while (i < ia.Count && j < ib.Count)
        {
            var lo = Math.Max(ia[i].From, ib[j].From);
            var hi = Math.Min(ia[i].To, ib[j].To);
            // A segment along the outline of both faces is just where they meet (neighbours sharing an edge).
            if (hi - lo > Tolerance.Length && !(OnOutline(a, origin + dir * ((lo + hi) / 2)) && OnOutline(b, origin + dir * ((lo + hi) / 2))))
                yield return (origin + dir * lo, origin + dir * hi);
            if (ia[i].To < ib[j].To)
                i++;
            else
                j++;
        }
    }

    private static bool OnOutline(WorldFace f, Vec3 p)
    {
        foreach (var loop in f.Loops)
        {
            for (var k = 0; k < loop.Count; k++)
            {
                var a = loop[k];
                var d = loop[(k + 1) % loop.Count] - a;
                var t = Math.Clamp((p - a).Dot(d) / d.LengthSquared, 0, 1);
                if ((a + d * t).DistanceTo(p) <= Tolerance.Length)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Parameter intervals along the line (origin, dir) inside the face (even-odd over all loops).</summary>
    private static List<(double From, double To)> Intervals(WorldFace f, Vec3 origin, Vec3 dir)
    {
        // The plane through the line, perpendicular to the face: points of the face's loops cross it.
        var m = f.Normal.Cross(dir);
        var ts = new List<double>();
        foreach (var loop in f.Loops)
        {
            for (var k = 0; k < loop.Count; k++)
            {
                var p = loop[k];
                var q = loop[(k + 1) % loop.Count];
                var sp = m.Dot(p - origin);
                var sq = m.Dot(q - origin);
                if ((sp > 0) == (sq > 0))
                    continue;
                var x = p + (q - p) * (sp / (sp - sq));
                ts.Add(dir.Dot(x - origin));
            }
        }
        ts.Sort();
        var result = new List<(double, double)>();
        for (var k = 0; k + 1 < ts.Count; k += 2)
            if (ts[k + 1] - ts[k] > Tolerance.Length)
                result.Add((ts[k], ts[k + 1]));
        return result;
    }
}

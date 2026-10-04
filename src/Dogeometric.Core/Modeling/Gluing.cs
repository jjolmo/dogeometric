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
    public static Dictionary<Face, List<List<Vec3>>> Openings(Entities e)
    {
        var result = new Dictionary<Face, List<List<Vec3>>>();
        foreach (var inst in e.Instances)
        {
            if (inst.GluedTo is not { } face || !inst.Definition.CutsOpening || !e.Faces.Contains(face))
                continue;
            foreach (var loop in Footprint(inst.Definition))
            {
                if (!result.TryGetValue(face, out var list))
                    result[face] = list = [];
                list.Add(loop.Select(inst.Transform.ApplyPoint).ToList());
            }
        }
        return result;
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

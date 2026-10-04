using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's 3D Text geometry: letter outlines (already flattened to polylines, in the red-green plane) become
/// faces with their holes, optionally extruded upwards. Without fill only the outlines are drawn.
/// </summary>
public static class Text3D
{
    public static void Build(Entities e, IReadOnlyList<IReadOnlyList<Vec3>> contours, bool filled, double extrude)
    {
        var loops = contours.Where(c => c.Count >= 3).ToList();
        foreach (var loop in loops)
            StickyGeometry.DrawEdges(e, loop, closed: true, preferredNormal: Vec3.UnitZ);
        if (!filled)
        {
            e.Faces.Clear();
            return;
        }

        // Faces whose inside lies in an even number of outlines are counters (the hole of an "o"): remove them.
        foreach (var face in e.Faces.ToList())
        {
            var p = InteriorPoint(face);
            var depth = loops.Count(l => Inside(l, p));
            if (depth % 2 == 0)
                e.Faces.Remove(face);
        }
        // Letters face up.
        foreach (var face in e.Faces)
            if (face.Normal.Z < 0)
                FaceFinder.Reverse(face);

        if (Math.Abs(extrude) > Tolerance.Length)
        {
            foreach (var face in e.Faces.ToList())
                PushPull.Apply(e, face, extrude);
            // Curved letter sides look smooth, sharp corners stay, as SketchUp's 3D text does.
            Editing.SoftenByAngle(e, e.Edges.ToList());
        }
    }

    private static Vec3 InteriorPoint(Face f)
    {
        // A triangle's centroid is inside the face (holes included).
        var outer = f.OuterLoop.Points.ToList();
        var holes = f.InnerLoops.Select(l => (IReadOnlyList<Vec3>)l.Points.ToList()).ToList();
        var all = outer.Concat(holes.SelectMany(h => h)).ToList();
        var tri = Polygon.Triangulate(outer, holes);
        if (tri.Count < 3)
            return outer[0];
        // The largest triangle's centroid is the safest sample.
        var best = 0;
        var bestArea = -1.0;
        for (var i = 0; i < tri.Count; i += 3)
        {
            var area = (all[tri[i + 1]] - all[tri[i]]).Cross(all[tri[i + 2]] - all[tri[i]]).Length;
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        return (all[tri[best]] + all[tri[best + 1]] + all[tri[best + 2]]) / 3;
    }

    private static bool Inside(IReadOnlyList<Vec3> loop, Vec3 p)
    {
        var inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = loop[i];
            var b = loop[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.IO;

/// <summary>Closed-mesh checks for exported triangles (what slicers and the solid tools need).</summary>
public static class MeshCheck
{
    public sealed record Report(int Triangles, int Vertices, int BoundaryEdges, int NonManifoldEdges, int MisorientedEdges, double Volume)
    {
        /// <summary>Every edge shared by exactly two triangles, traversed in opposite directions.</summary>
        public bool IsWatertight => Triangles > 0 && BoundaryEdges == 0 && NonManifoldEdges == 0 && MisorientedEdges == 0;
    }

    /// <summary>
    /// Welds vertices closer than <paramref name="tolerance"/> (mm) and checks edge use. Volume is signed: positive
    /// when the faces point outwards.
    /// </summary>
    public static Report Analyze(IReadOnlyList<Triangle> triangles, double tolerance = Tolerance.Length)
    {
        var cell = tolerance * 2;
        var ids = new Dictionary<(long, long, long), List<(Vec3 P, int Id)>>();
        var nextId = 0;

        int Id(Vec3 p)
        {
            var key = ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!ids.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var bucket))
                    continue;
                foreach (var (q, id) in bucket)
                {
                    if (q.DistanceTo(p) <= tolerance)
                        return id;
                }
            }
            if (!ids.TryGetValue(key, out var own))
                ids[key] = own = [];
            own.Add((p, nextId));
            return nextId++;
        }

        var directed = new Dictionary<(int, int), int>();
        double volume = 0;
        foreach (var t in triangles)
        {
            var a = Id(t.A);
            var b = Id(t.B);
            var c = Id(t.C);
            if (a == b || b == c || c == a)
                continue; // degenerate after welding
            foreach (var e in new[] { (a, b), (b, c), (c, a) })
                directed[e] = directed.GetValueOrDefault(e) + 1;
            volume += t.A.Dot(t.B.Cross(t.C)) / 6;
        }

        int boundary = 0, nonManifold = 0, misoriented = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var ((u, v), count) in directed)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!seen.Add(key))
                continue;
            var forward = directed.GetValueOrDefault((u, v));
            var backward = directed.GetValueOrDefault((v, u));
            var uses = forward + backward;
            if (uses == 1)
                boundary++;
            else if (uses > 2)
                nonManifold++;
            else if (forward != 1 || backward != 1)
                misoriented++;
        }
        return new Report(triangles.Count, nextId, boundary, nonManifold, misoriented, volume);
    }
}
